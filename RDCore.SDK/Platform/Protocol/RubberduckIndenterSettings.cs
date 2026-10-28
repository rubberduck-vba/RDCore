using MediatR;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Where the indenter settings a client read were found.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
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
/// Request <c>rdcore/rubberduck-indenter-settings</c>: asks the client for the indenter settings its user had in Rubberduck, as Rubberduck has them.
/// </summary>
/// <remarks>
/// <para>
/// Sent by the language server to a client that expects <see cref="RubberduckIndenterSettings"/>, once it offered to import them
/// (<strong>RD-VBAL §2.0.2.3</strong>). The client reads Rubberduck's settings where they are - its configuration, and the registry key of the legacy Smart Indenter -
/// and answers them <em>as they are</em>: it maps nothing. The language server maps them to the <c>Formatting</c> settings
/// (<see cref="Formatting.IndenterSettingsImport"/>), so that the mapping is made once for every client.
/// </para>
/// <para>
/// A client on a platform that has no such settings answers <see cref="RubberduckIndenterSettingsSource.None"/>.
/// </para>
/// </remarks>
[Method(RDCorePlatformProtocol.RubberduckIndenterSettings, Direction.ServerToClient)]
public record class RubberduckIndenterSettingsParams : IRequest<RubberduckIndenterSettingsResult>;

/// <summary>
/// What a client read of the indenter settings of Rubberduck, as Rubberduck has them: <c>{ "source": "RubberduckConfig", "values": { "IndentSpaces": "4" }, "tabWidth": null }</c>.
/// </summary>
public record class RubberduckIndenterSettingsResult
{
    /// <summary>Where the settings were found; <see cref="RubberduckIndenterSettingsSource.None"/> when they were not.</summary>
    public RubberduckIndenterSettingsSource Source { get; init; }

    /// <summary>
    /// The settings, by the name they have where they were found, with the value they have there.
    /// </summary>
    /// <remarks>
    /// For <see cref="RubberduckIndenterSettingsSource.RubberduckConfig"/>, the name of each child of <c>IndenterSettings</c> and its text: <c>"IndentCase": "true"</c>.
    /// For <see cref="RubberduckIndenterSettingsSource.SmartIndenterRegistry"/>, the name of each value of the key (<c>IndentProc</c>, <c>DebugCol1</c>, <c>AlignDimCol</c>,
    /// <c>EOLComments</c>, ...), a number for a <c>REG_DWORD</c> and text for a <c>REG_SZ</c>.
    /// </remarks>
    public JObject Values { get; init; } = new();

    /// <summary>
    /// The <c>TabWidth</c> of the VBA editor (<c>HKCU\Software\Microsoft\VBA\6.0\Common</c>, or <c>7.0</c>), which the Smart Indenter indents by; <see langword="null"/> when it has none.
    /// </summary>
    public int? TabWidth { get; init; }

    /// <summary>Nothing was found.</summary>
    public static RubberduckIndenterSettingsResult Nothing { get; } = new();
}

/// <summary>
/// Request <c>rdcore/rubberduck-indenter-settings/apply</c>: asks the client to take the indenter settings it read as its own settings.
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
[Method(RDCorePlatformProtocol.RubberduckIndenterSettingsApply, Direction.ServerToClient)]
public record class RubberduckIndenterSettingsApplyParams : IRequest<RubberduckIndenterSettingsApplyResult>
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
}
