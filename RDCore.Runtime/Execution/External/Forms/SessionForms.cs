using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Execution.External.Forms;

/// <summary>
/// The forms of a session: those that are loaded, in the order they were, and those that were unloaded - what a UserForm is to the program between the moment it
/// is created and the moment it is let go of.
/// </summary>
/// <remarks>
/// <para>
/// A form is loaded when its instance is created: on <c>New</c>, and on the first reference to its default instance, whatever the reference is (Excel-measured,
/// <c>oracle\s41</c> L01-L08). It is unloaded by <c>Unload</c>, and then it is an object whose own variables are still there but whose form is gone: a member its
/// designer declares fails, and nothing loads it again (L15, L51, L52). It is let go of either way, and its <c>Terminate</c> runs once - at <c>Unload</c>, or when
/// the last reference to a form that was never unloaded goes (L14, L53).
/// </para>
/// <para>
/// The session outlives the pipelines that run its programs, so what it knows of its forms is kept by the session, not by a pipeline.
/// </para>
/// </remarks>
internal sealed class SessionForms
{
    private static readonly ConditionalWeakTable<IRuntimeSession, SessionForms> Sessions = [];

    private readonly object _gate = new();
    private readonly List<VBRuntimeObjectId> _loaded = [];
    private readonly HashSet<VBRuntimeObjectId> _unloaded = [];
    private readonly Dictionary<VBRuntimeObjectId, IBindingHandle> _held = [];
    private readonly Dictionary<VBRuntimeObjectId, (VBRuntimeObjectId[] Controls, Action Leave, Action Stop)> _listening = [];
    private readonly Dictionary<object, (VBRuntimeObjectId Form, HashSet<string> Names)> _designed = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _visualBasic6 = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<VBRuntimeObjectId, IBindingHandle> _loadedHolds = [];
    private readonly HashSet<VBRuntimeObjectId> _unloading = [];
    private readonly List<VBRuntimeObjectId> _modal = [];
    private bool _holding;

    /// <summary>
    /// Records which of a program's forms a Visual Basic 6.0 designer wrote: their lifecycle is VB6's, and their handlers are <c>Form_</c>'s.
    /// </summary>
    /// <param name="forms">What the designers of the program's forms declare, by the name of the form's module.</param>
    public void Declare(IReadOnlyDictionary<string, SDK.Workspace.Forms.FormDescriptor> forms)
    {
        lock (_gate)
        {
            foreach (var (name, designer) in forms)
            {
                if (designer.Kind == SDK.Workspace.Forms.FormDesignerKind.VisualBasic6)
                {
                    _ = _visualBasic6.Add(name);
                }
            }
        }
    }

    /// <summary>
    /// Whether a form's class is a Visual Basic 6.0 form's.
    /// </summary>
    /// <param name="form">The form's class.</param>
    public bool IsVisualBasic6(VBClassModuleSymbol form)
    {
        lock (_gate)
        {
            return _visualBasic6.Contains(form.Name);
        }
    }

    /// <summary>
    /// Loads a Visual Basic 6.0 form: it is added at the end of the loaded forms, which hold a reference to it until it is unloaded. A form that is loaded
    /// already is left as it is.
    /// </summary>
    /// <param name="session">The session the form is of.</param>
    /// <param name="form">The instance of the form's class.</param>
    public void LoadVisualBasic6(IRuntimeSession session, VBRuntimeObjectId form)
    {
        var holder = new ValueBindingHandle(new VBObjectValue(form).RuntimeValue);
        lock (_gate)
        {
            if (_loaded.Contains(form))
            {
                return;
            }

            _loaded.Add(form);
            _loadedHolds[form] = holder;
        }

        session.Objects.AddRef(form, holder);
    }

    /// <summary>
    /// Whether a form is loaded.
    /// </summary>
    /// <param name="form">The instance of the form's class.</param>
    public bool IsLoaded(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            return _loaded.Contains(form);
        }
    }

    /// <summary>
    /// A Visual Basic 6.0 form begins to unload: one that is not loaded, or is being unloaded already, cannot.
    /// </summary>
    /// <param name="form">The instance of the form's class.</param>
    /// <returns>Whether it begins to unload.</returns>
    public bool BeginUnload(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            return _loaded.Contains(form) && _unloading.Add(form);
        }
    }

    /// <summary>
    /// A Visual Basic 6.0 form's unload is over: unloaded, it leaves the loaded forms; cancelled, it stays among them.
    /// </summary>
    /// <param name="form">The instance of the form's class.</param>
    /// <param name="unloaded">Whether it was unloaded.</param>
    public void EndUnload(VBRuntimeObjectId form, bool unloaded)
    {
        lock (_gate)
        {
            _ = _unloading.Remove(form);
            if (unloaded)
            {
                _ = _loaded.Remove(form);
            }
        }
    }

    /// <summary>
    /// The loaded forms let go of a Visual Basic 6.0 form that was unloaded: the last reference to it runs its <c>Terminate</c>.
    /// </summary>
    /// <param name="session">The session the form is of.</param>
    /// <param name="form">The instance of the form's class.</param>
    public RuntimeSemanticsEvaluationResult LetGoOfLoaded(IRuntimeSession session, VBRuntimeObjectId form)
    {
        IBindingHandle? holder;
        lock (_gate)
        {
            if (_loaded.Contains(form) || !_loadedHolds.Remove(form, out holder))
            {
                return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
            }
        }

        _ = session.ReleaseReference(form, holder);
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    /// <summary>
    /// The modal forms being waited for, the topmost last.
    /// </summary>
    public IReadOnlyList<VBRuntimeObjectId> Modal
    {
        get
        {
            lock (_gate)
            {
                return [.. _modal];
            }
        }
    }

    /// <summary>
    /// A form is shown modal, over those that are already.
    /// </summary>
    /// <param name="form">The instance of the form's class.</param>
    public void BeginModal(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            _modal.Add(form);
        }
    }

    /// <summary>
    /// A form's modal show is over.
    /// </summary>
    /// <param name="form">The instance of the form's class.</param>
    public void EndModal(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            var index = _modal.LastIndexOf(form);
            if (index >= 0)
            {
                _modal.RemoveAt(index);
            }
        }
    }

    /// <summary>
    /// Forgets every form, as an <c>End</c> forgets every object: none of them raises anything as it goes.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _loaded.Clear();
            _unloaded.Clear();
            _held.Clear();
            _listening.Clear();
            _designed.Clear();
            _loadedHolds.Clear();
            _unloading.Clear();
            _modal.Clear();
            Collection = null;
            FormsCollection = null;
        }
    }

    /// <summary>
    /// Whether an instance is of a Visual Basic 6.0 form's class.
    /// </summary>
    /// <param name="session">The session the instance is of.</param>
    /// <param name="form">The instance.</param>
    public bool IsVisualBasic6(IRuntimeSession session, VBRuntimeObjectId form)
        => session.Symbols.TryGetInstance(form, out var live) && IsVisualBasic6(live.ClassModule);

    /// <summary>
    /// Holds the messages of the windows of <paramref name="host"/>'s forms while a program of the session runs, from now on (<see cref="RDCore.External.Forms.IFormHost.Hold"/>):
    /// MS-VBA dispatches none until the program waits for them or ends. Done once, when the session's first form is created.
    /// </summary>
    /// <param name="session">The session whose programs hold them.</param>
    /// <param name="host">The host of the session's forms.</param>
    public void HoldWhileRunning(IRuntimeSession session, RDCore.External.Forms.IFormHost host)
    {
        lock (_gate)
        {
            if (_holding)
            {
                return;
            }

            _holding = true;
            _host = host;
        }

        session.Turn.Running += host.Hold;
        host.Hold(session.Turn.IsRunning);
    }

    private RDCore.External.Forms.IFormHost? _host;

    /// <summary>
    /// Hides every loaded form of the session that is shown, as a program that ends does (an <c>End</c> in a handler of one of their events, or a debugger that ended
    /// the program there): the modal form the program waits for returns from its <c>Show</c>, and the program goes on to its end.
    /// </summary>
    /// <remarks>
    /// Hiding runs none of the program's code: no <c>QueryClose</c>, no <c>Deactivate</c>; the events the forms raise as they go find a program that is ending.
    /// </remarks>
    /// <param name="session">The session whose forms they are.</param>
    public void HideAll(IRuntimeSession session)
    {
        RDCore.External.Forms.IFormHost? host;
        VBRuntimeObjectId[] loaded;
        lock (_gate)
        {
            host = _host;
            loaded = [.. _loaded];
        }

        if (host is null)
        {
            return;
        }

        foreach (var form in loaded)
        {
            if (!session.ExternalObjects.TryGet(form, out _, out var hosted) || hosted is null)
            {
                continue;
            }

            using (session.Turn.Yield())
            {
                try
                {
                    if (host.IsShown(hosted))
                    {
                        host.Hide(hosted);
                    }
                }
                catch (RDCore.External.Automation.AutomationException)
                {
                    // a form whose window is gone already is hidden.
                }
            }
        }
    }

    /// <summary>
    /// The <c>Controls</c> collection of a hosted form, and the names of the controls its designer declares: those <c>Controls.Remove</c> cannot remove (444,
    /// <c>oracle\s41</c> C06).
    /// </summary>
    /// <param name="form">The instance of the form's class.</param>
    /// <param name="controls">The handle of its <c>Controls</c> collection.</param>
    /// <param name="names">The names its designer declares.</param>
    public void Designed(VBRuntimeObjectId form, object controls, IEnumerable<string> names)
    {
        lock (_gate)
        {
            _designed[controls] = (form, new HashSet<string>(names, StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// The names of the controls a hosted form's designer declares, by the handle of the form's <c>Controls</c> collection; <see langword="null"/> for a collection that
    /// is no hosted form's.
    /// </summary>
    /// <param name="controls">The handle of a <c>Controls</c> collection.</param>
    public IReadOnlySet<string>? DesignedNamesOf(object controls)
    {
        lock (_gate)
        {
            return _designed.TryGetValue(controls, out var designed) ? designed.Names : null;
        }
    }

    /// <summary>
    /// A hosted form is listened to, and its controls (<see cref="FormEvents"/>), until it is unloaded or let go of.
    /// </summary>
    /// <param name="form">The instance of the form's class.</param>
    /// <param name="controls">Its controls, as the program knows them.</param>
    /// <param name="leave">What makes the focus leave the form, when it is unloaded (<see cref="Leave"/>).</param>
    /// <param name="stop">What stops listening, and lets the controls go.</param>
    public void Listening(VBRuntimeObjectId form, VBRuntimeObjectId[] controls, Action leave, Action stop)
    {
        lock (_gate)
        {
            _listening[form] = (controls, leave, stop);
        }
    }

    /// <summary>
    /// The focus leaves a form that is being unloaded, while it is still listened to: the extender's events of the control that had it - its pending update, its
    /// <c>Exit</c> - after the form's <c>QueryClose</c> and before its <c>Terminate</c> (<c>oracle\s41</c> E01, E05, E10, E11). A form that is not listened to is left
    /// as it is.
    /// </summary>
    /// <param name="form">The instance of the form's class.</param>
    public void Leave(VBRuntimeObjectId form)
    {
        Action? leave;
        lock (_gate)
        {
            leave = _listening.TryGetValue(form, out var listened) ? listened.Leave : null;
        }

        leave?.Invoke();
    }

    /// <summary>
    /// Whether an object is a hosted form that is listened to, or a control such a form listens to: its events are the form's to handle, whatever
    /// <c>WithEvents</c> variable holds it or lets it go.
    /// </summary>
    /// <param name="source">The object.</param>
    public bool IsListened(VBRuntimeObjectId source)
    {
        lock (_gate)
        {
            return _listening.ContainsKey(source) || _listening.Values.Any(listened => listened.Controls.Contains(source));
        }
    }

    /// <summary>
    /// Stops listening to a form and its controls, when it is unloaded or let go of. A form that is not listened to is left as it is.
    /// </summary>
    /// <param name="form">The instance of the form's class.</param>
    public void StopListening(VBRuntimeObjectId form)
    {
        Action? stop;
        lock (_gate)
        {
            stop = _listening.Remove(form, out var listened) ? listened.Stop : null;
            foreach (var controls in _designed.Where(designed => designed.Value.Form.Equals(form)).Select(designed => designed.Key).ToList())
            {
                _ = _designed.Remove(controls);
            }
        }

        stop?.Invoke();
    }

    /// <summary>
    /// The identity of the session's <c>UserForms</c> collection: one object, the same every time it is asked for (<c>oracle\s41</c> U02). Its instance is made
    /// by whatever first asks for it.
    /// </summary>
    public VBRuntimeObjectId? Collection { get; set; }

    /// <summary>
    /// The identity of the session's <c>Forms</c> collection, a Visual Basic 6.0 program's: one object, the same every time it is asked for. Its instance is made
    /// by whatever first asks for it.
    /// </summary>
    public VBRuntimeObjectId? FormsCollection { get; set; }

    /// <summary>
    /// Holds a form the collection loaded (<c>UserForms.Add</c>), which stays loaded with nothing else referring to it (U05), until it is unloaded.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="holder">The reference the collection holds it by.</param>
    public void Hold(VBRuntimeObjectId form, IBindingHandle holder)
    {
        lock (_gate)
        {
            _held[form] = holder;
        }
    }

    /// <summary>
    /// Lets go of a form the collection held, when it is unloaded.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="holder">The reference the collection held it by.</param>
    /// <returns>Whether the collection held it.</returns>
    public bool LetGo(VBRuntimeObjectId form, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IBindingHandle? holder)
    {
        lock (_gate)
        {
            return _held.Remove(form, out holder);
        }
    }

    /// <summary>The forms of <paramref name="session"/>.</summary>
    public static SessionForms Of(IRuntimeSession session) => Sessions.GetValue(session, _ => new SessionForms());

    /// <summary>The forms that are loaded, in the order they were.</summary>
    public IReadOnlyList<VBRuntimeObjectId> Loaded
    {
        get
        {
            lock (_gate)
            {
                return [.. _loaded];
            }
        }
    }

    /// <summary>A form is loaded: its instance was created.</summary>
    public void Load(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            if (!_loaded.Contains(form))
            {
                _loaded.Add(form);
            }
        }
    }

    /// <summary>Whether a form was unloaded.</summary>
    public bool IsUnloaded(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            return _unloaded.Contains(form);
        }
    }

    /// <summary>A form is unloaded: it is no longer loaded, and its object is one whose form is gone.</summary>
    public void Unload(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            _ = _loaded.Remove(form);
            _ = _unloaded.Add(form);
        }
    }

    /// <summary>
    /// A form's object is let go of, and forgotten.
    /// </summary>
    /// <returns>Whether the form was still loaded, and is terminated now; <see langword="false"/> for one that was unloaded, and terminated then.</returns>
    public bool Release(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            if (_unloaded.Remove(form))
            {
                return false;
            }

            return _loaded.Remove(form);
        }
    }
}

/// <summary>
/// The event procedures of a form's own code: <c>UserForm_Initialize</c>, <c>UserForm_QueryClose</c>, <c>UserForm_Terminate</c>, by the name VBA gives them
/// whatever the form is named.
/// </summary>
internal static class FormHandlers
{
    /// <summary>The <c>CloseMode</c> of a form <c>Unload</c> unloads (<strong>MS-VBAL §6.1.1.13</strong> <c>vbFormCode</c>).</summary>
    public const short FormCode = 1;

    /// <summary>The <c>CloseMode</c> of a form its user closes - its close button, Alt+F4 (<strong>MS-VBAL §6.1.1.13</strong> <c>vbFormControlMenu</c>).</summary>
    public const short FormControlMenu = 0;

    /// <summary>
    /// Unloads a form, as <c>Unload</c> does and as its user closing it does (Excel-measured, <c>oracle\s41</c>): its <c>QueryClose</c> first, which can cancel it;
    /// then the focus leaves it (<see cref="SessionForms.Leave"/>); then its <c>Terminate</c>, its window, its default instance - which the next reference makes
    /// again - and the collection's hold on it, if <c>UserForms.Add</c> loaded it. The object stays, for whatever holds it, and its designer's members fail. A form that was unloaded already is left as it is.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="bindings">What calls a procedure of the session's code.</param>
    /// <param name="form">The form's instance.</param>
    /// <param name="closeMode">Why it is unloaded: <see cref="FormCode"/>, <see cref="FormControlMenu"/>.</param>
    public static RuntimeSemanticsEvaluationResult Unload(IRuntimeSession session, ICallableBindingFactory bindings, VBRuntimeObjectId form, short closeMode)
    {
        var forms = SessionForms.Of(session);
        if (forms.IsVisualBasic6(session, form))
        {
            return UnloadVisualBasic6(session, bindings, form, closeMode);
        }

        if (forms.IsUnloaded(form))
        {
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        var asked = QueryClose(session, bindings, form, closeMode, out var cancelled);
        if (!asked.IsSuccess || cancelled)
        {
            return asked.IsSuccess ? RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void) : asked;
        }

        try
        {
            forms.Leave(form);
        }
        catch (RDCore.External.Automation.AutomationException)
        {
            // a form whose window is gone already - its host went with it - has no control to leave.
        }

        forms.Unload(form);
        forms.StopListening(form);
        var terminated = Raise(session, bindings, form, "Terminate");

        // the form's window goes with it; the object stays, for whatever holds it (L15) - but no longer for the collection, if it loaded it (UserForms.Add).
        session.ExternalObjects.Release(form);
        ReleaseDefaultInstance(session, form);
        if (forms.LetGo(form, out var holder))
        {
            _ = session.ReleaseReference(form, holder);
        }

        return terminated.IsSuccess ? RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void) : terminated;
    }

    // a Visual Basic 6.0 form: one that is not loaded, or is being unloaded, is left as it is. QueryUnload, then Unload, each of which can cancel it; then the focus
    // leaves it, its window goes, and it is no longer loaded once its Unload has returned. Its instance stays, with its variables and under its name: the next built-in
    // member loads it again, and its Terminate waits for its last reference.
    private static RuntimeSemanticsEvaluationResult UnloadVisualBasic6(IRuntimeSession session, ICallableBindingFactory bindings, VBRuntimeObjectId form, short unloadMode)
    {
        var forms = SessionForms.Of(session);
        if (!forms.BeginUnload(form))
        {
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        var kept = true;
        try
        {
            var asked = Ask(session, bindings, form, "QueryUnload", [0, unloadMode], out var cancelled);
            if (!asked.IsSuccess || cancelled)
            {
                return asked.IsSuccess ? RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void) : asked;
            }

            var unloading = Ask(session, bindings, form, "Unload", [0], out cancelled);
            if (!unloading.IsSuccess || cancelled)
            {
                return unloading.IsSuccess ? RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void) : unloading;
            }

            try
            {
                forms.Leave(form);
            }
            catch (RDCore.External.Automation.AutomationException)
            {
                // a form whose window is gone already (its host went with it) has no control to leave.
            }

            forms.StopListening(form);
            session.ExternalObjects.Release(form);
            kept = false;
        }
        finally
        {
            forms.EndUnload(form, unloaded: !kept);
        }

        // the loaded forms let go of it: what else holds it (its name, a variable, a running procedure's Me) keeps it, and the last of them runs its Terminate.
        return forms.LetGoOfLoaded(session, form);
    }

    // a form's default instance that is unloaded is let go of, as Set UserForm1 = Nothing lets it go: the next reference to it makes another (L12, L55).
    private static void ReleaseDefaultInstance(IRuntimeSession session, VBRuntimeObjectId form)
    {
        if (!session.Symbols.TryGetInstance(form, out var live))
        {
            return;
        }

        var resolver = session.Symbols.Resolver;
        var instance = new VBPredeclaredInstanceSymbol(live.ClassModule);
        if (!resolver.TryGetAddress(instance, out var address) || !resolver.TryRead(address, out var holder)
            || holder.Value is not VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var held } || !held.Equals(form))
        {
            return;
        }

        var before = ObjectReferences.HeldIn(session, holder);
        holder.SetValue(resolver, VBObjectValue.Nothing.RuntimeValue);
        ObjectReferences.Rebind(session, holder, before);
    }

    /// <summary>
    /// Runs a form's handler of an event that has no arguments, if its code has one.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="bindings">What calls a procedure of the session's code.</param>
    /// <param name="form">The form's instance.</param>
    /// <param name="name">The event: <c>Initialize</c>, <c>Terminate</c>.</param>
    public static RuntimeSemanticsEvaluationResult Raise(IRuntimeSession session, ICallableBindingFactory bindings, VBRuntimeObjectId form, string name)
        => HandlerOf(session, form, name) is { } handler
            ? bindings.ForMember(handler).Call(session.Symbols.Resolver, [new VBObjectValue(form).RuntimeValue])
            : RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);

    /// <summary>
    /// Asks a form whether it may close (<c>UserForm_QueryClose(Cancel, CloseMode)</c>): the handler gets <c>Cancel</c> by reference, and a form whose handler
    /// leaves it anything but 0 stays loaded. A form whose code has no handler closes.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="bindings">What calls a procedure of the session's code.</param>
    /// <param name="form">The form's instance.</param>
    /// <param name="closeMode">Why it closes (<c>VbQueryClose</c>).</param>
    /// <param name="cancelled">Whether the handler cancelled it.</param>
    public static RuntimeSemanticsEvaluationResult QueryClose(
        IRuntimeSession session, ICallableBindingFactory bindings, VBRuntimeObjectId form, short closeMode, out bool cancelled)
        => Ask(session, bindings, form, "QueryClose", [0, closeMode], out cancelled);

    // runs a form's handler of an event whose first argument is Cancel, by reference, with the rest given: whatever the handler leaves in Cancel but 0 cancels what
    // the event asks about. A form whose code has no handler cancels nothing.
    private static RuntimeSemanticsEvaluationResult Ask(
        IRuntimeSession session, ICallableBindingFactory bindings, VBRuntimeObjectId form, string name, short[] given, out bool cancelled)
    {
        cancelled = false;
        if (HandlerOf(session, form, name) is not { } handler)
        {
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        // MS-VBAL §5.4.2.20: an argument passed by reference is a location the handler can write to, which is where the cancellation is read from.
        var parameters = RuntimeProcedureInvoker.GetParameters(handler).Where(parameter => parameter.Name != "Me").ToArray();
        var values = given.Select(value => new VBIntegerValue(value)).ToArray();
        var temporaries = new List<MemoryAddress>();
        var arguments = new List<IRuntimeValue> { new VBObjectValue(form).RuntimeValue };
        MemoryAddress? cancel = null;
        try
        {
            for (var index = 0; index < parameters.Length; index++)
            {
                var value = index < values.Length ? values[index] : new VBIntegerValue(0);
                if (RuntimeProcedureInvoker.IsByRef(parameters[index].ParameterKind)
                    && session.Storage.TryAllocate(VBIntegerType.TypeInfo.DefaultValue.Size, new ValueBindingHandle(value.RuntimeValue), out var address))
                {
                    temporaries.Add(address);
                    cancel ??= index == 0 ? address : null;
                    arguments.Add(new VBRuntimeReference(address));
                }
                else
                {
                    arguments.Add(value.RuntimeValue);
                }
            }

            var handled = bindings.ForMember(handler).Call(session.Symbols.Resolver, [.. arguments]);
            if (!handled.IsSuccess)
            {
                return handled;
            }

            cancelled = cancel is { } location && session.Symbols.Resolver.TryRead(location, out var cell)
                && VBIntegerType.TypeInfo.CreateValue(new ValueBindingHandle(cell.Value)) is VBIntegerValue { Value: not 0 };
            return handled;
        }
        finally
        {
            foreach (var address in temporaries)
            {
                _ = session.Storage.TryDeallocate(address);
            }
        }
    }

    // UserForm_<event>: VBA names a form's event procedures after the UserForm, not after the form; VB6 names them after the Form, Form_<event>.
    private static VBTypeMemberSymbol? HandlerOf(IRuntimeSession session, VBRuntimeObjectId form, string name)
        => session.Symbols.TryGetInstance(form, out var live)
            ? live.ClassModule.Members.OfType<VBTypeMemberSymbol>().FirstOrDefault(member =>
                member is VBProcedureMemberSymbol && string.Equals(member.Name, $"{PrefixOf(session, live.ClassModule)}_{name}", StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>
    /// What a form's event procedures are named after: <c>Form</c> for a Visual Basic 6.0 form, <c>UserForm</c> for any other.
    /// </summary>
    /// <param name="session">The session the form is of.</param>
    /// <param name="form">The form's class.</param>
    public static string PrefixOf(IRuntimeSession session, VBClassModuleSymbol form)
        => SessionForms.Of(session).IsVisualBasic6(form) ? "Form" : SDK.Workspace.Forms.FormObject.ClassName;
}
