using RDCore.Runtime.Execution;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/evaluate</c>: the value of an expression in an activation of the program that waits.
/// </summary>
internal sealed class HostDebugEvaluateHandler(IEnvironmentSessionProvider sessionProvider, IVerboseMessageBuilder messages)
    : RDCoreRequestHandler<HostDebugEvaluateParams, HostDebugEvaluateResult>
{
    protected override Task<HostDebugEvaluateResult> HandleAsync(HostDebugEvaluateParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return Task.FromResult(new HostDebugEvaluateResult { Error = Resources.Host_NoRuntimeSession });
        }

        if (sessionProvider.Execution.State is not ProgramState.Suspended)
        {
            return Task.FromResult(new HostDebugEvaluateResult
            {
                Error = sessionProvider.Execution.State is ProgramState.Running ? Resources.Host_TheProgramIsRunning : Resources.Host_NoProgramIsSuspended,
            });
        }

        ExpressionNode expression;
        try
        {
            expression = PlatformJson.Deserialize<ExpressionNode>(request.Json);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException)
        {
            return Task.FromResult(new HostDebugEvaluateResult { Error = Resources.Host_RequestCarriedNoExpression });
        }

        var pipeline = RuntimeExecutionPipeline.Create(sessionProvider.Session, sessionProvider.Image, messages, outside: sessionProvider.Outside);
        return Task.FromResult(sessionProvider.Execution.Evaluate(pipeline, request.FrameId, expression));
    }
}
