using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/breakpoints</c>: sets the lines of a module that a program under a debugger waits at.
/// </summary>
internal sealed class HostDebugBreakpointsHandler(IEnvironmentSessionProvider sessionProvider)
    : RDCoreRequestHandler<HostDebugBreakpointsParams, DebugBreakpointsResult>
{
    protected override Task<DebugBreakpointsResult> HandleAsync(HostDebugBreakpointsParams request, CancellationToken token)
        => Task.FromResult(sessionProvider.IsComposed
            ? sessionProvider.Execution.SetBreakpoints(request.ModuleName, request.Breakpoints)
            : new DebugBreakpointsResult { Breakpoints = [.. request.Breakpoints.Select(breakpoint => new DebugBreakpoint(breakpoint.Line, false))] });
}
