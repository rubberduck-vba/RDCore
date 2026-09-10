using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Client;

/// <summary>
/// Describes all core platform capabilities.
/// </summary>
public class CorePlatformClientCapabilities
{
    /// <summary>
    /// The core platform capabilities of the parser process.
    /// </summary>
    [Optional]
    public ParserCapabilities? Parsing { get; set; }

    /// <summary>
    /// The core platform capabilities of the environment-host process.
    /// </summary>
    [Optional]
    public EnvironmentHostCapabilities? EnvironmentHost { get; set; }
}

/// <summary>
/// Regroups all core platform <c>Parsing</c> capabilities.
/// </summary>
public class ParserCapabilities
{
    /// <summary>
    /// If supported, enables the language server to request a parse result containing the full syntax tree of a specified workspace document.
    /// </summary>
    public ParseFullDocument ParseFullDocument { get; set; } = new();
}

/// <summary>
/// Regroups all core platform <c>EnvironmentHost</c> capabilities.
/// </summary>
public class EnvironmentHostCapabilities
{
    /// <summary>
    /// If supported, the environment host accepts module member symbol descriptors and defines them in its runtime session.
    /// </summary>
    public DefineSymbols DefineSymbols { get; set; } = new();
}

public static class RDCorePlatformProtocol
{
    /// <summary>
    /// Requests an AST from the parser for a full document.
    /// </summary>
    public const string ParseFullDocument = "rdcore/parser/document";

    /// <summary>
    /// Sends a module's member symbol descriptors to the environment host to define in its runtime session.
    /// </summary>
    public const string DefineSymbols = "rdcore/host/symbols/define";

    /// <summary>
    /// Hands a diagnostics-provider extension a parsed document and asks for the diagnostics it finds.
    /// </summary>
    public const string DiagnoseDocument = "rdcore/diagnostics/document";
}

[JsonDerivedType(typeof(ParseFullDocument))]
[JsonDerivedType(typeof(DefineSymbols))]
[JsonDerivedType(typeof(CliCommand))]
[JsonDerivedType(typeof(DiagnoseDocument))]
[JsonPolymorphic]
public abstract record class CorePlatformClientCapability(bool IsSupported = true);

/// <summary>
/// Enables the language server to request a parse result containing the full syntax tree of a specified workspace document.
/// </summary>
public record class ParseFullDocument(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Enables the language server to send module member symbol descriptors to the environment host over
/// <c>rdcore/host/symbols/define</c> for definition in the runtime session.
/// </summary>
public record class DefineSymbols(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the declaring extension answers <c>rdcore/diagnostics/document</c> — it is a
/// diagnostics provider the language server fans <c>textDocument/diagnostic</c> pulls out to.
/// </summary>
/// <remarks>
/// Declared via <c>[assembly: ProvidesCorePlatformClientCapability&lt;DiagnoseDocument&gt;]</c>;
/// <c>rdc.exe describe-ext</c> records it in the extension's <see cref="RDCore.SDK.Extensibility.ExtensionInfo"/>
/// manifest, which is where the language server reads it — nothing is negotiated over
/// <c>rdcore/platform/initialize</c> for this capability.
/// </remarks>
public record class DiagnoseDocument(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the declaring component contributes <c>rdc.exe</c> command-line verbs.
/// </summary>
/// <remarks>
/// Declared via <c>[assembly: ProvidesCorePlatformClientCapability&lt;CliCommand&gt;]</c>. The CLI itself
/// declares it for its native verbs; an extension declares it to have <c>rdc.exe describe-ext</c> record
/// the capability in its <see cref="RDCore.SDK.Extensibility.ExtensionInfo"/> manifest. Providers are
/// <see cref="CoreServerComponent.ClientApp"/> and <see cref="CoreServerComponent.Extension"/>. Currently
/// informational: nothing is negotiated over <c>rdcore/platform/initialize</c> for this capability yet.
/// </remarks>
public record class CliCommand(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);
