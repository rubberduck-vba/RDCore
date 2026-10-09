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

    /// <summary>
    /// If supported, enables the language server to request the lexical tokens of a text, which is what a document is highlighted by.
    /// </summary>
    public ParseTokens ParseTokens { get; set; } = new();
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

    /// <summary>
    /// If supported, the environment host takes a module out of its runtime session over <c>rdcore/host/discard</c>: what it declared, what it held,
    /// and the code and the model made of it.
    /// </summary>
    public SessionDiscard SessionDiscard { get; set; } = new();

    /// <summary>
    /// If supported, the environment host can run a program so that it stops and waits where a <c>Stop</c> or a break puts it, and resumes, steps or ends it
    /// over <c>rdcore/host/debug/*</c>.
    /// </summary>
    public ProgramDebugging ProgramDebugging { get; set; } = new();

    /// <summary>
    /// If supported, the environment host answers <c>rdcore/host/semantics</c> with the semantic model of the code it holds.
    /// </summary>
    public SemanticAnalysis SemanticAnalysis { get; set; } = new();

    /// <summary>
    /// If supported, the environment host reads and writes single bytes of the runtime session it owns.
    /// </summary>
    public SessionMemoryAccess SessionMemoryAccess { get; set; } = new();
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

    /// <summary>
    /// If supported, the language server runs the program a client asks it to under a debugger (<c>rdcore/session/execute</c> with <c>Debug</c>), and resumes, steps,
    /// pauses and ends it over <c>rdcore/session/debug/*</c>.
    /// </summary>
    public ProgramDebugging ProgramDebugging { get; set; } = new();

    /// <summary>
    /// If supported, the language server takes a module the client supplied out of the runtime session, over <c>rdcore/session/discard</c>: what it
    /// declared and what it held go, as a program's variables do when the program is cleared.
    /// </summary>
    public SessionDiscard SessionDiscard { get; set; } = new();

    /// <summary>
    /// If supported, the language server analyzes a module the client supplies, over
    /// <c>rdcore/session/analyze</c>, and reports the diagnostics its providers found.
    /// </summary>
    public SessionAnalyze SessionAnalyze { get; set; } = new();

    /// <summary>
    /// If supported, the language server reads and writes single bytes of the runtime session's
    /// memory, over <c>rdcore/session/memory/peek</c> and <c>.../poke</c>.
    /// </summary>
    public SessionMemoryAccess SessionMemoryAccess { get; set; } = new();
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
    /// Asks the language server to take a module the client supplied out of the runtime session.
    /// </summary>
    public const string SessionDiscard = "rdcore/session/discard";

    /// <summary>
    /// Asks the environment host to take a module out of its runtime session. The language-server side of <see cref="SessionDiscard"/>; never sent
    /// by a client.
    /// </summary>
    public const string HostDiscard = "rdcore/host/discard";

    /// <summary>
    /// Tells the language server what a program printed, as it prints: a notification from the environment host, sent for a program that was run with
    /// <see cref="HostExecuteParams.StreamOutput"/>.
    /// </summary>
    public const string HostOutput = "rdcore/host/output";

    /// <summary>
    /// Asks the environment host to resume a program that waits at a stop, or to take one step of it.
    /// </summary>
    public const string HostDebugResume = "rdcore/host/debug/resume";

    /// <summary>
    /// Asks the environment host to stop a program that is running where it is, so that it waits.
    /// </summary>
    public const string HostDebugPause = "rdcore/host/debug/pause";

    /// <summary>
    /// Asks the environment host to move the point a program that waits goes on from.
    /// </summary>
    public const string HostDebugGoto = "rdcore/host/debug/goto";

    /// <summary>
    /// Asks the environment host to set the lines of a module that a program under a debugger waits at.
    /// </summary>
    public const string HostDebugBreakpoints = "rdcore/host/debug/breakpoints";

    /// <summary>Asks the environment host for the call stack of the program that waits.</summary>
    public const string HostDebugStack = "rdcore/host/debug/stack";

    /// <summary>Asks the environment host for the variables of an activation of the program that waits.</summary>
    public const string HostDebugVariables = "rdcore/host/debug/variables";

    /// <summary>Asks the environment host for the value of an expression in an activation of the program that waits.</summary>
    public const string HostDebugEvaluate = "rdcore/host/debug/evaluate";

    /// <summary>Asks the environment host to run a statement in an activation of the program that waits.</summary>
    public const string HostDebugExecute = "rdcore/host/debug/execute";

    /// <summary>Asks the language server to resume, or step, the program a client ran under a debugger. See <see cref="HostDebugResume"/>.</summary>
    public const string SessionDebugResume = "rdcore/session/debug/resume";

    /// <summary>Asks the language server to stop the program that is running. See <see cref="HostDebugPause"/>.</summary>
    public const string SessionDebugPause = "rdcore/session/debug/pause";

    /// <summary>Asks the language server to move the point the program that waits goes on from. See <see cref="HostDebugGoto"/>.</summary>
    public const string SessionDebugGoto = "rdcore/session/debug/goto";

    /// <summary>Asks the language server to end the program that is running or waits. See <see cref="HostDebugTerminate"/>.</summary>
    public const string SessionDebugTerminate = "rdcore/session/debug/terminate";

    /// <summary>Asks the language server to set the lines of a module that a program under a debugger waits at. See <see cref="HostDebugBreakpoints"/>.</summary>
    public const string SessionDebugBreakpoints = "rdcore/session/debug/breakpoints";

    /// <summary>Asks the language server for the call stack of the program that waits. See <see cref="HostDebugStack"/>.</summary>
    public const string SessionDebugStack = "rdcore/session/debug/stack";

    /// <summary>Asks the language server for the variables of an activation of the program that waits. See <see cref="HostDebugVariables"/>.</summary>
    public const string SessionDebugVariables = "rdcore/session/debug/variables";

    /// <summary>Asks the language server for the value of an expression, as text, in an activation of the program that waits. See <see cref="HostDebugEvaluate"/>.</summary>
    public const string SessionDebugEvaluate = "rdcore/session/debug/evaluate";

    /// <summary>
    /// Asks the environment host to end a program that is running or waits.
    /// </summary>
    public const string HostDebugTerminate = "rdcore/host/debug/terminate";

    /// <summary>
    /// Asks the language server to analyze a module the client supplies.
    /// </summary>
    public const string SessionAnalyze = "rdcore/session/analyze";

    /// <summary>
    /// Reads one byte of the runtime session's memory.
    /// </summary>
    public const string SessionPeek = "rdcore/session/memory/peek";

    /// <summary>
    /// Writes one byte of the runtime session's memory.
    /// </summary>
    public const string SessionPoke = "rdcore/session/memory/poke";

    /// <summary>
    /// The language-server side of <see cref="SessionPeek"/>; never sent by a client.
    /// </summary>
    public const string HostPeek = "rdcore/host/memory/peek";

    /// <summary>
    /// The language-server side of <see cref="SessionPoke"/>; never sent by a client.
    /// </summary>
    public const string HostPoke = "rdcore/host/memory/poke";

    /// <summary>
    /// Requests an AST from the parser for a full document.
    /// </summary>
    public const string ParseFullDocument = "rdcore/parser/document";

    /// <summary>
    /// Requests the lexical tokens of a text from the parser.
    /// </summary>
    public const string ParseTokens = "rdcore/parser/tokens";

    /// <summary>
    /// Sends a module's member symbol descriptors to the environment host to define in its runtime session.
    /// </summary>
    public const string DefineSymbols = "rdcore/host/symbols/define";

    /// <summary>
    /// Asks the environment host for the semantic model of the code it holds. The language-server side of the diagnostics it hands an extension;
    /// never sent by a client.
    /// </summary>
    public const string HostSemantics = "rdcore/host/semantics";

    /// <summary>
    /// Hands a diagnostics-provider extension a parsed document and asks for the diagnostics it finds.
    /// </summary>
    public const string DiagnoseDocument = "rdcore/diagnostics/document";
}

[JsonDerivedType(typeof(ParseFullDocument))]
[JsonDerivedType(typeof(ParseTokens))]
[JsonDerivedType(typeof(DefineSymbols))]
[JsonDerivedType(typeof(CliCommand))]
[JsonDerivedType(typeof(DiagnoseDocument))]
[JsonDerivedType(typeof(SessionStatus))]
[JsonDerivedType(typeof(SessionExecute))]
[JsonDerivedType(typeof(SessionDiscard))]
[JsonDerivedType(typeof(ProgramDebugging))]
[JsonDerivedType(typeof(SessionAnalyze))]
[JsonDerivedType(typeof(SemanticAnalysis))]
[JsonDerivedType(typeof(SessionMemoryAccess))]
[JsonPolymorphic]
public abstract record class CorePlatformClientCapability(bool IsSupported = true);

/// <summary>
/// Enables the language server to request a parse result containing the full syntax tree of a specified workspace document.
/// </summary>
public record class ParseFullDocument(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Enables the language server to request the lexical tokens of a text over <c>rdcore/parser/tokens</c>.
/// </summary>
public record class ParseTokens(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

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
/// Advertises that the declaring component takes a module out of the runtime session — <c>rdcore/session/discard</c> to a client,
/// <c>rdcore/host/discard</c> between the language server and the component that owns the runtime session.
/// </summary>
/// <remarks>
/// What a client that clears a program means by it: the variables the program made go with the program, which defining it again never takes out.
/// </remarks>
public record class SessionDiscard(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the declaring component can run a program under a debugger: the program waits where a <c>Stop</c> or a break puts it
/// (<strong>MS-VBAL 5.4.2.11</strong>), and is resumed, stepped or ended by <c>rdcore/host/debug/resume</c>, <c>.../pause</c> and <c>.../terminate</c>.
/// </summary>
public record class ProgramDebugging(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the declaring component analyzes a supplied module and reports diagnostics —
/// <c>rdcore/session/analyze</c>, the editor-less counterpart to LSP's own diagnostics pull.
/// </summary>
public record class SessionAnalyze(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the declaring component reads and writes single bytes of a live runtime session's
/// memory — <c>PEEK</c> and <c>POKE</c>, unchecked.
/// </summary>
public record class SessionMemoryAccess(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the declaring component runs the semantic analysis pass over the code it holds and answers with its model - <c>rdcore/host/semantics</c>,
/// which is what a diagnostics extension analyzes.
/// </summary>
public record class SemanticAnalysis(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

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
