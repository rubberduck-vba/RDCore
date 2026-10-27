using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.SDK.Server.Commands;

namespace RDCore.LanguageServer.Server.Handlers.Workspace;

/// <summary>
/// Serves <c>workspace/executeCommand</c> (<strong>LSP 3.17</strong> §Execute a command): runs the server command the client names
/// (<see cref="IServerCommand"/>), and advertises the names of all of them.
/// </summary>
/// <remarks>
/// A command that is refused (<see cref="ServerCommandRefusedException"/>) is answered with an error (<c>RequestFailed</c>) whose message says why, which a
/// client shows its user, as a refused rename is. What a command asks the user is asked with <c>window/showMessageRequest</c>.
/// </remarks>
internal sealed class ExecuteCommandHandler(IEnumerable<IServerCommand> commands, ILanguageServerFacade server) : ExecuteCommandHandlerBase<JToken>
{
    /// <summary>JSON-RPC "Request Failed" (<strong>LSP 3.17</strong> <c>ErrorCodes.RequestFailed</c>): the request was valid, and is refused.</summary>
    private const int RequestFailedCode = -32803;

    private readonly IReadOnlyList<IServerCommand> _commands = [.. commands];

    public override async Task<JToken> Handle(ExecuteCommandParams<JToken> request, CancellationToken cancellationToken)
    {
        var command = _commands.FirstOrDefault(candidate => string.Equals(candidate.Name, request.Command, StringComparison.Ordinal))
            ?? throw new RpcErrorException(RequestFailedCode, error: null!, $"The server has no command '{request.Command}'.");
        try
        {
            return await command.ExecuteAsync(request.Arguments, new ClientContext(server), cancellationToken) ?? JValue.CreateNull();
        }
        catch (ServerCommandRefusedException refused)
        {
            throw new RpcErrorException(RequestFailedCode, error: null!, refused.Message);
        }
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
        => new() { Commands = new Container<string>(_commands.Select(command => command.Name)) };

    private sealed class ClientContext(ILanguageServerFacade server) : IServerCommandContext
    {
        public async Task<string?> AskAsync(string message, IReadOnlyList<string> choices, CancellationToken token)
        {
            var chosen = await server.Window.ShowMessageRequest(new ShowMessageRequestParams
            {
                Type = MessageType.Warning,
                Message = message,
                Actions = new Container<MessageActionItem>(choices.Select(choice => new MessageActionItem { Title = choice })),
            }, token);
            return chosen?.Title;
        }
    }
}
