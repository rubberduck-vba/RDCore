using RDCore.External.Automation;

namespace RDCore.External.Protocol;

/// <summary>
/// The methods of the external host's protocol: what an environment host asks of the process that makes its program's calls to the outside world, and what that
/// process tells it.
/// </summary>
/// <remarks>
/// Every call a program makes to an automation server is one request, answered once the call returns; the program waits for the answer, as it waits for any call.
/// An event a server raises is a request the other way, answered once the handlers have run.
/// <para>
/// The requests go over a channel of their own (<see cref="SDK.Platform.Channels.CallChannel"/>, on the pipe <see cref="CallsPipeName"/> names), not over the
/// connection that started the external host: a program makes them by the thousand, and each waits for the last.
/// </para>
/// <para>
/// A handler of an event is a program too, and the calls it makes are answered by the thread that is waiting for the event to be handled - the one the server raised it
/// on, which is the thread such a call has to be made from (<see cref="AutomationRequest.HandlingEvent"/>).
/// </para>
/// </remarks>
public static class ExternalProtocol
{
    /// <summary><c>rdcore/external/automation/status</c>: whether the external host has automation servers to reach at all.</summary>
    public const string AutomationStatus = "rdcore/external/automation/status";

    /// <summary><c>rdcore/external/automation/create</c>: creates an object by the name its class is registered under.</summary>
    public const string AutomationCreate = "rdcore/external/automation/create";

    /// <summary><c>rdcore/external/automation/obtain</c>: gets an object by name, as <c>CreateObject</c> and <c>GetObject</c> do.</summary>
    public const string AutomationObtain = "rdcore/external/automation/obtain";

    /// <summary><c>rdcore/external/automation/invoke</c>: calls a member of an object by its name.</summary>
    public const string AutomationInvoke = "rdcore/external/automation/invoke";

    /// <summary><c>rdcore/external/automation/className</c>: the name a server gives the class of an object.</summary>
    public const string AutomationClassName = "rdcore/external/automation/className";

    /// <summary><c>rdcore/external/automation/moveNext</c>: moves an enumerator to its next member.</summary>
    public const string AutomationMoveNext = "rdcore/external/automation/moveNext";

    /// <summary><c>rdcore/external/automation/reset</c>: returns an enumerator to before its first member.</summary>
    public const string AutomationReset = "rdcore/external/automation/reset";

    /// <summary><c>rdcore/external/automation/advise</c>: starts listening to the events of an object.</summary>
    public const string AutomationAdvise = "rdcore/external/automation/advise";

    /// <summary><c>rdcore/external/automation/unadvise</c>: stops listening to the events of an object.</summary>
    public const string AutomationUnadvise = "rdcore/external/automation/unadvise";

    /// <summary><c>rdcore/external/automation/release</c>: lets go of an object.</summary>
    public const string AutomationRelease = "rdcore/external/automation/release";

    /// <summary><c>rdcore/external/automation/event</c>: an object raised an event, which the environment host handles.</summary>
    public const string AutomationEvent = "rdcore/external/automation/event";

    /// <summary><c>rdcore/external/forms/create</c>: creates a form from its definition, an object of the automation server like any other.</summary>
    public const string FormCreate = "rdcore/external/forms/create";

    /// <summary><c>rdcore/external/forms/show</c>: shows a form, modeless or modal; a modal one is answered once it is hidden.</summary>
    public const string FormShow = "rdcore/external/forms/show";

    /// <summary><c>rdcore/external/forms/hide</c>: hides a form.</summary>
    public const string FormHide = "rdcore/external/forms/hide";

    /// <summary><c>rdcore/external/forms/isShown</c>: whether a form is shown.</summary>
    public const string FormIsShown = "rdcore/external/forms/isShown";

    /// <summary><c>rdcore/external/forms/caption</c>: gets a form's caption, or sets it when the request gives one.</summary>
    public const string FormCaption = "rdcore/external/forms/caption";

    /// <summary><c>rdcore/external/forms/leave</c>: the focus leaves a form for good - its active control is exited - as it does when the form is unloaded.</summary>
    public const string FormLeave = "rdcore/external/forms/leave";

    /// <summary><c>rdcore/external/native/call</c>: calls a function of a native library, as a <c>Declare</c> names it.</summary>
    public const string NativeCall = "rdcore/external/native/call";

    /// <summary>
    /// The name of the pipe the calls go over: the one the external host was started with, which carries its connection to the environment host, and a suffix.
    /// </summary>
    /// <param name="pipeName">The name of the pipe the external host was started with.</param>
    public static string CallsPipeName(string pipeName) => pipeName + ".Calls";
}

/// <summary>
/// What a server reported when a request failed: its <c>HRESULT</c>, and what it had to say.
/// </summary>
public sealed record class ExternalFailure
{
    /// <summary>The status code the server reported.</summary>
    public int HResult { get; init; }

    /// <summary>What the server said, if it said anything.</summary>
    public string? Message { get; init; }

    /// <summary>The name of the server that said it, if it named itself.</summary>
    public string? Source { get; init; }

    /// <summary>
    /// The failure a server reported, as it crosses.
    /// </summary>
    public static ExternalFailure Of(AutomationException failure) => new() { HResult = failure.HResult, Message = failure.Message, Source = failure.Source };

    /// <summary>
    /// The failure, as the server reported it.
    /// </summary>
    public AutomationException ToException() => new(HResult, Message, Source);
}

/// <summary>
/// The answer to a request of the external host.
/// </summary>
public abstract record class ExternalResult
{
    /// <summary>
    /// Why the request failed; <see langword="null"/> when it did not.
    /// </summary>
    public ExternalFailure? Failure { get; init; }
}

/// <summary>
/// The answer to a request that answers nothing but that it was done.
/// </summary>
public sealed record class ExternalDoneResult : ExternalResult;

/// <summary>
/// A request about the objects of the automation servers.
/// </summary>
public abstract record class AutomationRequest
{
    /// <summary>
    /// The event whose handlers make the request, as the external host numbered it when it raised it; <c>0</c> when no handler makes it.
    /// </summary>
    /// <remarks>
    /// A handler runs while the server that raised the event waits for it, on the thread it raised the event on, and a call the handler makes is answered on that
    /// thread: in a single-threaded apartment, that is the thread the call has to be made from, which is busy until the handler is done.
    /// </remarks>
    public long HandlingEvent { get; init; }
}

/// <summary>Request for <see cref="ExternalProtocol.AutomationStatus"/>.</summary>
public sealed record class AutomationStatusParams : AutomationRequest;

/// <summary>Response for <see cref="ExternalProtocol.AutomationStatus"/>.</summary>
public sealed record class AutomationStatusResult : ExternalResult
{
    /// <summary>Whether the external host has automation servers to reach: <see cref="IAutomationServer.IsAvailable"/>.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>Whether the external host can host forms: <see cref="Forms.IFormHost.IsAvailable"/>.</summary>
    public bool IsFormHostAvailable { get; init; }
}

/// <summary>Request for <see cref="ExternalProtocol.AutomationCreate"/>.</summary>
public sealed record class AutomationCreateParams : AutomationRequest
{
    /// <summary>The programmatic identifier of the class.</summary>
    public string ProgId { get; init; } = string.Empty;
}

/// <summary>Request for <see cref="ExternalProtocol.AutomationObtain"/>: <see cref="IAutomationServer.GetObject"/>.</summary>
public sealed record class AutomationObtainParams : AutomationRequest
{
    /// <summary>Where the object comes from.</summary>
    public SDK.Runtime.Abstract.Execution.ExternalObjectSource Source { get; init; }

    /// <summary>The programmatic identifier of its class, if one is given.</summary>
    public string? ProgId { get; init; }

    /// <summary>The file, or the moniker, of an object from a file.</summary>
    public string? PathName { get; init; }

    /// <summary>The machine a new object is created on.</summary>
    public string? ServerName { get; init; }
}

/// <summary>Response for <see cref="ExternalProtocol.AutomationCreate"/> and <see cref="ExternalProtocol.AutomationObtain"/>.</summary>
public sealed record class AutomationObjectResult : ExternalResult
{
    /// <summary>The handle of the new object.</summary>
    public long Handle { get; init; }
}

/// <summary>Request for <see cref="ExternalProtocol.AutomationInvoke"/>.</summary>
public sealed record class AutomationInvokeParams : AutomationRequest
{
    /// <summary>The handle of the object.</summary>
    public long Target { get; init; }

    /// <summary>The name of the member.</summary>
    public string Member { get; init; } = string.Empty;

    /// <summary>How the member is reached.</summary>
    public AutomationInvocation Invocation { get; init; }

    /// <summary>The arguments in the member's order, a value being assigned last.</summary>
    public ExternalValue[] Arguments { get; init; } = [];

    /// <summary>Whether each argument is passed by reference.</summary>
    public bool[] ByReference { get; init; } = [];

    /// <summary>The name of the locale the call is made in.</summary>
    public string Culture { get; init; } = string.Empty;
}

/// <summary>Response for <see cref="ExternalProtocol.AutomationInvoke"/>.</summary>
public sealed record class AutomationInvokeResult : ExternalResult
{
    /// <summary>What the member returned.</summary>
    public ExternalValue Returned { get; init; } = new();

    /// <summary>
    /// The arguments as the call left them: what the server wrote to an argument passed by reference; any other is not said, and is an <c>Empty</c>.
    /// </summary>
    public ExternalValue[] Arguments { get; init; } = [];
}

/// <summary>Request for <see cref="ExternalProtocol.AutomationClassName"/>.</summary>
public sealed record class AutomationClassNameParams : AutomationRequest
{
    /// <summary>The handle of the object.</summary>
    public long Target { get; init; }
}

/// <summary>Response for <see cref="ExternalProtocol.AutomationClassName"/>.</summary>
public sealed record class AutomationClassNameResult : ExternalResult
{
    /// <summary>The name a server gives the class of the object, qualified by its library; <see langword="null"/> when it gives none.</summary>
    public string? Name { get; init; }
}

/// <summary>Request for <see cref="ExternalProtocol.AutomationMoveNext"/>.</summary>
public sealed record class AutomationMoveNextParams : AutomationRequest
{
    /// <summary>The handle of the enumerator.</summary>
    public long Enumerator { get; init; }
}

/// <summary>Response for <see cref="ExternalProtocol.AutomationMoveNext"/>.</summary>
public sealed record class AutomationMoveNextResult : ExternalResult
{
    /// <summary><see langword="false"/> when the members have run out.</summary>
    public bool Moved { get; init; }

    /// <summary>The member the enumerator moved to.</summary>
    public ExternalValue Current { get; init; } = new();
}

/// <summary>Request for <see cref="ExternalProtocol.AutomationReset"/>.</summary>
public sealed record class AutomationResetParams : AutomationRequest
{
    /// <summary>The handle of the enumerator.</summary>
    public long Enumerator { get; init; }
}

/// <summary>Request for <see cref="ExternalProtocol.AutomationAdvise"/>.</summary>
public sealed record class AutomationAdviseParams : AutomationRequest
{
    /// <summary>The handle of the object that raises the events.</summary>
    public long Source { get; init; }
}

/// <summary>Request for <see cref="ExternalProtocol.AutomationUnadvise"/>.</summary>
public sealed record class AutomationUnadviseParams : AutomationRequest
{
    /// <summary>The handle of the object that raises the events.</summary>
    public long Source { get; init; }
}

/// <summary>Request for <see cref="ExternalProtocol.AutomationRelease"/>.</summary>
public sealed record class AutomationReleaseParams : AutomationRequest
{
    /// <summary>The handle of the object.</summary>
    public long Handle { get; init; }
}

/// <summary>Request for <see cref="ExternalProtocol.AutomationEvent"/>, from the external host to the environment host.</summary>
public sealed record class AutomationEventParams
{
    /// <summary>
    /// The number the external host gave the event: what a call that a handler of it makes says it is made for (<see cref="AutomationRequest.HandlingEvent"/>).
    /// </summary>
    public long Event { get; init; }

    /// <summary>The handle of the object that raised it.</summary>
    public long Source { get; init; }

    /// <summary>The name of the event.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What the server raised it with.</summary>
    public ExternalValue[] Arguments { get; init; } = [];

    /// <summary>Whether a call that the program waits for is what raised it: <see cref="Automation.AutomationEvent.IsSynchronous"/>.</summary>
    public bool IsSynchronous { get; init; }

    /// <summary>The dispatch identifier it was raised with: <see cref="Automation.AutomationEvent.DispatchId"/>.</summary>
    public int? DispatchId { get; init; }
}

/// <summary>Response for <see cref="ExternalProtocol.AutomationEvent"/>.</summary>
public sealed record class AutomationEventResult : ExternalResult
{
    /// <summary>The arguments as the handlers left them, which is what the server finds in the ones it passed by reference.</summary>
    public ExternalValue[] Arguments { get; init; } = [];
}
