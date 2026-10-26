using RDCore.External;
using RDCore.External.Automation;
using RDCore.External.Forms;
using RDCore.Runtime.Execution.External.Automation;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.Runtime.Execution.External.Forms;

/// <summary>
/// Runs the members a form's designer declares on its class (<see cref="FormObject.IsDeclaredBy"/>): its controls, the members of the Microsoft Forms
/// <c>UserForm</c> it is, and those of the VBA form object - with the form host of the platform, which hosts the form in a window of its own.
/// </summary>
/// <remarks>
/// <para>
/// An instance of a form's class is an object of the workspace, whose code is the form's code-behind. The form it is - the Microsoft Forms form, in its window - is an
/// object of the automation server the form host shares, created from what the form's designer declares the first time the program reaches a member of it, and bound to
/// the instance (<see cref="ISessionExternalObjects"/>): when the instance is let go of, so is the form, window and all.
/// </para>
/// <para>
/// What the form host adds to the form is the window, so the members that are the window's are asked of it: <c>Show</c>, <c>Hide</c>, <c>Visible</c>, and the
/// <c>Caption</c>, which is the window's caption and not the Microsoft Forms form's own. Every other member of the <c>UserForm</c> - <c>Controls</c>,
/// <c>InsideWidth</c> - and every control is the form object's, called through the automation server as any server object's member is.
/// </para>
/// <para>
/// The standard library's <c>Load</c> and <c>Unload</c> are answered here too: <c>Unload</c> asks the form (<c>QueryClose</c>), terminates it, destroys its
/// window and lets its default instance go, and leaves an object whose designer's members fail (<see cref="SessionForms"/>).
/// </para>
/// <para>
/// 🚧 Not yet: the <c>UserForms</c> collection; the window's own events (<c>Activate</c>, <c>Deactivate</c>, the close button's <c>QueryClose</c>); the form
/// object's other members (<c>Left</c>, <c>Move</c>, <c>Tag</c>…), which say so.
/// </para>
/// </remarks>
/// <param name="session">The session whose forms these are.</param>
/// <param name="outside">The outside world: its form host, and the automation server the forms are objects of.</param>
/// <param name="automation">What calls a member of an automation server's object: the form's, once the form is bound to its instance.</param>
/// <param name="forms">What the designers of the session's forms declare, by the name of the form's module (<see cref="ProgramImage.Forms"/>).</param>
public sealed class FormCallProvider(IRuntimeSession session, ExternalWorld outside, AutomationCallProvider automation, IReadOnlyDictionary<string, FormDescriptor> forms)
    : IExternalCallProvider
{
    // the members of the VBA form object, which the form host is what has; of them, those it hosts so far.
    private static readonly HashSet<string> FormObjectMembers = new(
        FormObject.Description.Classes.SelectMany(described => described.Members).Select(member => member.Name), StringComparer.OrdinalIgnoreCase);

    // Load and Unload are the standard library's (its Global), and what they load and unload is the forms this provider knows of: they are answered here, ahead of
    // the library's own provider, as the servers' provider answers the members of the enumerators it holds.
    private static readonly string LoadKey = GlobalKey(nameof(IStdGlobalClass.Load));
    private static readonly string UnloadKey = GlobalKey(nameof(IStdGlobalClass.Unload));

    private static string GlobalKey(string member) => StdLibSymbolReader.ExternalTargetOf(typeof(IStdGlobalClass), typeof(IStdGlobalClass).GetMethod(member)!);

    // RPC_E_SERVER_DIED_DNE, what MS-VBA reports for a member of a form that was unloaded: Err.Number -2147418105 (oracle\s41 L51).
    private const int FormUnloaded = unchecked((int)0x80010007);

    /// <summary>
    /// What calls a procedure of the session's code - a form's <c>UserForm_QueryClose</c> and <c>UserForm_Terminate</c>, which <c>Unload</c> runs. Set once the
    /// pipeline has composed it, which needs this provider first.
    /// </summary>
    public ICallableBindingFactory? Bindings { get; set; }

    // Controls.Remove of a hosted form's Controls collection, which MS-VBA's form host refuses for a control of the form's designer (444, oracle\s41 C06, C10): Microsoft
    // Forms itself would remove it.
    private const string RemoveKey = FormDesigner.MSFormsLibrary + ".Controls.Remove/method";

    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request)
        => FormObject.IsDeclaredBy(request.Member)
        || request.Member.GetProperty(SymbolProperties.ExternalTarget) is { } key && (key == LoadKey || key == UnloadKey || key == RemoveKey && DesignedNamesOf(request) is not null);

    // the names a hosted form's designer declares, when the receiver of a request is that form's Controls collection.
    private IReadOnlySet<string>? DesignedNamesOf(ExternalCallRequest request)
        => request.Arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var receiver }, ..]
            && session.ExternalObjects.TryGet(receiver, out var owner, out var handle) && ReferenceEquals(owner, outside.Automation)
            ? SessionForms.Of(session).DesignedNamesOf(handle)
            : null;

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, ISymbolResolver resolver)
    {
        var key = request.Member.GetProperty(SymbolProperties.ExternalTarget)!;
        if (key == LoadKey || key == UnloadKey)
        {
            return LoadOrUnload(request, resolver, unload: key == UnloadKey);
        }

        if (key == RemoveKey)
        {
            return Remove(request, resolver);
        }

        var (form, member, kind) = Parse(key);
        if (request.Arguments is not [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var identity }, ..])
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        try
        {
            // an unloaded form's own variables are still there, but its form is gone, and nothing loads it again (oracle\s41 L15, L51, L52).
            if (SessionForms.Of(session).IsUnloaded(identity))
            {
                throw new AutomationException(FormUnloaded, $"the form '{form}' was unloaded.");
            }

            // what needs no window is answered without making one: a form's name is its module's, and one that was never shown is not visible.
            var bound = session.ExternalObjects.TryGet(identity, out var owner, out _) && ReferenceEquals(owner, outside.Automation);
            switch (member.ToUpperInvariant(), kind)
            {
                case ("NAME", "get"):
                    return RuntimeSemanticsEvaluationResult.Success(new VBStringValue(form));
                case ("VISIBLE", "get") when !bound:
                    return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(false));
            }

            if (FormOf(identity, form, request) is not { } hosted)
            {
                return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, request.CallSite,
                    $"{request.Describe()} could not be made: the designer of '{form}' declares no form to host."));
            }

            return (member.ToUpperInvariant(), kind) switch
            {
                ("SHOW", _) => Show(hosted, request, form),
                ("HIDE", _) => Done(() => outside.Forms.Hide(hosted)),
                ("CAPTION", "get") => RuntimeSemanticsEvaluationResult.Success(new VBStringValue(outside.Forms.GetCaption(hosted))),
                ("CAPTION", "let") => Done(() => outside.Forms.SetCaption(hosted, StringOf(request.Arguments[^1]))),
                ("VISIBLE", "get") => RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(outside.Forms.IsShown(hosted))),
                _ when FormObjectMembers.Contains(member) => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                    VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, request.CallSite, $"{request.Describe()}: the form host does not host '{member}' yet.")),

                // a control, or a member of the Microsoft Forms UserForm: a member of the form object, which the automation server calls.
                _ => automation.Dispatch(request, resolver),
            };
        }
        catch (AutomationException failure)
        {
            // what fails here is the form being hosted - a platform that hosts none, a machine without Microsoft Forms - which is a class that cannot be created (429).
            return RuntimeSemanticsEvaluationResult.Error(AutomationErrors.ToError(failure, request.CallSite, request.Describe(), creating: true));
        }
    }

    // a control the program added is removed, as Microsoft Forms removes it; one the form's designer declares - by its name, or at its position - is not.
    private RuntimeSemanticsEvaluationResult Remove(ExternalCallRequest request, ISymbolResolver resolver)
    {
        if (DesignedNamesOf(request) is not { } designed || request.Arguments.Length < 2
            || !session.ExternalObjects.TryGet(((VBRuntimeValue<VBRuntimeObjectId>)request.Arguments[0]).StoredValue, out _, out var controls))
        {
            return automation.Dispatch(request, resolver);
        }

        try
        {
            var name = VariantOf(request.Arguments[1]) switch
            {
                VBStringValue text => text.Value,
                { } position => outside.Automation.Invoke(
                    outside.Automation.Invoke(controls, "Item", AutomationInvocation.Get, [position.RuntimeValue.BoxedValue], [false], session.Environment.Culture)!,
                    "Name", AutomationInvocation.Get, [], [], session.Environment.Culture) as string,
                _ => null,
            };

            if (name is not null && designed.Contains(name))
            {
                return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.MethodNotApplicableInThisContext, request.CallSite,
                    $"{request.Describe()}: '{name}' is a control of the form's designer, and only a control the program added can be removed."));
            }
        }
        catch (AutomationException)
        {
            // a position that is no control's is the collection's to say, as it says it when it is asked to remove it.
        }

        return automation.Dispatch(request, resolver);
    }

    // Load, which the reference to its argument has done already, and Unload: QueryClose (vbFormCode), which can cancel it; then Terminate, the form's window,
    // and its default instance, which the next reference makes again (oracle\s41 L10-L19).
    private RuntimeSemanticsEvaluationResult LoadOrUnload(ExternalCallRequest request, ISymbolResolver resolver, bool unload)
    {
        if (FormArgument(request, resolver) is not { } identity)
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.CantLoadOrUnloadThisObject, request.CallSite,
                $"{request.Describe()}: the object is no form."));
        }

        if (!unload)
        {
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        return Bindings is { } bindings
            ? FormHandlers.Unload(session, bindings, identity, FormHandlers.FormCode)
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    // the instance of a form's class the argument is, if it is one.
    private VBRuntimeObjectId? FormArgument(ExternalCallRequest request, ISymbolResolver resolver)
    {
        if (request.Arguments is not [var argument, ..])
        {
            return null;
        }

        while (argument is VBRuntimeReference reference && resolver.TryRead(reference.Value, out var cell))
        {
            argument = cell.Value;
        }

        return VBObjectType.TypeInfo.CreateValue(new ValueBindingHandle(argument)) is VBObjectValue { } value && !value.IsNothing()
            && session.Symbols.TryGetInstance(value.Value, out var live) && FormObject.IsFormClass(live.ClassModule)
            ? value.Value
            : null;
    }

    // the form an instance of the form's class is: the one it is bound to, or one created for it now from what the designer declares.
    private object? FormOf(VBRuntimeObjectId identity, string form, ExternalCallRequest request)
    {
        if (session.ExternalObjects.TryGet(identity, out var owner, out var bound) && ReferenceEquals(owner, outside.Automation))
        {
            return bound;
        }

        if (!forms.TryGetValue(form, out var designer))
        {
            return null;
        }

        object created;
        using (session.Turn.Yield())
        {
            created = outside.Forms.Create(FormDefinition.Of(form, designer));
        }

        session.ExternalObjects.Bind(identity, outside.Automation, created);
        FormEvents.Listen(session, outside.Automation, outside.Forms, identity, created, designer);
        return created;
    }

    // Show [Modal]: vbModal unless it says vbModeless, and as the designer's ShowModal says when it says nothing. A modal form is waited for, and the program gives its
    // turn up while it does: what the form raises meanwhile is the answer to the call.
    private RuntimeSemanticsEvaluationResult Show(object hosted, ExternalCallRequest request, string form)
    {
        var modal = request.Arguments.Length > 1 && VariantOf(request.Arguments[1]) is { } argument and not VBMissingValue
            ? Convert.ToInt32(argument.RuntimeValue.BoxedValue, System.Globalization.CultureInfo.InvariantCulture) != 0
            : !forms.TryGetValue(form, out var designer) || (designer.ShowModal ?? true);

        using (session.Turn.Yield())
        {
            outside.Forms.Show(hosted, modal);
        }

        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    private static RuntimeSemanticsEvaluationResult Done(Action call)
    {
        call();
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    private static VBTypedValue? VariantOf(IRuntimeValue argument)
        => VBVariantType.TypeInfo.CreateValue(new ValueBindingHandle(argument)) is VBVariantValue { TypedValue: var value } ? value : null;

    private static string StringOf(IRuntimeValue argument)
        => VBStringType.TypeInfo.CreateValue(new ValueBindingHandle(argument)) is VBStringValue { Value: var text } ? text : string.Empty;

    // `UserForms.Form.Member/kind`, as the form's designer stamped it.
    private static (string Form, string Member, string Kind) Parse(string target)
    {
        var slash = target.LastIndexOf('/');
        var path = target[(FormObject.LibraryName.Length + 1)..slash];
        var dot = path.IndexOf('.');
        return (path[..dot], path[(dot + 1)..], target[(slash + 1)..]);
    }
}
