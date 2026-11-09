using RDCore.Runtime.Execution;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Platform.Components;
using RDCore.Runtime.Semantics;
using SupportResources = RDCore.Runtime.Support.Resources;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/evaluate</c>: the value of an expression in an activation of the program that waits.
/// </summary>
internal sealed class HostDebugEvaluateHandler(IEnvironmentSessionProvider sessionProvider, IErrorDetailsRecorder errorDetails)
    : RDCoreRequestHandler<HostDebugEvaluateParams, DebugEvaluateResult>
{
    protected override Task<DebugEvaluateResult> HandleAsync(HostDebugEvaluateParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return Task.FromResult(new DebugEvaluateResult { Error = Resources.Host_NoRuntimeSession });
        }

        if (sessionProvider.Execution.State is not ProgramState.Suspended)
        {
            return Task.FromResult(new DebugEvaluateResult
            {
                Error = sessionProvider.Execution.State is ProgramState.Running ? SupportResources.Host_TheProgramIsRunning : SupportResources.Host_NoProgramIsSuspended,
            });
        }

        // nothing of the project runs while a module of it does not load: an expression evaluated at the stop, which can call its code, either.
        if (HeldDefinitions.FailedToLoad(sessionProvider.Session) is { Count: > 0 } failed)
        {
            return Task.FromResult(new DebugEvaluateResult { Error = string.Join(Environment.NewLine, failed) });
        }

        ExpressionNode expression;
        try
        {
            expression = PlatformJson.Deserialize<ExpressionNode>(request.Json);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException)
        {
            return Task.FromResult(new DebugEvaluateResult { Error = SupportResources.Host_RequestCarriedNoExpression });
        }

        var pipeline = RuntimeExecutionPipeline.Create(sessionProvider.Session, sessionProvider.Image, errorDetails, outside: sessionProvider.Outside);
        // what it runs may open a dialog that waits for an answer over this very connection: it runs on a thread of its own, and not on the one that reads it.
        return ProgramExecution.OffTheConnectionAsync(() => sessionProvider.Execution.Evaluate(pipeline, request.FrameId, expression));
    }
}
