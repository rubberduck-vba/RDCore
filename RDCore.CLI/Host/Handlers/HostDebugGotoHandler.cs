using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Platform.Components;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/goto</c>: moves the point the program that waits goes on from, or says where a move would land.
/// </summary>
internal sealed class HostDebugGotoHandler(IEnvironmentSessionProvider sessionProvider) : RDCoreRequestHandler<HostDebugGotoParams, DebugGotoResult>
{
    protected override Task<DebugGotoResult> HandleAsync(HostDebugGotoParams request, CancellationToken token)
        => Task.FromResult(sessionProvider.IsComposed
            ? sessionProvider.Execution.Goto(request.Line, request.Label, request.ModuleName, request.Peek)
            : new DebugGotoResult { Reason = Resources.Host_NoRuntimeSession });
}
