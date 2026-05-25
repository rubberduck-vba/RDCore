using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Server.Services.States;

public abstract record class ServerState
{
    public static ServerState Starting { get; } = new StartingServerState();
    public static ServerState Initializing { get; } = new InitializingServerState();
    public static ServerState Running { get; } = new RunningServerState();
    public static ServerState RunningVerbose { get; } = new RunningVerboseServerState();
    public static ServerState RunningTraceless { get; } = new RunningTracelessServerState();
    public static ServerState ShuttingDown { get; } = new ShuttingDownServerState();
    public static ServerState Exiting(ServerStateValue state) => new ExitingServerState(state);

    protected ServerState(ServerStateValue value)
    {
        Value = value;
    }

    public ServerStateValue Value { get; }

    public virtual int ExitCode => 1;
}

public record class StartingServerState : ServerState
{
    public StartingServerState() : base(ServerStateValue.Starting) { }
    public override int ExitCode => 0;
}
public record class InitializingServerState : ServerState { public InitializingServerState() : base(ServerStateValue.Initializing) { } }
public record class RunningServerState : ServerState
{
    public RunningServerState(InitializeTrace trace = InitializeTrace.Messages) : base(ServerStateValue.Running)
    {
        Trace = trace;
    }

    protected RunningServerState(InitializeTrace trace = InitializeTrace.Messages, ServerStateValue state = ServerStateValue.Running) : base(state)
    {
        Trace = trace;
    }

    public InitializeTrace Trace { get; }
}
public record class RunningVerboseServerState : RunningServerState { public RunningVerboseServerState() : base(InitializeTrace.Verbose, ServerStateValue.RunningVerbose) { } }
public record class RunningTracelessServerState : RunningServerState { public RunningTracelessServerState() : base(InitializeTrace.Off, ServerStateValue.RunningTraceless) { } }
public record class ShuttingDownServerState : ServerState { public ShuttingDownServerState() : base(ServerStateValue.ShuttingDown) { } }
public record class ExitingServerState : ServerState
{
    public ExitingServerState(ServerStateValue state) : base(ServerStateValue.Exiting)
    {
        PreviousState = state;
    }

    public ServerStateValue PreviousState { get; }
    public override int ExitCode => PreviousState is ServerStateValue.ShuttingDown or ServerStateValue.Starting ? 0 : 1;
}
