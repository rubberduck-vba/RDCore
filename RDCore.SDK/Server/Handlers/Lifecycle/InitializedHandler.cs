using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.General;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Server.Services.States;

namespace RDCore.SDK.Server.Handlers.Lifecycle;

internal class InitializedHandler(ILogger<IJsonRpcHandler> logger, IServerStateProvider server) : LanguageProtocolInitializedHandlerBase
{
    public async override Task<Unit> Handle(InitializedParams request, CancellationToken cancellationToken)
    {
        logger.LogTrace("Received Initialized notification.");
        cancellationToken.ThrowIfCancellationRequested();

        server.OnInitialized();

        return await Task.FromResult(Unit.Value);
    }
}