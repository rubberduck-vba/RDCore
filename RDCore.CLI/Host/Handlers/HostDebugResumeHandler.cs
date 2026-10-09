using Microsoft.Extensions.Logging;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/resume</c>: goes on with the program that waits at a stop, to the next place it waits or to its end, or for one step.
/// </summary>
/// <remarks>
/// The request is the program running, as <c>rdcore/host/execute</c> was: it answers when the program next waits (<see cref="ExecutionOutcome.Suspended"/>) or is over.
/// Cancelling it is a break.
/// </remarks>
internal sealed class HostDebugResumeHandler(
    IEnvironmentSessionProvider sessionProvider,
    ILogger<HostDebugResumeHandler> logger) : RDCoreRequestHandler<HostDebugResumeParams, ExecuteSessionResult>
{
    protected override async Task<ExecuteSessionResult> HandleAsync(HostDebugResumeParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return new ExecuteSessionResult { Outcome = ExecutionOutcome.NotFound, ErrorMessage = Resources.Host_NoRuntimeSessionToRunIn };
        }

        var result = await sessionProvider.Execution.ResumeAsync(request.Step, token);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("⏯️ resume ({step}): {outcome}", request.Step?.ToString() ?? "continue", result.Outcome);
        }

        return result;
    }
}
