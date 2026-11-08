using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace RDCore.SDK.Client;

/// <summary>
/// The <c>System.Text.Json</c> settings that write and read the <c>rdcore/*</c> messages of this package exactly as the platform's own processes do.
/// </summary>
/// <remarks>
/// <para>
/// A client that speaks to the language server with this package alone - an IDE add-in over its host's message channel, an editor extension - serializes the
/// parameters it sends and deserializes the results it receives with these options, and the language server reads and writes the same JSON.
/// </para>
/// <para>
/// The wire they produce:
/// </para>
/// <list type="bullet">
/// <item><description>member names in <c>camelCase</c>, read regardless of case;</description></item>
/// <item><description>enumerations as their <em>numeric</em> value, except where a type says otherwise (<see cref="Platform.Protocol.RubberduckIndenterSettingsSource"/> is written by name);</description></item>
/// <item><description><see langword="null"/> members written as <c>null</c>, except the capability groups of <see cref="CorePlatformClientCapabilities"/>, which are left out;</description></item>
/// <item><description>a <see cref="Uri"/> as its original text, and <see cref="Model.Source.SourcePosition"/> as an LSP position (<c>{ "line": 0, "character": 0 }</c>);</description></item>
/// <item><description><see cref="Model.Source.SourceRange"/> as an LSP range with one more member, <c>isEmpty</c>, which a reader ignores:
/// <c>{ "start": { … }, "end": { … }, "isEmpty": false }</c>.</description></item>
/// </list>
/// <para>
/// The instance is read-only: copy it (<c>new JsonSerializerOptions(RDCorePlatformJson.Options)</c>) to add a converter of your own.
/// </para>
/// </remarks>
public static class RDCorePlatformJson
{
    /// <summary>
    /// The options every <c>rdcore/*</c> message of this package is written and read with.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        options.MakeReadOnly();
        return options;
    }
}
