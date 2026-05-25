using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Server.Services.States;

namespace RDCore.SDK.Server.Handlers.Lifecycle;

public class SetTraceHandler(ILogger<IJsonRpcHandler> logger, IServerStateProvider server) : SetTraceHandlerBase
{
    public async override Task<Unit> Handle(SetTraceParams request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogTrace("Received SetTrace request.");

        if (request.Value == InitializeTrace.Off && server.State is RunningServerState and not RunningTracelessServerState)
        {
            server.OnTraceOff();
        }
        else if (request.Value == InitializeTrace.Messages && server.State is RunningServerState)
        {
            server.OnTraceMessages();
        }
        else if (request.Value == InitializeTrace.Verbose && server.State is RunningServerState and not RunningVerboseServerState)
        {
            server.OnTraceVerbose();
        }

        return await Task.FromResult(Unit.Value);
    }
}