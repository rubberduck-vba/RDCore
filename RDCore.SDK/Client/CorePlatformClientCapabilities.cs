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
}

[JsonDerivedType(typeof(ParseFullDocument))]
[JsonDerivedType(typeof(DefineSymbols))]
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
