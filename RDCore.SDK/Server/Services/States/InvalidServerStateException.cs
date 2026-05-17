namespace RDCore.SDK.Server.Services.States;

public class InvalidServerStateException : InvalidOperationException
{
    public InvalidServerStateException() : base("This operation is invalid in the current server state.") { }
    public InvalidServerStateException(ServerStateValue state)
        : base($"This operation (set state: {state}) is invalid in the current server state.") { }
}
