using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// Handles <c>rdcore/session/memory/peek</c>: relays a client's byte read to the component that owns
/// the runtime session.
/// </summary>
internal sealed class SessionPeekHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities,
    ILogger<SessionPeekHandler> logger)
    : RDCoreRequestHandler<PeekSessionParams, PeekSessionResult>
{
    protected override async Task<PeekSessionResult> HandleAsync(PeekSessionParams request, CancellationToken token)
    {
        SessionMemoryRelay.RequireCapability(clientCapabilities);

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            logger.LogWarning("rdcore/session/memory/peek: no runtime environment component is registered.");
            return new PeekSessionResult { Address = request.Address };
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostPeekParams, PeekSessionResult>(
            new HostPeekParams { Address = request.Address }, token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/memory/poke</c>: relays a client's byte write to the component that owns
/// the runtime session.
/// </summary>
internal sealed class SessionPokeHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities,
    ILogger<SessionPokeHandler> logger)
    : RDCoreRequestHandler<PokeSessionParams, PokeSessionResult>
{
    protected override async Task<PokeSessionResult> HandleAsync(PokeSessionParams request, CancellationToken token)
    {
        SessionMemoryRelay.RequireCapability(clientCapabilities);

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            logger.LogWarning("rdcore/session/memory/poke: no runtime environment component is registered.");
            return new PokeSessionResult { Address = request.Address };
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostPokeParams, PokeSessionResult>(
            new HostPokeParams { Address = request.Address, Value = request.Value }, token);
    }
}

/// <summary>
/// What the two memory-access relays share: the capability both are gated on.
/// </summary>
internal static class SessionMemoryRelay
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    /// <summary>
    /// Refuses the request unless the client advertised that it reads and writes session memory.
    /// </summary>
    /// <param name="clientCapabilities">What the connected client asked this server for.</param>
    /// <exception cref="RpcErrorException">The client never advertised the capability.</exception>
    public static void RequireCapability(IPlatformClientCapabilitiesService clientCapabilities)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.SessionMemoryAccess))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(SessionMemoryAccess)}' platform capability.");
        }
    }
}
