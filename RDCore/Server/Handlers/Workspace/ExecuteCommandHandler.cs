using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.SDK.Server.Commands;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Server.Handlers.Workspace;

internal class ExecuteCommandHandler(IServerCommandProvider commands) : ExecuteCommandHandlerBase
{
    public async override Task<Unit> Handle(ExecuteCommandParams request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (commands.GetCommand(request.Command) is ServerCommand command)
        {
            await command.ExecuteAsync(cancellationToken, request.Arguments);
        }
        return await Task.FromResult(Unit.Value);
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions
        {
            WorkDoneProgress = ClientCapabilities.Window?.WorkDoneProgress.Value ?? false,
            Commands = Container.From(commands.GetCommandRegistrations())
        };
    }
}