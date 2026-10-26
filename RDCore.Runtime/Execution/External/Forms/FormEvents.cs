using RDCore.External.Automation;
using RDCore.Runtime.Execution.External.Automation;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.Runtime.Execution.External.Forms;

/// <summary>
/// Listens to the events of a hosted form and of its controls, for as long as the form is: an event of a control runs the handler of the form's code named after it
/// (<c>CommandButton1_Click</c>), and an event of the form runs its <c>UserForm_</c> handler (<c>UserForm_Click</c>).
/// </summary>
/// <remarks>
/// <para>
/// A control is an implicit <c>WithEvents</c> member of its form, which is what its handlers are the handlers of: no variable is assigned for it, so the form listens to
/// it when it is hosted, and to the form itself. The events come the way a server's do (<see cref="ServerEventRouter"/>): at once, when a call the program waits for
/// raised them - a click while a modal <c>Show</c> waits - or once the session is open to them otherwise, with the arguments a handler can write to
/// (<c>KeyPress(KeyAscii)</c>, <c>BeforeUpdate(Cancel)</c>) written back. A <c>WithEvents</c> variable the program assigns a control to handles its events too.
/// </para>
/// <para>
/// 🚧 The events of the extender MS-VBA wraps a control in (<c>Enter</c>, <c>Exit</c>, <c>BeforeUpdate</c>, <c>AfterUpdate</c>) are its container's, which the form
/// host does not raise yet.
/// </para>
/// </remarks>
internal static class FormEvents
{
    /// <summary>
    /// Listens to the events of <paramref name="form"/>, hosted for the instance <paramref name="identity"/>, and of each control its designer declares.
    /// </summary>
    /// <param name="session">The session whose code handles the events.</param>
    /// <param name="server">The automation server the form and its controls are objects of.</param>
    /// <param name="identity">The instance of the form's class.</param>
    /// <param name="form">The hosted form.</param>
    /// <param name="designer">What the form's designer declares.</param>
    public static void Listen(IRuntimeSession session, IAutomationServer server, VBRuntimeObjectId identity, object form, FormDescriptor designer)
    {
        if (!session.Symbols.TryGetInstance(identity, out var live))
        {
            return;
        }

        var resolver = session.Symbols.Resolver;
        var forms = SessionForms.Of(session);
        var objects = new ServerObjects(session, server);
        var listened = new List<(object Handle, VBRuntimeObjectId Control, IBindingHandle Holder)>();

        using (session.Turn.Yield())
        {
            // the form's own events: those of the Microsoft Forms UserForm it is, and those of the VBA form object its window raises (Activate, Deactivate), run by its
            // UserForm_ handlers. Its user closing its window is the session's to do, as Unload does - its QueryClose can cancel it.
            var userForm = VBProjectSymbol.ResolveQualifiedType(resolver, FormDesigner.MSFormsLibrary, FormObject.ClassName, StaticSymbol.GlobalUri).Symbol as VBClassModuleSymbol;
            var formObject = FormObject.ClassIn(live.ClassModule.WorkspaceRoot);
            Advise(server, form, new ServerEventRouter(
                session,
                server,
                name => (userForm?.FindEvent(name) ?? formObject.FindEvent(name)) is { } raised
                    ? new ServerEventRoute(raised, HandlerOf(session, identity, $"{FormObject.ClassName}_{name}"))
                    : null,
                (name, arguments) =>
                {
                    if (name != RDCore.External.Forms.FormWindowEvents.Close || session.Callables is not { } callables)
                    {
                        return false;
                    }

                    _ = FormHandlers.Unload(session, callables, identity, FormHandlers.FormControlMenu);
                    return true;
                }));

            foreach (var control in designer.DeclaredControls())
            {
                if (live.ClassModule.Members.FirstOrDefault(member => member.GetProperty(SymbolProperties.DesignerControl)
                        && string.Equals(member.Name, control.Name, StringComparison.OrdinalIgnoreCase)) is not ITypedSymbol { ResolvedType: VBClassType declared } member
                    || HandleOf(server, form, control.Name, session) is not { } handle)
                {
                    continue;
                }

                // the control as the program knows it - the object Me.TextBox1 is - held for as long as the form is, so that what it raises has a source.
                var wrapped = (VBObjectValue)objects.Wrap(handle, declared, resolver);
                var holder = new ValueBindingHandle(wrapped.RuntimeValue);
                session.Objects.AddRef(wrapped.Value, holder);
                _ = session.ExternalObjects.TryBeginListening(wrapped.Value);
                listened.Add((handle, wrapped.Value, holder));

                var controlName = control.Name;
                var controlIdentity = wrapped.Value;
                Advise(server, handle, new ServerEventRouter(session, server, name =>
                    session.Symbols.TryGetInstance(controlIdentity, out var source) && source.ClassModule.FindEvent(name) is { } raised
                        ? new ServerEventRoute(raised, [.. HandlerOf(session, identity, $"{controlName}_{name}"), .. ServerEventRouter.SubscribersOf(session, controlIdentity, raised)])
                        : null));
            }
        }

        forms.Listening(identity, [.. listened.Select(entry => entry.Control)], () =>
        {
            using (session.Turn.Yield())
            {
                server.Unadvise(form);
                foreach (var (handle, _, _) in listened)
                {
                    server.Unadvise(handle);
                }
            }

            foreach (var (_, control, holder) in listened)
            {
                _ = session.ExternalObjects.TryEndListening(control);
                _ = session.ReleaseReference(control, holder);
            }
        });
    }

    // the handler of the form's code an event runs, if the code has one: the instance is what it runs on.
    private static IEnumerable<(VBRuntimeObjectId Subscriber, VBTypeMemberSymbol Handler)> HandlerOf(IRuntimeSession session, VBRuntimeObjectId form, string name)
    {
        if (session.Symbols.TryGetInstance(form, out var live)
            && live.ClassModule.Members.OfType<VBProcedureMemberSymbol>().FirstOrDefault(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)) is { } handler)
        {
            yield return (form, handler);
        }
    }

    private static object? HandleOf(IAutomationServer server, object form, string control, IRuntimeSession session)
    {
        try
        {
            return server.Invoke(form, control, AutomationInvocation.Get, [], [], session.Environment.Culture);
        }
        catch (AutomationException)
        {
            // a control the form does not answer to by its name - one of a library the machine does not have - raises nothing to listen to.
            return null;
        }
    }

    private static void Advise(IAutomationServer server, object source, IAutomationEventSink sink)
    {
        try
        {
            server.Advise(source, sink);
        }
        catch (AutomationException)
        {
            // an object that cannot say what it raises raises nothing the program can handle, and is no worse for having been asked.
        }
    }
}
