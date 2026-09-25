using Microsoft.Extensions.Logging;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// Handles <c>rdcore/session/status</c>: answers a client asking for the state of the platform's
/// runtime session, by asking the component that owns it.
/// </summary>
/// <remarks>
/// This is the shape every client-facing platform request takes. The client knows only the language
/// server; the language server knows the platform it assembled. A client that never advertised the
/// capability in the platform handshake is refused rather than served — the expectations half of that
/// handshake is what makes a client-driven request family negotiable instead of ambient.
/// <para>
/// The wait is the caller's to ask for: platform bring-up is asynchronous, so a status request can
/// legitimately land before the environment host has attached. A shell printing a readiness banner
/// asks to wait; anything polling does not.
/// </para>
/// </remarks>
internal sealed class SessionStatusHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities,
    ILogger<SessionStatusHandler> logger)
    : RDCoreRequestHandler<SessionStatusParams, SessionStatusResult>
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    protected override async Task<SessionStatusResult> HandleAsync(SessionStatusParams request, CancellationToken token)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.SessionStatus))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(SessionStatus)}' platform capability.");
        }

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            logger.LogWarning("rdcore/session/status: no runtime environment component is registered.");
            return new SessionStatusResult();
        }

        try
        {
            if (request.WaitMilliseconds > 0)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(request.WaitMilliseconds);
                await environment.WaitForReadyAsync(deadline.Token);
            }

            return await environment.SendRequestAsync<HostSessionStatusParams, SessionStatusResult>(new(), token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // the client's own wait ran out, not its request: answer "no session yet" rather than fault.
            logger.LogInformation("rdcore/session/status: the runtime environment was not ready within {wait}ms.", request.WaitMilliseconds);
            return new SessionStatusResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "rdcore/session/status could not be serviced by the runtime environment.");
            return new SessionStatusResult();
        }
    }
}
