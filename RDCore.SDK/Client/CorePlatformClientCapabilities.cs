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

    /// <summary>
    /// The core platform capabilities of the language server — what a <em>client</em> (an IDE
    /// extension, <c>rdc.exe</c>'s interactive shell) expects the platform coordinator itself to
    /// serve beyond LSP.
    /// </summary>
    /// <remarks>
    /// A client never talks to the environment host, the parser or an extension: it talks to the
    /// language server, which owns them. So the capabilities a client asks for are the language
    /// server's, even where servicing one means fanning the work out to a child component.
    /// </remarks>
    [Optional]
    public LanguageServerCapabilities? LanguageServer { get; set; }
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

    /// <summary>
    /// If supported, the environment host reports the state of the runtime session it owns.
    /// </summary>
    public SessionStatus SessionStatus { get; set; } = new();

    /// <summary>
    /// If supported, the environment host lowers and runs a parsed module in its runtime session.
    /// </summary>
    public SessionExecute SessionExecute { get; set; } = new();
}

/// <summary>
/// Regroups all core platform <c>LanguageServer</c> capabilities — the non-LSP requests a client can
/// make of the platform coordinator.
/// </summary>
public class LanguageServerCapabilities
{
    /// <summary>
    /// If supported, the language server answers <c>rdcore/session/status</c> with the state of the
    /// platform's runtime session, including its memory.
    /// </summary>
    public SessionStatus SessionStatus { get; set; } = new();

    /// <summary>
    /// If supported, the language server runs a procedure of a module the client supplies, over
    /// <c>rdcore/session/execute</c>, and reports what it printed.
    /// </summary>
    public SessionExecute SessionExecute { get; set; } = new();
}

public static class RDCorePlatformProtocol
{
    /// <summary>
    /// Asks the language server for the state of the platform's runtime session.
    /// </summary>
    public const string SessionStatus = "rdcore/session/status";

    /// <summary>
    /// Asks the environment host for the state of the runtime session it owns. The language-server
    /// side of <see cref="SessionStatus"/>; never sent by a client.
    /// </summary>
    public const string HostSessionStatus = "rdcore/host/session/status";

    /// <summary>
    /// Asks the language server to run one procedure of a module the client supplies.
    /// </summary>
    public const string SessionExecute = "rdcore/session/execute";

    /// <summary>
    /// Asks the environment host to lower and run a parsed module in its runtime session. The
    /// language-server side of <see cref="SessionExecute"/>; never sent by a client.
    /// </summary>
    public const string HostExecute = "rdcore/host/execute";

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
[JsonDerivedType(typeof(SessionStatus))]
[JsonDerivedType(typeof(SessionExecute))]
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
/// Advertises that the declaring component answers <c>rdcore/session/status</c> — the state of the
/// runtime session, including how much of its memory is reserved, allocated and free.
/// </summary>
/// <remarks>
/// Provided by the language server to its client, and by the environment host to the language server
/// (as <c>rdcore/host/session/status</c>): one capability, both hops, because a client asking the
/// platform coordinator for the session and the coordinator asking the host that actually owns it are
/// the same question at two levels.
/// </remarks>
public record class SessionStatus(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the declaring component runs a procedure of a supplied module and reports what it
/// printed — <c>rdcore/session/execute</c> to a client, <c>rdcore/host/execute</c> between the
/// language server and the component that owns the runtime session.
/// </summary>
public record class SessionExecute(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

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
