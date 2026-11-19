using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Commands;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Workspace;

/// <summary>
/// <c>rdcore.listSettingValue</c>: adds an item to the list of a setting, or removes one from it, in the user's settings of the platform or in the workspace's: the
/// command of the code action that disables a diagnostic (<c>Analysis.DisabledDiagnostics</c>).
/// </summary>
/// <remarks>
/// <para>
/// A client owns its configuration, and the language server writes none of it: it asks the client to make the change (<c>rdcore/settings/listValue</c>), and the
/// client says the settings changed once it did. The command is refused by a client that did not say it expects <see cref="SDK.Client.ListSettingValue"/>.
/// </para>
/// <para>
/// Its one argument is the change, as the request carries it (<see cref="ListSettingValueParams"/>): <c>{ "key": "Analysis.DisabledDiagnostics", "value": "RDC01001",
/// "scope": 0, "remove": false, "document": "file:///..." }</c>. The answer is <c>{ "applied": true }</c>, or <see langword="false"/> when the client did not make the
/// change, or the argument is no change.
/// </para>
/// </remarks>
internal sealed class ListSettingValueCommand(ILanguageServerFacade server, IPlatformClientCapabilitiesService capabilities) : IServerCommand
{
    /// <summary>
    /// The name a client asks for the command by.
    /// </summary>
    public const string CommandName = "rdcore.listSettingValue";

    public string Name => CommandName;

    /// <summary>
    /// The argument of the command that makes a change.
    /// </summary>
    /// <param name="change">The change.</param>
    public static JArray ArgumentsOf(ListSettingValueParams change) => [JObject.FromObject(change, JsonSerializer.Create(Wire))];

    // the change written as the request carries it: its members camel-cased.
    private static readonly JsonSerializerSettings Wire = new() { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() };

    public async Task<JToken?> ExecuteAsync(JArray? arguments, IServerCommandContext context, CancellationToken token)
    {
        if (!capabilities.Expects(group => group.ListSettingValue))
        {
            throw new ServerCommandRefusedException(RefactoringTitles.SettingListNotTaken());
        }

        var change = arguments?.FirstOrDefault() is JObject argument ? argument.ToObject<ListSettingValueParams>(JsonSerializer.Create(Wire)) : null;
        if (change is not { Key.Length: > 0, Value.Length: > 0 })
        {
            return new JObject { ["applied"] = false };
        }

        var answer = await server.SendRequest(new ListSettingValueRequest(change), token);
        return new JObject { ["applied"] = answer.Applied };
    }
}
