using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/goto</c>: moves the point the program that waits goes on from, or says where a move would land.
/// </summary>
internal sealed class HostDebugGotoHandler(IEnvironmentSessionProvider sessionProvider) : RDCoreRequestHandler<HostDebugGotoParams, HostDebugGotoResult>
{
    protected override Task<HostDebugGotoResult> HandleAsync(HostDebugGotoParams request, CancellationToken token)
        => Task.FromResult(sessionProvider.IsComposed
            ? sessionProvider.Execution.Goto(request.Line, request.Label, request.ModuleName, request.Peek)
            : new HostDebugGotoResult { Reason = Resources.Host_NoRuntimeSession });
}
