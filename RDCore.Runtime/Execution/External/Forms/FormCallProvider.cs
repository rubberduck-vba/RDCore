using RDCore.External;
using RDCore.External.Automation;
using RDCore.External.Forms;
using RDCore.Runtime.Execution.External.Automation;
using RDCore.Runtime.LanguageCore.StdLib;
using RDCore.Runtime.LanguageCore.StdLib.Abstract;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Workspace.Forms;
using RDCore.Runtime.LanguageCore.Errors;

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
/// What the form host adds to the form is the window, so the members that are the window's are asked of it: <c>Show</c>, <c>Hide</c>, <c>Visible</c>, the
/// <c>Caption</c>, which is the window's caption and not the Microsoft Forms form's own, and <c>Left</c>, <c>Top</c>, <c>Width</c> and <c>Height</c>, which are
/// where the window is and its size. Every other member of the <c>UserForm</c> (<c>Controls</c>, <c>InsideWidth</c>) and every control is the form object's,
/// called through the automation server as any server object's member is.
/// </para>
/// <para>
/// The standard library's <c>Load</c> and <c>Unload</c> are answered here too: <c>Unload</c> asks the form (<c>QueryClose</c>), terminates it, destroys its
/// window and lets its default instance go, and leaves an object whose designer's members fail (<see cref="SessionForms"/>).
/// </para>
/// <para>
/// A Visual Basic 6.0 form follows VB6's lifecycle. The use of its name makes its instance without loading it; any member of its form, <c>Load</c>, <c>Show</c>
/// and <c>Hide</c> load it, and its <c>Form_Load</c> runs inside the use. <c>Unload</c> keeps its instance, its variables and its name, and the next member of
/// its form loads it again (measured in VB6). Its <c>Left</c>, <c>Top</c>, <c>Width</c> and <c>Height</c> are members of its own object model, which its
/// server calls, as it does every other member of the form.
/// </para>
/// <para>
/// 🚧 Not yet: the <c>UserForms</c> collection; the window's own events (<c>Activate</c>, <c>Deactivate</c>, the close button's <c>QueryClose</c>); the form
/// object's other members (<c>Move</c>, <c>Tag</c>…), which say so.
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

    // what Load and Unload of an element of a Visual Basic 6.0 control array call, with the array and the index: the array's server loads and unloads its elements.
    private static readonly string LoadElementKey = GlobalKey(nameof(IStdGlobalClass.LoadElement));
    private static readonly string UnloadElementKey = GlobalKey(nameof(IStdGlobalClass.UnloadElement));

    private static string GlobalKey(string member) => StdLibSymbolReader.ExternalTargetOf(typeof(IStdGlobalClass), typeof(IStdGlobalClass).GetMethod(member)!);

    // the VB library's global Forms, a Visual Basic 6.0 program's loaded forms: the session's one collection of them, answered here as UserForms is by the
    // standard library.
    private const string FormsKey = "VB.Global.Forms/get";

    // RPC_E_SERVER_DIED_DNE, what MS-VBA reports for a member of a form that was unloaded: Err.Number -2147418105 (oracle\s41 L51).
    private const int FormUnloaded = unchecked((int)0x80010007);

    // the session's forms, which know which of them a Visual Basic 6.0 designer wrote: their lifecycle and their handlers' names are VB6's.
    private readonly SessionForms _forms = Declared(session, forms);

    private static SessionForms Declared(IRuntimeSession session, IReadOnlyDictionary<string, FormDescriptor> forms)
    {
        var known = SessionForms.Of(session);
        known.Declare(forms);
        return known;
    }

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
        || request.Member.GetProperty(SymbolProperties.ExternalTarget) is { } key
            && (key == LoadKey || key == UnloadKey || key == LoadElementKey || key == UnloadElementKey || key == FormsKey
                || key == RemoveKey && DesignedNamesOf(request) is not null);

    // the names a hosted form's designer declares, when the receiver of a request is that form's Controls collection.
    private IReadOnlySet<string>? DesignedNamesOf(ExternalCallRequest request)
        => request.Arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var receiver }, ..]
            && session.ExternalObjects.TryGet(receiver, out var owner, out var handle) && ReferenceEquals(owner, outside.Automation)
            ? SessionForms.Of(session).DesignedNamesOf(handle)
            : null;

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var key = request.Member.GetProperty(SymbolProperties.ExternalTarget)!;
        if (key == LoadKey || key == UnloadKey)
        {
            return LoadOrUnload(request, resolver, unload: key == UnloadKey);
        }

        if (key == LoadElementKey || key == UnloadElementKey)
        {
            return LoadOrUnloadElement(request, resolver, unload: key == UnloadElementKey);
        }

        if (key == RemoveKey)
        {
            return Remove(request, resolver);
        }

        if (key == FormsKey)
        {
            return FormsCollection();
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

            // what needs no window is answered without making one: a form's name is its module's, and one that was never shown is not visible. A Visual Basic 6.0
            // form is loaded by any member of its form, those two included.
            var visualBasic6 = IsVisualBasic6(form);
            var bound = session.ExternalObjects.TryGet(identity, out var owner, out _) && ReferenceEquals(owner, outside.Automation);
            switch (member.ToUpperInvariant(), kind)
            {
                case ("NAME", "get") when !visualBasic6:
                    return RuntimeSemanticsEvaluationResult.Success(new VBStringValue(form));
                case ("VISIBLE", "get") when !bound && !visualBasic6:
                    return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(false));

                // a program run unattended shows no form, modal or not, in the process or through an external host, and makes none to show: nobody is there to
                // answer it, and the run fails (IRuntimeSession.RunsUnattended).
                case ("SHOW", _) when session.RunsUnattended:
                    return RuntimeSemanticsEvaluationResult.Error(Unattended.NobodyToAnswer(session, $"{form}.Show", request.CallSite));
            }

            if (FormOf(identity, form, request) is not { } hosted)
            {
                return NoFormToHost(request, form);
            }

            if (visualBasic6 && Loading(identity, request) is { IsSuccess: false } failed)
            {
                return failed;
            }

            return (member.ToUpperInvariant(), kind) switch
            {
                ("NAME", "get") => RuntimeSemanticsEvaluationResult.Success(new VBStringValue(form)),
                ("SHOW", _) when visualBasic6 => ShowVisualBasic6(hosted, identity, request, resolver),
                ("HIDE", _) when visualBasic6 => HideVisualBasic6(hosted, identity, request),
                ("SHOW", _) => Show(hosted, request, form),
                ("HIDE", _) => Done(() => outside.Forms.Hide(hosted)),
                ("CAPTION", "get") => RuntimeSemanticsEvaluationResult.Success(new VBStringValue(outside.Forms.GetCaption(hosted))),
                ("CAPTION", "let") => Done(() => outside.Forms.SetCaption(hosted, StringOf(request.Arguments[^1]))),
                ("VISIBLE", "get") => RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(outside.Forms.IsShown(hosted))),
                ("LEFT" or "TOP" or "WIDTH" or "HEIGHT", "get") when !visualBasic6 =>
                    RuntimeSemanticsEvaluationResult.Success(new VBSingleValue(Side(outside.Forms.GetBounds(hosted), member))),
                ("LEFT" or "TOP" or "WIDTH" or "HEIGHT", "let") when !visualBasic6 =>
                    Done(() => outside.Forms.SetBounds(hosted, WithSide(outside.Forms.GetBounds(hosted), member, SingleOf(request.Arguments[^1])))),

                // a member of the VBA form object that the form host does not host raises 438 on a UserForm; a Visual Basic 6.0 form's own object model
                // has it, and its server calls it.
                _ when FormObjectMembers.Contains(member) && !visualBasic6 => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
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
    private RuntimeSemanticsEvaluationResult Remove(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
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

    // Load, which the reference to its argument has done already for a UserForm, and does now for a Visual Basic 6.0 form; and Unload: QueryClose (vbFormCode),
    // which can cancel it; then Terminate, the form's window, and its default instance, which the next reference makes again (Excel-measured). A Visual
    // Basic 6.0 form's Unload is its own (FormHandlers.Unload).
    private RuntimeSemanticsEvaluationResult LoadOrUnload(ExternalCallRequest request, IRuntimeSymbolResolver resolver, bool unload)
    {
        if (request.Arguments is not [var argument, ..] || FormArgument(argument, resolver) is not { } identity)
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.CantLoadOrUnloadThisObject, request.CallSite,
                $"{request.Describe()}: the object is no form."));
        }

        if (!unload)
        {
            return session.Symbols.TryGetInstance(identity, out var live) && _forms.IsVisualBasic6(live.ClassModule)
                ? Load(identity, live.ClassModule.Name, request)
                : RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        return Bindings is { } bindings
            ? FormHandlers.Unload(session, bindings, identity, FormHandlers.FormCode)
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    // Load and Unload of an element of a control array, given the array and the index: its server loads or unloads the element, and raises what VB6 raises.
    // Any object other than a control array of a form's server has no element to load (361).
    private RuntimeSemanticsEvaluationResult LoadOrUnloadElement(ExternalCallRequest request, IRuntimeSymbolResolver resolver, bool unload)
    {
        if (request.Arguments is not [var array, var index] || ObjectArgument(array, resolver) is not { } identity
            || !session.ExternalObjects.TryGet(identity, out var owner, out var handle) || !ReferenceEquals(owner, outside.Automation))
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.CantLoadOrUnloadThisObject, request.CallSite,
                $"{request.Describe()}: the object is not a control array of a form."));
        }

        try
        {
            _ = outside.Automation.Invoke(handle, unload ? FormControlArrayMembers.Unload : FormControlArrayMembers.Load, AutomationInvocation.Method,
                [Dereferenced(index, resolver).BoxedValue], [false], session.Environment.Culture);
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }
        catch (AutomationException failure)
        {
            return RuntimeSemanticsEvaluationResult.Error(AutomationErrors.ToError(failure, request.CallSite, request.Describe()));
        }
    }

    // the instance of a form's class the argument is, if it is one.
    private VBRuntimeObjectId? FormArgument(IRuntimeValue argument, IRuntimeSymbolResolver resolver)
        => ObjectArgument(argument, resolver) is { } identity && session.Symbols.TryGetInstance(identity, out var live) && FormObject.IsFormClass(live.ClassModule)
            ? identity
            : null;

    // the object an argument is, if it is one: a Variant argument is the object it holds, and a Variant that holds no object (Missing, for an omitted owner form) is
    // none.
    private static VBRuntimeObjectId? ObjectArgument(IRuntimeValue argument, IRuntimeSymbolResolver resolver)
    {
        argument = Dereferenced(argument, resolver);
        if (argument is VBRuntimeVariantValue { WrappedValue: var held })
        {
            if (held is not VBObjectValue)
            {
                return null;
            }

            argument = held.RuntimeValue;
        }

        return VBObjectType.TypeInfo.CreateValue(new ValueBindingHandle(argument)) is VBObjectValue { } value && !value.IsNothing() ? value.Value : null;
    }

    // the value an argument is: the variable's, for one passed by reference.
    private static IRuntimeValue Dereferenced(IRuntimeValue argument, IRuntimeSymbolResolver resolver)
    {
        while (argument is VBRuntimeReference reference && resolver.TryRead(reference.Value, out var cell))
        {
            argument = cell.Value;
        }

        return argument;
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

        // from the first form on, its windows' messages wait while a program runs - what Microsoft Forms posts as it creates this one included.
        SessionForms.Of(session).HoldWhileRunning(session, outside.Forms);

        object created;
        using (session.Turn.Yield())
        {
            created = outside.Forms.Create(FormDefinition.Of(form, designer));
        }

        session.ExternalObjects.Bind(identity, outside.Automation, created);
        FormEvents.Listen(session, outside.Automation, outside.Forms, identity, created, designer);
        return created;
    }

    private bool IsVisualBasic6(string form) => forms.TryGetValue(form, out var designer) && designer.Kind == SDK.Workspace.Forms.FormDesignerKind.VisualBasic6;

    // the session's Forms collection, of the standard library's class that has no name in VB6 (IStdFormsClass).
    private RuntimeSemanticsEvaluationResult FormsCollection()
    {
        var collection = _forms.FormsCollection ??= session.Objects.CreateObject();
        if (!session.Symbols.TryGetInstance(collection, out _))
        {
            if (VBProjectSymbol.ResolveQualifiedType(session.Symbols.Resolver, StandardLibrary.LibraryName, "_Forms", StaticSymbol.GlobalUri).Symbol
                is not VBClassModuleSymbol collectionClass)
            {
                return RuntimeSemanticsEvaluationResult.InternalError();
            }

            _ = session.Symbols.CreateInstance(collection, collectionClass);
        }

        return RuntimeSemanticsEvaluationResult.Success(new VBObjectValue(collection));
    }

    private static RuntimeSemanticsEvaluationResult NoFormToHost(ExternalCallRequest request, string form)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, request.CallSite,
            $"{request.Describe()} could not be made: the designer of '{form}' declares no form to host."));

    // Load of a Visual Basic 6.0 form: its form is made and loaded, as by a member of it.
    private RuntimeSemanticsEvaluationResult Load(VBRuntimeObjectId identity, string form, ExternalCallRequest request)
    {
        try
        {
            return FormOf(identity, form, request) is null ? NoFormToHost(request, form) : Loading(identity, request);
        }
        catch (AutomationException failure)
        {
            return RuntimeSemanticsEvaluationResult.Error(AutomationErrors.ToError(failure, request.CallSite, request.Describe(), creating: true));
        }
    }

    // a Visual Basic 6.0 form that is not loaded is loaded by the use of its form: it is one of the loaded forms from now on, and its Form_Load runs inside the use.
    // A Load that unloads the form fails the use (364), and nothing loads it a second time to finish it (measured in VB6).
    private RuntimeSemanticsEvaluationResult Loading(VBRuntimeObjectId identity, ExternalCallRequest request)
    {
        if (_forms.IsLoaded(identity))
        {
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        if (Bindings is not { } bindings)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        _forms.LoadVisualBasic6(session, identity);
        var loaded = FormHandlers.Raise(session, bindings, identity, "Load");
        if (!loaded.IsSuccess)
        {
            return loaded;
        }

        return _forms.IsLoaded(identity)
            ? RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void)
            : RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectWasUnloaded, request.CallSite,
                $"{request.Describe()}: the form's Load unloaded it."));
    }

    // Show [Modal] of a Visual Basic 6.0 form: the style converts as CInt does, and only vbModeless (0) and vbModal (1) are styles (5 otherwise, the form loaded).
    // A modal form is waited for until it is hidden or unloaded. While one is, a modeless Show fails (401), and so does a modal Show of a form that is modal
    // already (405); a form shown modeless, or one beneath the modal form, turns modal (measured in VB6). The owner form, when its form is hosted, is given to the
    // host with the form it owns.
    private RuntimeSemanticsEvaluationResult ShowVisualBasic6(object hosted, VBRuntimeObjectId identity, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var modal = false;
        if (request.Arguments.Length > 1 && VariantOf(request.Arguments[1]) is { } argument and not VBMissingValue)
        {
            var style = ValueConversions.ToNumeric(argument, VBIntegerType.TypeInfo, session.Environment.Regional);
            if (!style.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(style.Error ?? VBRuntimeErrorId.TypeMismatch, request.CallSite,
                    $"{request.Describe()}: a value of type {argument.TypeInfo.Name} is no style."));
            }

            switch (ValueConversions.WholeValueOf(style.Value!))
            {
                case 0:
                    break;
                case 1:
                    modal = true;
                    break;
                default:
                    return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.InvalidProcedureCallOrArgument, request.CallSite,
                        $"{request.Describe()}: the style is vbModeless (0) or vbModal (1)."));
            }
        }

        var displayed = _forms.Modal;
        if (displayed.Count > 0 && !modal)
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.CantShowNonModalFormWhenModalFormIsDisplayed, request.CallSite,
                $"{request.Describe()}: a modal form is displayed."));
        }

        if (modal && displayed.Contains(identity))
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.UnableToShowModalFormWithinThisContext, request.CallSite,
                $"{request.Describe()}: the form is modal already."));
        }

        object? owner = null;
        if (request.Arguments.Length > 2 && FormArgument(request.Arguments[2], resolver) is { } ownerIdentity
            && session.ExternalObjects.TryGet(ownerIdentity, out var server, out var ownerForm) && ReferenceEquals(server, outside.Automation))
        {
            owner = ownerForm;
        }

        if (modal)
        {
            _forms.BeginModal(identity);
        }

        try
        {
            using (session.Turn.Yield())
            {
                outside.Forms.Show(hosted, modal, owner);
            }
        }
        finally
        {
            if (modal)
            {
                _forms.EndModal(identity);
            }
        }

        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    // Hide of a Visual Basic 6.0 form: while a modal form is displayed, only the topmost one can be hidden, which ends its Show (measured in VB6).
    private RuntimeSemanticsEvaluationResult HideVisualBasic6(object hosted, VBRuntimeObjectId identity, ExternalCallRequest request)
    {
        if (_forms.Modal is [.., var topmost] && !topmost.Equals(identity))
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.MustCloseOrHideTopmostModalFormFirst, request.CallSite,
                $"{request.Describe()}: another form is the topmost modal form."));
        }

        return Done(() => outside.Forms.Hide(hosted));
    }

    // Show [Modal]: modal when it says vbModal, and as the designer's ShowModal says when it says nothing: a UserForm's is True unless its designer says otherwise,
    // and a Visual Basic 6.0 form has none, and is shown modeless (vbModeless). A modal form is waited for, and the program gives its turn up while it does: what the
    // form raises meanwhile is the answer to the call.
    private RuntimeSemanticsEvaluationResult Show(object hosted, ExternalCallRequest request, string form)
    {
        var modal = request.Arguments.Length > 1 && VariantOf(request.Arguments[1]) is { } argument and not VBMissingValue
            ? Convert.ToInt32(argument.RuntimeValue.BoxedValue, System.Globalization.CultureInfo.InvariantCulture) != 0
            : !forms.TryGetValue(form, out var designer) || (designer.ShowModal ?? designer.Kind != SDK.Workspace.Forms.FormDesignerKind.VisualBasic6);

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

    // the form object's Left, Top, Width or Height, which are its window's, as a Single.
    private static float Side(FormBounds bounds, string member) => (float)(member.ToUpperInvariant() switch
    {
        "LEFT" => bounds.Left,
        "TOP" => bounds.Top,
        "WIDTH" => bounds.Width,
        _ => bounds.Height,
    });

    private static FormBounds WithSide(FormBounds bounds, string member, float value) => member.ToUpperInvariant() switch
    {
        "LEFT" => bounds with { Left = value },
        "TOP" => bounds with { Top = value },
        "WIDTH" => bounds with { Width = value },
        _ => bounds with { Height = value },
    };

    private static float SingleOf(IRuntimeValue argument)
        => VBSingleType.TypeInfo.CreateValue(new ValueBindingHandle(argument)) is VBSingleValue { Value: var value } ? value : 0;

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
