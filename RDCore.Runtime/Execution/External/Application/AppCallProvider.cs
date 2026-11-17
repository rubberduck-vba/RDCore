using RDCore.Runtime.Execution.External.Automation;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.External.Application;

/// <summary>
/// Runs Visual Basic 6.0's <c>App</c> (of <c>VB.Global</c>) and the members of the one object it is, which the platform holds itself: what it says comes from the
/// session's <see cref="SessionApplication"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Path</c> and <c>EXEName</c> are the workspace project's folder and name under <c>rdc run</c>, and the executable's when <c>rdc build</c> built the program.
/// <c>PrevInstance</c> is whether the same executable was already running, and <see langword="False"/> under <c>rdc run</c>.
/// </para>
/// <para>
/// <c>Title</c> is the project's name and the version is 1.0.0, with the six version strings empty, as a new Visual Basic 6.0 project has them. <c>StartMode</c>
/// is <c>vbSModeStandalone</c>, <c>NonModalAllowed</c> is <see langword="True"/>, <c>RetainedProject</c> is <see langword="False"/>, and <c>UnattendedApp</c>
/// is whether the program runs unattended (<see cref="IRuntimeSession.RunsUnattended"/>).
/// </para>
/// <para>
/// <c>TaskVisible</c> and the <c>Ole*</c> properties keep what the program assigns, and change nothing. <c>ThreadID</c> is the operating system's identifier of
/// the thread the program runs on. <c>hInstance</c> raises error 5 on an operating system other than Windows, and 6 in a 64-bit process, whose handle does not
/// fit a <c>Long</c>.
/// </para>
/// </remarks>
/// <param name="session">The session whose program asks.</param>
public sealed class AppCallProvider(IRuntimeSession session) : IExternalCallProvider, IExternalObjectOwner
{
    private const string Library = "VB";
    private const string ClassName = "App";
    private const string GlobalKey = "VB.Global.App/get";
    private const string MemberPrefix = "VB.App.";

    // VBRUN's StartModeConstants: a program that was started on its own.
    private const short StandaloneMode = 0;

    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request)
        => !request.IsCreation
            && request.Member.GetProperty(SymbolProperties.ExternalTarget) is { } key
            && (key == GlobalKey || key.StartsWith(MemberPrefix, StringComparison.Ordinal) && IsApp(request));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var key = request.Member.GetProperty(SymbolProperties.ExternalTarget)!;
        return key == GlobalKey ? Object(resolver) : Member(request, resolver, key);
    }

    /// <inheritdoc/>
    public void Release(object handle)
    {
        // what App says is the session's, which outlives the object.
        if (handle is SessionApplication application)
        {
            application.Object = null;
        }
    }

    // the session's App object: the one that is held, or a new one when nothing holds it.
    private RuntimeSemanticsEvaluationResult Object(IRuntimeSymbolResolver resolver)
    {
        var application = SessionApplication.Of(session);
        if (application.Object is { } held && session.ExternalObjects.TryGet(held, out _, out var handle) && ReferenceEquals(handle, application))
        {
            return Success(new VBObjectValue(held));
        }

        if (ServerObjects.Current(resolver, Library, ClassName) is not { } classModule)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var identity = session.Objects.CreateObject();
        session.Symbols.CreateInstance(identity, classModule);
        session.ExternalObjects.Bind(identity, this, application);
        application.Object = identity;
        return Success(new VBObjectValue(identity));
    }

    private RuntimeSemanticsEvaluationResult Member(ExternalCallRequest request, IRuntimeSymbolResolver resolver, string key)
    {
        var application = SessionApplication.Of(session);
        var slash = key.LastIndexOf('/');
        var name = key[MemberPrefix.Length..slash];
        var kind = key[(slash + 1)..];

        return (name.ToUpperInvariant(), kind) switch
        {
            ("PATH", "get") => Success(new VBStringValue(application.Folder)),
            ("EXENAME", "get") => Success(new VBStringValue(application.ExecutableName)),
            ("TITLE", "get") => Success(new VBStringValue(application.Title)),
            ("TITLE", "let") => Assigned(request, resolver, value => application.Title = Text(value)),
            ("PREVINSTANCE", "get") => Success(Boolean(application.PrevInstance)),
            ("STARTMODE", "get") => Success(new VBIntegerValue(StandaloneMode)),
            ("TASKVISIBLE", "get") => Success(Boolean(application.TaskVisible)),
            ("TASKVISIBLE", "let") => Assigned(request, resolver, value => application.TaskVisible = value is VBBooleanValue { Value.StoredValue: not 0 }),
            ("OLESERVERBUSYTIMEOUT", "get") => Success(new VBLongValue(application.OleServerBusyTimeout)),
            ("OLESERVERBUSYTIMEOUT", "let") => Assigned(request, resolver, value => application.OleServerBusyTimeout = Whole(value)),
            ("OLEREQUESTPENDINGTIMEOUT", "get") => Success(new VBLongValue(application.OleRequestPendingTimeout)),
            ("OLEREQUESTPENDINGTIMEOUT", "let") => Assigned(request, resolver, value => application.OleRequestPendingTimeout = Whole(value)),
            ("OLESERVERBUSYRAISEERROR", "get") => Success(Boolean(application.OleServerBusyRaiseError)),
            ("OLESERVERBUSYRAISEERROR", "let") => Assigned(request, resolver, value => application.OleServerBusyRaiseError = value is VBBooleanValue { Value.StoredValue: not 0 }),
            ("OLESERVERBUSYMSGTITLE", "get") => Success(new VBStringValue(application.OleServerBusyMessageTitle ?? application.Title)),
            ("OLESERVERBUSYMSGTITLE", "let") => Assigned(request, resolver, value => application.OleServerBusyMessageTitle = Text(value)),
            ("OLESERVERBUSYMSGTEXT", "get") => Success(new VBStringValue(application.OleServerBusyMessageText)),
            ("OLESERVERBUSYMSGTEXT", "let") => Assigned(request, resolver, value => application.OleServerBusyMessageText = Text(value)),
            ("OLEREQUESTPENDINGMSGTITLE", "get") => Success(new VBStringValue(application.OleRequestPendingMessageTitle ?? application.Title)),
            ("OLEREQUESTPENDINGMSGTITLE", "let") => Assigned(request, resolver, value => application.OleRequestPendingMessageTitle = Text(value)),
            ("OLEREQUESTPENDINGMSGTEXT", "get") => Success(new VBStringValue(application.OleRequestPendingMessageText)),
            ("OLEREQUESTPENDINGMSGTEXT", "let") => Assigned(request, resolver, value => application.OleRequestPendingMessageText = Text(value)),
            // a new project's version, and its version strings, until the project says otherwise.
            ("MAJOR", "get") => Success(new VBIntegerValue(1)),
            ("MINOR" or "REVISION", "get") => Success(new VBIntegerValue(0)),
            ("COMMENTS" or "COMPANYNAME" or "FILEDESCRIPTION" or "LEGALCOPYRIGHT" or "LEGALTRADEMARKS" or "PRODUCTNAME", "get") => Success(new VBStringValue(string.Empty)),
            ("HINSTANCE", "get") => InstanceHandle(request),
            ("NONMODALALLOWED", "get") => Success(VBBooleanValue.True),
            ("UNATTENDEDAPP", "get") => Success(Boolean(session.RunsUnattended)),
            ("THREADID", "get") => OperatingSystemThread.CurrentId() switch
            {
                { } id when id is >= int.MinValue and <= int.MaxValue => Success(new VBLongValue((int)id)),
                { } => Error(VBRuntimeErrorId.Overflow, request, "the thread's identifier does not fit a Long"),
                null => Error(VBRuntimeErrorId.InvalidProcedureCallOrArgument, request, "this operating system gives no thread identifier"),
            },
            ("RETAINEDPROJECT", "get") => Success(VBBooleanValue.False),
            _ => Error(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, request, $"App.{name} is not implemented"),
        };
    }

    // the handle of the program's module, which is Windows': it does not fit a Long in a 64-bit process, and is never given cut short.
    private static RuntimeSemanticsEvaluationResult InstanceHandle(ExternalCallRequest request)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Error(VBRuntimeErrorId.InvalidProcedureCallOrArgument, request, "a module handle is Windows'");
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var handle = Environment.Is64BitProcess ? null : process.MainModule?.BaseAddress.ToInt64();
        return handle is { } value and >= int.MinValue and <= int.MaxValue
            ? Success(new VBLongValue((int)value))
            : Error(VBRuntimeErrorId.Overflow, request, "the module handle of a 64-bit process does not fit a Long");
    }

    // a Property Let: the value it is given, as its parameter declares it.
    private static RuntimeSemanticsEvaluationResult Assigned(ExternalCallRequest request, IRuntimeSymbolResolver resolver, Action<VBTypedValue?> assign)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(request.Member);
        if (request.Arguments is not [_, .., var value] || parameters.Length == 0)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        assign(Read(value, parameters[^1].ResolvedType, resolver));
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    private bool IsApp(ExternalCallRequest request)
        => request.Arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var receiver }, ..]
            && session.ExternalObjects.TryGet(receiver, out var owner, out var handle) && owner is AppCallProvider && handle is SessionApplication;

    // an argument as the type its parameter declares; one passed by reference is the variable it names.
    private static VBTypedValue? Read(IRuntimeValue argument, VBType declared, IRuntimeSymbolResolver resolver)
    {
        while (argument is VBRuntimeReference reference && resolver.TryRead(reference.Value, out var cell))
        {
            argument = cell.Value;
        }

        return declared.CreateValue(new ValueBindingHandle(argument));
    }

    private static string Text(VBTypedValue? value) => value is VBStringValue { Value: var text } ? text ?? string.Empty : string.Empty;

    private static int Whole(VBTypedValue? value) => value is VBLongValue { Value: var whole } ? whole : 0;

    private static VBBooleanValue Boolean(bool value) => value ? VBBooleanValue.True : VBBooleanValue.False;

    private static RuntimeSemanticsEvaluationResult Success(VBTypedValue value) => RuntimeSemanticsEvaluationResult.Success(value);

    private static RuntimeSemanticsEvaluationResult Error(VBRuntimeErrorId error, ExternalCallRequest request, string detail)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(error, request.CallSite, $"{request.Describe()}: {detail}"));
}
