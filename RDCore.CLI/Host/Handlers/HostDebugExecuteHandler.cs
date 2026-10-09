using RDCore.Runtime.Execution;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/execute</c>: runs a statement in an activation of the program that waits.
/// </summary>
internal sealed class HostDebugExecuteHandler(IEnvironmentSessionProvider sessionProvider, IVerboseMessageBuilder messages)
    : RDCoreRequestHandler<HostDebugExecuteParams, HostDebugEvaluateResult>
{
    protected override Task<HostDebugEvaluateResult> HandleAsync(HostDebugExecuteParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return Task.FromResult(new HostDebugEvaluateResult { Error = "there is no runtime session" });
        }

        if (sessionProvider.Execution.State is not ProgramState.Suspended)
        {
            return Task.FromResult(new HostDebugEvaluateResult { Error = sessionProvider.Execution.State is ProgramState.Running ? "the program is running" : "no program is suspended" });
        }

        SyntaxNode statement;
        try
        {
            statement = PlatformJson.Deserialize<SyntaxNode>(request.Json);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException)
        {
            return Task.FromResult(new HostDebugEvaluateResult { Error = "the request carried no statement" });
        }

        var pipeline = RuntimeExecutionPipeline.Create(sessionProvider.Session, sessionProvider.Image, messages);
        return Task.FromResult(sessionProvider.Execution.Execute(pipeline, request.FrameId, statement));
    }
}
