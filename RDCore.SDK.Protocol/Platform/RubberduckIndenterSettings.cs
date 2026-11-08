using RDCore.SDK.Client;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Where the indenter settings a client read were found.
/// </summary>
/// <remarks>
/// Written by name on the wire (<c>"RubberduckConfig"</c>), unlike the other enumerations of the protocol.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RubberduckIndenterSettingsSource>))]
public enum RubberduckIndenterSettingsSource
{
    /// <summary>No settings were found, or the client is on a platform that has none.</summary>
    None = 0,

    /// <summary>Rubberduck's own configuration: the children of <c>IndenterSettings</c> in <c>%APPDATA%\Rubberduck\rubberduck.config</c>.</summary>
    RubberduckConfig = 1,

    /// <summary>The key of the legacy Smart Indenter in the Windows Registry: <c>HKCU\Software\VB and VBA Program Settings\Office Automation Ltd.\Smart Indenter</c>.</summary>
    SmartIndenterRegistry = 2,
}

/// <summary>
/// Request <c>rdcore/formatting/indenterSettings</c> (<see cref="RDCorePlatformProtocol.RubberduckIndenterSettings"/>): asks the client for the indenter settings its user had in
/// Rubberduck, as Rubberduck has them. The client answers a <see cref="RubberduckIndenterSettingsResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// Sent by the language server to a client that expects <see cref="RubberduckIndenterSettings"/>, once it offered to import them
/// (<strong>RD-VBAL §2.0.2.3</strong>). The client reads Rubberduck's settings where they are - its configuration, and the registry key of the legacy Smart Indenter -
/// and answers them <em>as they are</em>: it maps nothing. The language server maps them to the <c>Formatting</c> settings, so that the mapping is made once for every client.
/// </para>
/// <para>
/// A client on a platform that has no such settings answers <see cref="RubberduckIndenterSettingsSource.None"/>.
/// </para>
/// </remarks>
public record class RubberduckIndenterSettingsParams;

/// <summary>
/// What a client read of the indenter settings of Rubberduck, as Rubberduck has them: <c>{ "source": "RubberduckConfig", "values": { "IndentSpaces": "4" }, "tabWidth": null }</c>.
/// </summary>
public record class RubberduckIndenterSettingsResult
{
    /// <summary>Where the settings were found; <see cref="RubberduckIndenterSettingsSource.None"/> when they were not.</summary>
    public RubberduckIndenterSettingsSource Source { get; init; }

    /// <summary>
    /// The settings, by the name they have where they were found, with the value they have there: a JSON string or number each.
    /// </summary>
    /// <remarks>
    /// For <see cref="RubberduckIndenterSettingsSource.RubberduckConfig"/>, the name of each child of <c>IndenterSettings</c> and its text: <c>"IndentCase": "true"</c>.
    /// For <see cref="RubberduckIndenterSettingsSource.SmartIndenterRegistry"/>, the name of each value of the key (<c>IndentProc</c>, <c>DebugCol1</c>, <c>AlignDimCol</c>,
    /// <c>EOLComments</c>, ...), a number for a <c>REG_DWORD</c> and text for a <c>REG_SZ</c>. The language server looks the names up regardless of case.
    /// </remarks>
    public IReadOnlyDictionary<string, JsonElement> Values { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary>
    /// The <c>TabWidth</c> of the VBA editor (<c>HKCU\Software\Microsoft\VBA\6.0\Common</c>, or <c>7.0</c>), which the Smart Indenter indents by; <see langword="null"/> when it has none.
    /// </summary>
    public int? TabWidth { get; init; }

    /// <summary>Nothing was found.</summary>
    public static RubberduckIndenterSettingsResult Nothing { get; } = new();
}

/// <summary>
/// Request <c>rdcore/formatting/indenterSettings/apply</c> (<see cref="RDCorePlatformProtocol.RubberduckIndenterSettingsApply"/>): asks the client to take the indenter settings
/// it read as its own settings. The client answers a <see cref="RubberduckIndenterSettingsApplyResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// A client owns its configuration, and the language server writes none of it: this asks the client to set the <c>Formatting</c> settings the language server mapped
/// from what the client read, as a change of its own - the shell writes them to the user's settings. The client announces the change as it does any other
/// (<c>workspace/didChangeConfiguration</c>), and the language server sees the settings in the section it pulls. A client may decline, answering that it applied none.
/// </para>
/// <para>
/// Sent only after <see cref="RubberduckIndenterSettingsParams"/> was answered with settings, to a client that expects <see cref="RubberduckIndenterSettings"/>.
/// </para>
/// </remarks>
public record class RubberduckIndenterSettingsApplyParams
{
    /// <summary>Where the settings were read.</summary>
    public RubberduckIndenterSettingsSource Source { get; init; }

    /// <summary>The settings, by their keys under <c>Configuration</c>, with the value as it is written: <c>"Formatting.IndentSpaces": "2"</c>, <c>"Formatting.IndentCase": "True"</c>.</summary>
    public Dictionary<string, string> Settings { get; init; } = [];
}

/// <summary>
/// What a client did with the settings it was asked to apply.
/// </summary>
public record class RubberduckIndenterSettingsApplyResult
{
    /// <summary>How many of the settings the client set; <c>0</c> when it declined.</summary>
    public int Applied { get; init; }

    /// <summary>
    /// The keys of the settings the client did not take, when it took some or none: a key an import does not set, or a value its setting cannot be. A client that
    /// does not say (<see cref="Applied"/> is all it says) has the settings it took told as a number only.
    /// </summary>
    public IReadOnlyList<string> Refused { get; init; } = [];
}
