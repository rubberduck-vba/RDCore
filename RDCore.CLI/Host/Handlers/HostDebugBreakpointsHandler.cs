using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/breakpoints</c>: sets the lines of a module that a program under a debugger waits at.
/// </summary>
internal sealed class HostDebugBreakpointsHandler(IEnvironmentSessionProvider sessionProvider)
    : RDCoreRequestHandler<HostDebugBreakpointsParams, HostDebugBreakpointsResult>
{
    protected override Task<HostDebugBreakpointsResult> HandleAsync(HostDebugBreakpointsParams request, CancellationToken token)
        => Task.FromResult(sessionProvider.IsComposed
            ? sessionProvider.Execution.SetBreakpoints(request.ModuleName, request.Lines)
            : new HostDebugBreakpointsResult { Breakpoints = [.. request.Lines.Select(line => new HostBreakpoint(line, false))] });
}
