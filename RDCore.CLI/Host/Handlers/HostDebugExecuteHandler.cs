using RDCore.Runtime.Execution;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Platform.Components;
using RDCore.Runtime.Semantics;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/execute</c>: runs a statement in an activation of the program that waits.
/// </summary>
internal sealed class HostDebugExecuteHandler(IEnvironmentSessionProvider sessionProvider, IErrorDetailsRecorder errorDetails)
    : RDCoreRequestHandler<HostDebugExecuteParams, DebugEvaluateResult>
{
    protected override Task<DebugEvaluateResult> HandleAsync(HostDebugExecuteParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return Task.FromResult(new DebugEvaluateResult { Error = Resources.Host_NoRuntimeSession });
        }

        if (sessionProvider.Execution.State is not ProgramState.Suspended)
        {
            return Task.FromResult(new DebugEvaluateResult
            {
                Error = sessionProvider.Execution.State is ProgramState.Running ? Resources.Host_TheProgramIsRunning : Resources.Host_NoProgramIsSuspended,
            });
        }

        // nothing of the project runs while a module of it does not load: a statement typed at the stop either.
        if (HeldDefinitions.FailedToLoad(sessionProvider.Session) is { Count: > 0 } failed)
        {
            return Task.FromResult(new DebugEvaluateResult { Error = string.Join(Environment.NewLine, failed) });
        }

        SyntaxNode statement;
        try
        {
            statement = PlatformJson.Deserialize<SyntaxNode>(request.Json);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException)
        {
            return Task.FromResult(new DebugEvaluateResult { Error = Resources.Host_RequestCarriedNoStatement });
        }

        var pipeline = RuntimeExecutionPipeline.Create(sessionProvider.Session, sessionProvider.Image, errorDetails, outside: sessionProvider.Outside);
        // what it runs may open a dialog that waits for an answer over this very connection: it runs on a thread of its own, and not on the one that reads it.
        return ProgramExecution.OffTheConnectionAsync(() => sessionProvider.Execution.Execute(pipeline, request.FrameId, statement));
    }
}
