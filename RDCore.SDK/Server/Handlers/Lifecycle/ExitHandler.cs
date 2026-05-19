using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.General;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Server.Services.States;

namespace RDCore.SDK.Server.Handlers.Lifecycle;

public class ExitHandler(ILogger<IJsonRpcHandler> logger, IServerStateProvider server) : ExitHandlerBase
{
    /* LSP 3.17 Exit Notification
     * A notification to ask the server to exit its process. 
     * The server should exit with success code 0 if the shutdown request has been received before; otherwise with error code 1.
    */

    public async override Task<Unit> Handle(ExitParams request, CancellationToken cancellationToken)
    {
        logger.LogTrace("Received Exit notification.");
        cancellationToken.ThrowIfCancellationRequested();

        server.OnExit();

        return await Task.FromResult(Unit.Value);
    }
}