using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.LanguageServer.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// Handles <c>rdcore/session/debug/resume</c>: relays a client's request to go on with the program it ran under a debugger to the component that owns the runtime
/// session, and answers what that answers - where the program waits next, or how it ended.
/// </summary>
internal sealed class SessionDebugResumeHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities,
    ILogger<SessionDebugResumeHandler> logger)
    : RDCoreRequestHandler<SessionDebugResumeParams, ExecuteSessionResult>
{
    protected override async Task<ExecuteSessionResult> HandleAsync(SessionDebugResumeParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            logger.LogWarning("rdcore/session/debug/resume: no runtime environment component is registered.");
            return new ExecuteSessionResult { Outcome = ExecutionOutcome.NotFound, ErrorMessage = "no runtime environment component is registered" };
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugResumeParams, ExecuteSessionResult>(new HostDebugResumeParams { Step = request.Step }, token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/pause</c>: relays a client's request to stop the program that is running.
/// </summary>
internal sealed class SessionDebugPauseHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugPauseParams, HostDebugAck>
{
    protected override async Task<HostDebugAck> HandleAsync(SessionDebugPauseParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugAck();
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugPauseParams, HostDebugAck>(new HostDebugPauseParams(), token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/goto</c>: relays a client's request to move the point the program that waits goes on from.
/// </summary>
internal sealed class SessionDebugGotoHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugGotoParams, HostDebugGotoResult>
{
    protected override async Task<HostDebugGotoResult> HandleAsync(SessionDebugGotoParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugGotoResult { Reason = "no runtime environment component is registered" };
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugGotoParams, HostDebugGotoResult>(
            new HostDebugGotoParams { Line = request.Line, Label = request.Label }, token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/terminate</c>: relays a client's request to end the program that is running or waits.
/// </summary>
internal sealed class SessionDebugTerminateHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugTerminateParams, HostDebugAck>
{
    protected override async Task<HostDebugAck> HandleAsync(SessionDebugTerminateParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugAck();
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugTerminateParams, HostDebugAck>(new HostDebugTerminateParams { Wipe = request.Wipe }, token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/breakpoints</c>: relays the lines of a module a client wants the program to wait at.
/// </summary>
internal sealed class SessionDebugBreakpointsHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugBreakpointsParams, HostDebugBreakpointsResult>
{
    protected override async Task<HostDebugBreakpointsResult> HandleAsync(SessionDebugBreakpointsParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugBreakpointsResult { Breakpoints = [.. request.Lines.Select(line => new HostBreakpoint(line, false))] };
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugBreakpointsParams, HostDebugBreakpointsResult>(
            new HostDebugBreakpointsParams { ModuleName = request.ModuleName, Lines = request.Lines }, token);
    }
}

/// <summary>
/// What the debugging relays share: the capability they are gated on.
/// </summary>
internal static class SessionDebugRelay
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    /// <summary>
    /// Refuses the request unless the client advertised that it debugs programs.
    /// </summary>
    /// <param name="clientCapabilities">What the connected client asked this server for.</param>
    /// <exception cref="RpcErrorException">The client never advertised the capability.</exception>
    public static void RequireCapability(IPlatformClientCapabilitiesService clientCapabilities)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.ProgramDebugging))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(ProgramDebugging)}' platform capability.");
        }
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/stack</c>: relays a client's request for the activations of the program that waits.
/// </summary>
internal sealed class SessionDebugStackHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugStackParams, HostDebugStackResult>
{
    protected override async Task<HostDebugStackResult> HandleAsync(SessionDebugStackParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugStackResult();
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugStackParams, HostDebugStackResult>(new HostDebugStackParams(), token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/variables</c>: relays a client's request for the variables of an activation of the program that waits.
/// </summary>
internal sealed class SessionDebugVariablesHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugVariablesParams, HostDebugVariablesResult>
{
    protected override async Task<HostDebugVariablesResult> HandleAsync(SessionDebugVariablesParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugVariablesResult();
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugVariablesParams, HostDebugVariablesResult>(
            new HostDebugVariablesParams { FrameId = request.FrameId, Scope = request.Scope, Reference = request.Reference }, token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/evaluate</c>: parses the expression a client wrote and has the component that owns the runtime session evaluate it in an activation of
/// the program that waits.
/// </summary>
/// <remarks>
/// The parser parses statements, and an expression is the right-hand side of one: the text is written as the value of an assignment inside a procedure of its own, and the
/// value is the tree that comes back. Text that is not one expression - a second statement after a <c>:</c>, a line break - is not an expression, and the answer says so
/// without it reaching the host.
/// </remarks>
internal sealed class SessionDebugEvaluateHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities,
    IParsingClientService parsing,
    IOptions<SdkAppOptions> options)
    : RDCoreRequestHandler<SessionDebugEvaluateParams, HostDebugEvaluateResult>
{
    protected override async Task<HostDebugEvaluateResult> HandleAsync(SessionDebugEvaluateParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugEvaluateResult { Error = "no runtime environment component is registered" };
        }

        if (request.Expression.Contains('\n') || request.Expression.Contains('\r') || string.IsNullOrWhiteSpace(request.Expression))
        {
            return new HostDebugEvaluateResult { Error = "an expression is one line" };
        }

        var document = new UriBuilder(new Uri(options.Value.Workspace.WorkspaceUri)) { Fragment = "Evaluate" }.Uri;
        var parsed = await parsing.ParseFragmentAsync(document, $"Public Sub __Evaluate()\r\n__e = {request.Expression}\r\nEnd Sub\r\n", token);
        if (!parsed.IsSuccess || parsed.SyntaxTree is null)
        {
            return new HostDebugEvaluateResult { Error = parsed.SyntaxErrors.FirstOrDefault()?.Description ?? "this is not an expression" };
        }

        var assignments = Descendants(parsed.SyntaxTree).OfType<AssignmentStatementNode>().ToArray();
        if (assignments is not [{ Value: { } value }])
        {
            return new HostDebugEvaluateResult { Error = "this is not one expression" };
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugEvaluateParams, HostDebugEvaluateResult>(
            new HostDebugEvaluateParams { FrameId = request.FrameId, Json = PlatformJson.Serialize<ExpressionNode>(value) }, token);
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node) => node.Children.SelectMany(Descendants).Prepend(node);
}
