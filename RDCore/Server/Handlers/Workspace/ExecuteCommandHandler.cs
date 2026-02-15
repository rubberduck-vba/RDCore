using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.Server.Commands;

namespace RDCore.Server.Handlers.Workspace;

internal class ExecuteCommandHandler(IEnumerable<ServerCommand> commands) : ExecuteCommandHandlerBase
{
    public async override Task<Unit> Handle(ExecuteCommandParams request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var command = commands.SingleOrDefault(e => e.CanHandle(request.Command));
        if (command is ServerCommand serverCommand)
        {
            serverCommand.Execute(request.Arguments);
        }
        else
        {
            // command was not found. TODO log something here.
        }

        return await Task.FromResult(Unit.Value);
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions
        {
            WorkDoneProgress = true,
            Commands = new Container<string>(
                nameof(AddReferenceCommand),
                nameof(RemoveReferenceCommand)
            )
        };
    }
}