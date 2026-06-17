using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.SDK.Server.Commands;
using RDCore.SDK.Server.Services;

namespace RDCore.SDK.Server.Handlers;

public class ExecuteCommandHandler(IServerCommandProvider CommandProvider) : ExecuteCommandHandlerBase
{
    public async override Task<Unit> Handle(ExecuteCommandParams request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CommandProvider.GetCommand(request.Command) is ServerCommand command)
        {
            await command.ExecuteAsync(cancellationToken, request.Arguments);
        }
        return await Task.FromResult(Unit.Value);
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(ExecuteCommandCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        WorkDoneProgress = ClientCapabilities.Window?.WorkDoneProgress.Value ?? false,
        Commands = Container.From(CommandProvider.GetCommandRegistrations())
    };
}