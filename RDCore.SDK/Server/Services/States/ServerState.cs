using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Server.Services.States;

/// <summary>
/// The base <see cref="ServerState"/> abstraction.
/// </summary>
/// <param name="Value">The <see cref="ServerStateValue"/> value corresponding to this state.</param>
public abstract record class ServerState(ServerStateValue Value)
{
    /// <summary>
    /// Gets the <see cref="ServerStateValue.Starting"/> server state.
    /// </summary>
    public static ServerState Starting { get; } = new StartingServerState();
    /// <summary>
    /// Gets the <see cref="ServerStateValue.Initializing"/> server state.
    /// </summary>
    public static ServerState Initializing { get; } = new InitializingServerState();
    /// <summary>
    /// Gets the <see cref="ServerStateValue.Running"/> server state.
    /// </summary>
    public static ServerState Running { get; } = new RunningServerState();
    /// <summary>
    /// Gets the <see cref="ServerStateValue.RunningVerbose"/> server state.
    /// </summary>
    public static ServerState RunningVerbose { get; } = new RunningVerboseServerState();
    /// <summary>
    /// Gets the <see cref="ServerStateValue.RunningTraceless"/> server state.
    /// </summary>
    public static ServerState RunningTraceless { get; } = new RunningTracelessServerState();
    /// <summary>
    /// Gets the <see cref="ServerStateValue.ShuttingDown"/> server state.
    /// </summary>
    public static ServerState ShuttingDown { get; } = new ShuttingDownServerState();
    /// <summary>
    /// Gets the <see cref="ServerStateValue.Exiting"/> server state for the specified current <c>state</c>.
    /// </summary>
    /// <param name="state">The <see cref="ServerStateValue"/> for the current server state.</param>
    public static ServerState Exiting(ServerStateValue state) => new ExitingServerState(state);

    public ServerStateValue Value { get; } = Value;

    /// <summary>
    /// The application exit code corresponding to the current <see cref="ServerState"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Returns an <strong>error code</strong> unless overridden in a more specialized state.
    /// </remarks>
    public virtual int ExitCode => 1;
}

/// <summary>
/// The LSP Server application is starting. This is the initial state; server will only accept an <c>Initialize</c> request.
/// </summary>
/// <remarks>
/// <a href="https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#initialize">Server lifecycle § Initialize Request</a>
/// </remarks>
public record class StartingServerState() : ServerState(ServerStateValue.Starting)
{
    /// <summary>
    /// Early exit prior to initialization is not an error.
    /// </summary>
    public override int ExitCode => 0;
}
/// <summary>
/// The LSP Server application has received an <c>Initialize</c> request and is now <em>initializing</em>.
/// </summary>
public record class InitializingServerState() : ServerState(ServerStateValue.Initializing);
/// <summary>
/// The LSP Server application has completed initialization and is now running with trace enabled.
/// </summary>
/// <param name="Trace">The initial server <c>Trace</c> level <strong>as determined by the client</strong> through the LSP <em>initialization handshake</em>.</param>
/// <remarks>
/// 👉 LSP Server application must use <see cref="InitializeTrace.Off"/> if the initialization parameters omit the <c>Trace</c> value.
/// <br/><a href="https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#initialize">Server lifecycle § Initialize Request</a>
/// </remarks>
public record class RunningServerState(InitializeTrace Trace = InitializeTrace.Messages) 
    : ServerState(Trace == InitializeTrace.Off ? ServerStateValue.RunningTraceless : Trace == InitializeTrace.Verbose ? ServerStateValue.RunningVerbose : ServerStateValue.Running)
{
    /// <summary>
    /// The current server <c>Trace</c> level.
    /// </summary>
    /// <remarks>
    /// 👉 The client can change this at any time through a <c>SetTrace</c> notification
    /// <br/><a href="https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#setTrace">Server lifecycle § SetTrace Notification</a>
    /// </remarks>
    public InitializeTrace Trace { get; } = Trace;
}
/// <summary>
/// The LSP Server application is in a <see cref="RunningServerState"/> with <c>Trace</c> set to <see cref="InitializeTrace.Verbose"/>.
/// </summary>
public record class RunningVerboseServerState() : RunningServerState(InitializeTrace.Verbose);
/// <summary>
/// The LSP Server application is in a <see cref="RunningServerState"/> with <c>Trace</c> set to <see cref="InitializeTrace.Off"/>.
/// </summary>
public record class RunningTracelessServerState() : RunningServerState(InitializeTrace.Off);
/// <summary>
/// The LSP Server application has received a <c>Shutdown</c> request and is in a <see cref="ServerStateValue.ShuttingDown"/> state.
/// </summary>
/// <remarks>
/// 👉 LSP Server application application will only accept an <c>Exit</c> notification
/// <br/><a href="https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#shutdown">Server lifecycle § Shutdown Request</a>
/// </remarks>
public record class ShuttingDownServerState() : ServerState(ServerStateValue.ShuttingDown);
/// <summary>
/// The LSP Server application has received an <c>Exit</c> notification and the process will exit with the <see cref="ExitingServerState.ExitCode"/> value.
/// </summary>
/// <param name="PreviousState">The previous server state.</param>
/// <remarks>
/// 👉 <c>ExitCode</c> is an error code unless the current state is <see cref="ServerStateValue.ShuttingDown"/> or <see cref="ServerStateValue.Starting"/>
/// <br/><a href="https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#exit">Server lifecycle § Exit Notification</a>
/// </remarks>
public record class ExitingServerState(ServerStateValue PreviousState) : ServerState(ServerStateValue.Exiting)
{
    /// <summary>
    /// The <em>exit code</em> that the application process should use to exit.
    /// </summary>
    public override int ExitCode => PreviousState is ServerStateValue.ShuttingDown or ServerStateValue.Starting ? 0 : 1;
}
