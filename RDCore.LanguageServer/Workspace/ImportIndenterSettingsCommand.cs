using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Formatting;
using RDCore.SDK.Server.Commands;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Workspace;

/// <summary>
/// <c>rdcore.importIndenterSettings</c>: sets the user's <c>Formatting</c> settings to the indenter settings they had in Rubberduck, which the client reads
/// (<see cref="IndenterSettingsImportFlow"/>).
/// </summary>
/// <remarks>
/// <para>
/// The language server asks the client what it found (<c>rdcore/rubberduck-indenter-settings</c>), maps that to <c>Formatting</c> settings, and asks the client to take them as
/// its own (<c>rdcore/rubberduck-indenter-settings/apply</c>): a client owns its configuration, and the language server writes none of it. The command takes no argument, and
/// is refused by a client that did not say it expects <see cref="SDK.Client.RubberduckIndenterSettings"/>.
/// </para>
/// <para>
/// The answer is <c>{ "source": "RubberduckConfig" | "SmartIndenterRegistry" | "None", "found": n, "applied": n, "message": "..." }</c>, the message in the language of the
/// user interface: that nothing was found is an answer, not a refusal.
/// </para>
/// </remarks>
internal sealed class ImportIndenterSettingsCommand(ILanguageServerFacade server, IPlatformClientCapabilitiesService capabilities) : IServerCommand
{
    /// <summary>The name a client asks for the command by.</summary>
    public const string CommandName = "rdcore.importIndenterSettings";

    public string Name => CommandName;

    public async Task<JToken?> ExecuteAsync(JArray? arguments, IServerCommandContext context, CancellationToken token)
    {
        var client = new RubberduckIndenterSettingsClient(server, capabilities);
        if (!client.ClientExpects)
        {
            throw new ServerCommandRefusedException(IndenterSettingsImportText.Unsupported());
        }

        var outcome = await IndenterSettingsImportFlow.ImportAsync(client.ReadAsync, client.ApplyAsync, token);
        return new JObject
        {
            ["source"] = outcome.Source.ToString(),
            ["found"] = outcome.Found,
            ["applied"] = outcome.Applied,
            ["message"] = outcome.Message,
        };
    }
}
