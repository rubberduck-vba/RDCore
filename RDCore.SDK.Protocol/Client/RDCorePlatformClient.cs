using RDCore.SDK.Platform.Protocol;

namespace RDCore.SDK.Client;

/// <summary>
/// The client's side of the RDCore platform protocol, over any <see cref="IRDCorePlatformChannel"/>: the handshake, the <c>rdcore/session/*</c> requests a client
/// sends, and the requests the language server sends a client, which it answers.
/// </summary>
/// <remarks>
/// <para>
/// A client connects to the language server, initializes it over LSP, then performs the platform handshake (<see cref="InitializeAsync"/>), which says what it
/// expects of the platform beyond LSP. The language server relays a <c>session/*</c> request to the component that serves it - the environment host, for the
/// runtime session - and answers it once that component did.
/// </para>
/// <para>
/// The requests the language server sends a client are answered only by a client that registered for them (<see cref="OnMessageBox"/>, <see cref="OnInputBox"/>,
/// <see cref="OnRubberduckIndenterSettings"/>, <see cref="OnRubberduckIndenterSettingsApply"/>), and should be registered before the handshake says the client expects
/// them: the language server may send one as soon as it is told.
/// </para>
/// </remarks>
/// <param name="channel">The connection to the language server.</param>
public sealed class RDCorePlatformClient(IRDCorePlatformChannel channel)
{
    /// <summary>The connection this client speaks through.</summary>
    public IRDCorePlatformChannel Channel { get; } = channel;

    /// <summary>
    /// Performs the platform handshake (<see cref="RDCorePlatformHandshake.Method"/>): says what the client expects of the component it connected to, and learns what
    /// that component provides.
    /// </summary>
    /// <param name="expectedComponent">The component the client expects to be connected to: <see cref="CoreServerComponent.LanguageServer"/>, for a client of the platform.</param>
    /// <param name="expected">The capabilities the client expects of it; a group left <see langword="null"/> is expected of no component.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The component the client is connected to, and the capabilities it provides.</returns>
    public Task<PlatformInitializeResult> InitializeAsync(CoreServerComponent expectedComponent, CorePlatformClientCapabilities expected, CancellationToken token)
        => Send<PlatformInitializeParams, PlatformInitializeResult>(RDCorePlatformHandshake.Method, new PlatformInitializeParams { ExpectedComponent = expectedComponent, Expected = expected }, token);

    /// <summary>Asks for the state of the runtime session (<see cref="RDCorePlatformProtocol.SessionStatus"/>).</summary>
    /// <param name="request">The request.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>Whether the session is composed, its project, and its memory.</returns>
    public Task<SessionStatusResult> StatusAsync(SessionStatusParams request, CancellationToken token)
        => Send<SessionStatusParams, SessionStatusResult>(RDCorePlatformProtocol.SessionStatus, request, token);

    /// <summary>Runs code in the runtime session (<see cref="RDCorePlatformProtocol.SessionExecute"/>).</summary>
    /// <param name="request">The code to run, and how.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>How the code ran: completed, suspended, or failed, with what it printed.</returns>
    public Task<ExecuteResult> ExecuteAsync(SessionExecuteParams request, CancellationToken token)
        => Send<SessionExecuteParams, ExecuteResult>(RDCorePlatformProtocol.SessionExecute, request, token);

    /// <summary>Discards a module of the runtime session (<see cref="RDCorePlatformProtocol.SessionDiscard"/>).</summary>
    /// <param name="request">The module.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>How much was discarded.</returns>
    public Task<DiscardResult> DiscardAsync(SessionDiscardParams request, CancellationToken token)
        => Send<SessionDiscardParams, DiscardResult>(RDCorePlatformProtocol.SessionDiscard, request, token);

    /// <summary>Reads a byte of the runtime session's memory (<see cref="RDCorePlatformProtocol.SessionPeek"/>).</summary>
    /// <param name="request">The address.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The byte, and whether the address is allocated.</returns>
    public Task<PeekResult> PeekAsync(SessionPeekParams request, CancellationToken token)
        => Send<SessionPeekParams, PeekResult>(RDCorePlatformProtocol.SessionPeek, request, token);

    /// <summary>Writes a byte of the runtime session's memory (<see cref="RDCorePlatformProtocol.SessionPoke"/>).</summary>
    /// <param name="request">The address and the value.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>Whether the byte was written.</returns>
    public Task<PokeResult> PokeAsync(SessionPokeParams request, CancellationToken token)
        => Send<SessionPokeParams, PokeResult>(RDCorePlatformProtocol.SessionPoke, request, token);

    /// <summary>Resumes, or steps, the suspended program (<see cref="RDCorePlatformProtocol.SessionDebugResume"/>).</summary>
    /// <param name="request">How far to run.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>How the program ran: suspended again, ended, or failed.</returns>
    public Task<ExecuteResult> DebugResumeAsync(SessionDebugResumeParams request, CancellationToken token)
        => Send<SessionDebugResumeParams, ExecuteResult>(RDCorePlatformProtocol.SessionDebugResume, request, token);

    /// <summary>Suspends the running program (<see cref="RDCorePlatformProtocol.SessionDebugPause"/>).</summary>
    /// <param name="request">The request.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>Whether there was a program to suspend.</returns>
    public Task<DebugAck> DebugPauseAsync(SessionDebugPauseParams request, CancellationToken token)
        => Send<SessionDebugPauseParams, DebugAck>(RDCorePlatformProtocol.SessionDebugPause, request, token);

    /// <summary>Moves the next statement of the suspended program (<see cref="RDCorePlatformProtocol.SessionDebugGoto"/>).</summary>
    /// <param name="request">Where to.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>Whether the next statement moved, where to, or why it did not.</returns>
    public Task<DebugGotoResult> DebugGotoAsync(SessionDebugGotoParams request, CancellationToken token)
        => Send<SessionDebugGotoParams, DebugGotoResult>(RDCorePlatformProtocol.SessionDebugGoto, request, token);

    /// <summary>Asks what the edits made while the program is suspended do to it (<see cref="RDCorePlatformProtocol.SessionDebugEditStatus"/>).</summary>
    /// <param name="request">The request.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>Each edit, and what it takes to apply it.</returns>
    public Task<DebugEditStatusResult> DebugEditStatusAsync(SessionDebugEditStatusParams request, CancellationToken token)
        => Send<SessionDebugEditStatusParams, DebugEditStatusResult>(RDCorePlatformProtocol.SessionDebugEditStatus, request, token);

    /// <summary>Ends the suspended program (<see cref="RDCorePlatformProtocol.SessionDebugTerminate"/>).</summary>
    /// <param name="request">Whether to wipe the session's state as well.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>Whether there was a program to end.</returns>
    public Task<DebugAck> DebugTerminateAsync(SessionDebugTerminateParams request, CancellationToken token)
        => Send<SessionDebugTerminateParams, DebugAck>(RDCorePlatformProtocol.SessionDebugTerminate, request, token);

    /// <summary>Sets the breakpoints of a module (<see cref="RDCorePlatformProtocol.SessionDebugBreakpoints"/>).</summary>
    /// <param name="request">The module and its breakpoints: all of them, which replace the ones it had.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>Each breakpoint as it was set: verified on a line that runs, or not.</returns>
    public Task<DebugBreakpointsResult> DebugBreakpointsAsync(SessionDebugBreakpointsParams request, CancellationToken token)
        => Send<SessionDebugBreakpointsParams, DebugBreakpointsResult>(RDCorePlatformProtocol.SessionDebugBreakpoints, request, token);

    /// <summary>Asks for the call stack of the suspended program (<see cref="RDCorePlatformProtocol.SessionDebugStack"/>).</summary>
    /// <param name="request">The request.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The frames, innermost first.</returns>
    public Task<DebugStackResult> DebugStackAsync(SessionDebugStackParams request, CancellationToken token)
        => Send<SessionDebugStackParams, DebugStackResult>(RDCorePlatformProtocol.SessionDebugStack, request, token);

    /// <summary>Asks for the variables of a frame of the suspended program, or of an object (<see cref="RDCorePlatformProtocol.SessionDebugVariables"/>).</summary>
    /// <param name="request">The frame, the scope, and the object.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The variables, with their values and types.</returns>
    public Task<DebugVariablesResult> DebugVariablesAsync(SessionDebugVariablesParams request, CancellationToken token)
        => Send<SessionDebugVariablesParams, DebugVariablesResult>(RDCorePlatformProtocol.SessionDebugVariables, request, token);

    /// <summary>Evaluates an expression in a frame of the suspended program (<see cref="RDCorePlatformProtocol.SessionDebugEvaluate"/>).</summary>
    /// <param name="request">The frame and the expression.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The value, or the error the expression raised.</returns>
    public Task<DebugEvaluateResult> DebugEvaluateAsync(SessionDebugEvaluateParams request, CancellationToken token)
        => Send<SessionDebugEvaluateParams, DebugEvaluateResult>(RDCorePlatformProtocol.SessionDebugEvaluate, request, token);

    /// <summary>Runs a statement in a frame of the suspended program, as the Immediate window does (<see cref="RDCorePlatformProtocol.SessionDebugExecute"/>).</summary>
    /// <param name="request">The frame and the statement.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>Whether the statement ran, what it printed, or the error it raised.</returns>
    public Task<DebugEvaluateResult> DebugExecuteAsync(SessionDebugExecuteParams request, CancellationToken token)
        => Send<SessionDebugExecuteParams, DebugEvaluateResult>(RDCorePlatformProtocol.SessionDebugExecute, request, token);

    /// <summary>
    /// Shows the program's <c>MsgBox</c> calls (<see cref="RDCorePlatformProtocol.SessionMessageBox"/>), for a client that expects <see cref="SessionInteraction"/>.
    /// </summary>
    /// <param name="handler">Shows a message box and answers the button the user chose; <see cref="MessageBoxResult.NotHandled"/> when it showed none.</param>
    /// <returns>The registration; disposing it stops answering.</returns>
    public IDisposable OnMessageBox(Func<SessionMessageBoxParams, CancellationToken, Task<MessageBoxResult>> handler)
        => Channel.OnRequest(RDCorePlatformProtocol.SessionMessageBox, handler);

    /// <summary>
    /// Shows the program's <c>InputBox</c> calls (<see cref="RDCorePlatformProtocol.SessionInputBox"/>), for a client that expects <see cref="SessionInteraction"/>.
    /// </summary>
    /// <param name="handler">Shows an input box and answers what the user typed, or that they cancelled; <see cref="InputBoxResult.NotHandled"/> when it showed none.</param>
    /// <returns>The registration; disposing it stops answering.</returns>
    public IDisposable OnInputBox(Func<SessionInputBoxParams, CancellationToken, Task<InputBoxResult>> handler)
        => Channel.OnRequest(RDCorePlatformProtocol.SessionInputBox, handler);

    /// <summary>
    /// Answers what the client finds of the user's Rubberduck indenter settings (<see cref="RDCorePlatformProtocol.RubberduckIndenterSettings"/>), for a client that
    /// expects <see cref="RubberduckIndenterSettings"/>.
    /// </summary>
    /// <param name="handler">Reads the settings, as they are; <see cref="RubberduckIndenterSettingsResult.Nothing"/> when there are none.</param>
    /// <returns>The registration; disposing it stops answering.</returns>
    public IDisposable OnRubberduckIndenterSettings(Func<RubberduckIndenterSettingsParams, CancellationToken, Task<RubberduckIndenterSettingsResult>> handler)
        => Channel.OnRequest(RDCorePlatformProtocol.RubberduckIndenterSettings, handler);

    /// <summary>
    /// Takes the <c>Formatting</c> settings the language server mapped from them as the client's own (<see cref="RDCorePlatformProtocol.RubberduckIndenterSettingsApply"/>),
    /// for a client that expects <see cref="RubberduckIndenterSettings"/>.
    /// </summary>
    /// <param name="handler">Sets the settings, and says how many it set and which it did not.</param>
    /// <returns>The registration; disposing it stops answering.</returns>
    public IDisposable OnRubberduckIndenterSettingsApply(Func<RubberduckIndenterSettingsApplyParams, CancellationToken, Task<RubberduckIndenterSettingsApplyResult>> handler)
        => Channel.OnRequest(RDCorePlatformProtocol.RubberduckIndenterSettingsApply, handler);

    private Task<TResult> Send<TParams, TResult>(string method, TParams request, CancellationToken token)
        => Channel.SendRequestAsync<TParams, TResult>(method, request, token);
}
