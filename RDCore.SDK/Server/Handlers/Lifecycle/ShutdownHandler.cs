using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.General;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Server.Services.States;

namespace RDCore.SDK.Server.Handlers.Lifecycle;

public class ShutdownHandler(ILogger<IJsonRpcHandler> logger, IServerStateProvider server) : ShutdownHandlerBase
{
    public async override Task<Unit> Handle(ShutdownParams request, CancellationToken cancellationToken)
    {
        logger.LogTrace("Received Shutdown notification.");
        cancellationToken.ThrowIfCancellationRequested();

        server.OnShutdown();

        return await Task.FromResult(Unit.Value);
    }
}