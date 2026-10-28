using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.SDK.Server.Configuration;
using System.Globalization;

namespace RDCore.SDK.Formatting;

/// <summary>
/// Keeps <c>Formatting.IndentSpaces</c> and the tab size of the client's editor the same, through the configuration the client owns: one setting, said twice.
/// </summary>
/// <remarks>
/// <para>
/// Rubberduck set the tab size of the VBA editor from its indenter's setting. A client owns its configuration (<strong>LSP 3.17</strong> §Configuration Request),
/// so a server sets nothing of it: the client says its settings, and a change of the editor's tab size is a change of configuration like any other. The server asks
/// for two things whenever the client says the configuration changed (<c>workspace/didChangeConfiguration</c>, then <c>workspace/configuration</c>): the platform's
/// live settings (<see cref="LiveSettingsProtocol.Section"/>), which hold <c>Formatting.IndentSpaces</c> when the user set it, and the editor's own
/// <see cref="EditorSection"/> (<see cref="RequestEditorTabSizeAsync"/>).
/// </para>
/// <para>
/// When <c>Formatting.IndentSpaces</c> was never set, the indentation is the editor's tab size for as long as the server runs: nothing is written, and a client that
/// stops saying a tab size takes it back to the default. When both are set and differ, <c>Formatting.IndentSpaces</c> wins: the formatting is made with the
/// setting whatever <c>tabSize</c> a request to format carries. A client that wants the two to agree sets one from the other, in its own configuration.
/// </para>
/// </remarks>
public static class FormattingTabSize
{
    /// <summary>The configuration section of the client's editor that carries its tab size, which the server asks for besides <see cref="LiveSettingsProtocol.Section"/>.</summary>
    public const string EditorSection = "editor.tabSize";

    /// <summary>
    /// The tab size of the client's editor, from what the client answered.
    /// </summary>
    /// <param name="answer">The client's answer for <see cref="EditorSection"/>: a number, or the number as text.</param>
    /// <returns>The tab size; <see langword="null"/> when the client said none, or one the indentation cannot be.</returns>
    public static int? EditorTabSizeOf(JToken? answer)
        => int.TryParse(answer is JValue { Value: not null } value ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) : null, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)
            && size is >= 1 and <= CodeFormattingOptions.MaximumIndentSpaces ? size : null;

    /// <summary>
    /// Asks the client for the tab size of its editor (<c>workspace/configuration</c>, <see cref="EditorSection"/>).
    /// </summary>
    /// <remarks>
    /// OmniSharp's own configuration keeps what a client answers only when it is an object, and the tab size is a number: it is asked for as a request of its own.
    /// </remarks>
    /// <param name="server">The language server.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The tab size; <see langword="null"/> when the client does not have configuration to ask, said none, or did not answer.</returns>
    public static async Task<int?> RequestEditorTabSizeAsync(ILanguageServerFacade server, CancellationToken token = default)
    {
        if (server.ClientSettings.Capabilities?.Workspace?.Configuration.IsSupported != true)
        {
            return null;
        }

        try
        {
            var answers = await server.Workspace.RequestConfiguration(new ConfigurationParams { Items = new Container<ConfigurationItem>(new ConfigurationItem { Section = EditorSection }) }, token);
            return EditorTabSizeOf(answers.FirstOrDefault());
        }
        catch (Exception exception) when (exception is OperationCanceledException or OmniSharp.Extensions.JsonRpc.RpcErrorException)
        {
            return null;
        }
    }

    /// <summary>
    /// The live settings the client said, with the editor's tab size as the indentation when the user never set one.
    /// </summary>
    /// <param name="said">What the client said the live settings are, by their keys under <see cref="SettingsSchema.Root"/>, separated by <c>:</c>.</param>
    /// <param name="editorTabSize">The tab size of the client's editor; <see langword="null"/> when the client has none to say.</param>
    /// <returns><paramref name="said"/> itself when the user set the indentation or the editor has no tab size; otherwise a copy that says the indentation is the editor's tab size.</returns>
    public static IReadOnlyDictionary<string, string> WithEditorTabSize(IReadOnlyDictionary<string, string> said, int? editorTabSize)
    {
        const string key = $"{nameof(SdkAppOptions.Formatting)}:{nameof(SdkFormattingOptions.IndentSpaces)}";
        if (editorTabSize is not { } size || said.ContainsKey(key))
        {
            return said;
        }

        return new Dictionary<string, string>(said, StringComparer.OrdinalIgnoreCase) { [key] = size.ToString(CultureInfo.InvariantCulture) };
    }
}
