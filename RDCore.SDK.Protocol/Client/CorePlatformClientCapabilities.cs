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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ParserCapabilities? Parsing { get; set; }

    /// <summary>
    /// The core platform capabilities of the environment-host process.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EnvironmentHostCapabilities? EnvironmentHost { get; set; }

    /// <summary>
    /// The core platform capabilities of the language server: what a <em>client</em> (an IDE
    /// extension, <c>rdc.exe</c>'s interactive shell) expects the platform coordinator itself to
    /// serve beyond LSP.
    /// </summary>
    /// <remarks>
    /// A client never talks to the environment host, the parser or an extension: it talks to the
    /// language server, which owns them. So the capabilities a client asks for are the language
    /// server's, even where servicing one means fanning the work out to a child component.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    /// If supported, the environment host composes its runtime session again from the project file as it is now, over <c>rdcore/host/project/reload</c>: the
    /// modules a rewritten <c>.rdproj</c> lists, which the next definition of the workspace defines.
    /// </summary>
    public ProjectReload ProjectReload { get; set; } = new();

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
/// Regroups all core platform <c>LanguageServer</c> capabilities: the non-LSP requests a client can
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
    /// If supported, the language server lists what can be written at a position of a module the client supplies, over <c>rdcore/session/completion</c>: the
    /// completion list of a line that is being typed at a prompt, which is no document.
    /// </summary>
    public SessionCompletion SessionCompletion { get; set; } = new();

    /// <summary>
    /// If supported, the language server says what the call being written at a position of a module the client supplies takes, over
    /// <c>rdcore/session/signatureHelp</c>: the signature help of a line that is being typed at a prompt, which is no document.
    /// </summary>
    public SessionSignatureHelp SessionSignatureHelp { get; set; } = new();

    /// <summary>
    /// If supported, the language server tells what the tokens of a code fragment the client supplies are, over <c>rdcore/session/semanticTokens</c>: the colours of
    /// a condition, or of a line that is being typed at a prompt, which are no document.
    /// </summary>
    public SessionSemanticTokens SessionSemanticTokens { get; set; } = new();

    /// <summary>
    /// If supported, the language server reads and writes single bytes of the runtime session's
    /// memory, over <c>rdcore/session/memory/peek</c> and <c>.../poke</c>.
    /// </summary>
    public SessionMemoryAccess SessionMemoryAccess { get; set; } = new();

    /// <summary>
    /// If supported, the language server relays the dialogs a program opens - <c>MsgBox</c> and <c>InputBox</c> - to the client, over
    /// <c>rdcore/session/interaction/*</c>, and the client answers them. A client that does not expect it is never asked, and the program's dialogs are
    /// the platform's own.
    /// </summary>
    public SessionInteraction SessionInteraction { get; set; } = new();

    /// <summary>
    /// If expected, the client runs programs unattended - a CI run, a script - and nobody is there to answer a dialog: the language server runs every program
    /// the client asks it to so that a dialog it opens fails the run (<c>VBR50001</c>). It wins over <see cref="SessionInteraction"/>.
    /// </summary>
    public Unattended Unattended { get; set; } = new();

    /// <summary>
    /// If supported, the language server asks the client for the indenter settings its user had in Rubberduck, over <c>rdcore/formatting/indenterSettings</c>, and
    /// asks it to take the <c>Formatting</c> settings it mapped from them as its own, over <c>rdcore/formatting/indenterSettings/apply</c>.
    /// </summary>
    public RubberduckIndenterSettings RubberduckIndenterSettings { get; set; } = new();

    /// <summary>
    /// If supported, the language server says what a refactoring asks its user before it is applied, over <c>rdcore/refactor/prepare</c>, and answers the edit
    /// it makes with the user's answers, over <c>rdcore/refactor/apply</c>: what a client that has a dialog of its own asks with. Any client is offered the refactorings as
    /// LSP code actions, which complete with their defaults.
    /// </summary>
    public Refactoring Refactoring { get; set; } = new();
}

/// <summary>
/// A capability of the RDCore platform beyond LSP: what a component of it says it provides (its assembly declares it), and what one that connects to it
/// expects it to (<c>rdcore/platform/initialize</c>). Each capability is a type of its own, named after what it makes possible.
/// </summary>
/// <param name="IsSupported">Whether the capability is expected, or provided.</param>
[JsonDerivedType(typeof(ParseFullDocument))]
[JsonDerivedType(typeof(ParseTokens))]
[JsonDerivedType(typeof(DefineSymbols))]
[JsonDerivedType(typeof(ProjectReload))]
[JsonDerivedType(typeof(CliCommand))]
[JsonDerivedType(typeof(DiagnoseDocument))]
[JsonDerivedType(typeof(SessionStatus))]
[JsonDerivedType(typeof(SessionExecute))]
[JsonDerivedType(typeof(SessionDiscard))]
[JsonDerivedType(typeof(ProgramDebugging))]
[JsonDerivedType(typeof(SessionAnalyze))]
[JsonDerivedType(typeof(SessionCompletion))]
[JsonDerivedType(typeof(SessionSignatureHelp))]
[JsonDerivedType(typeof(SessionSemanticTokens))]
[JsonDerivedType(typeof(SemanticAnalysis))]
[JsonDerivedType(typeof(SessionMemoryAccess))]
[JsonDerivedType(typeof(SessionInteraction))]
[JsonDerivedType(typeof(Unattended))]
[JsonDerivedType(typeof(Refactoring))]
[JsonDerivedType(typeof(RubberduckIndenterSettings))]
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
/// Advertises that the declaring component composes its runtime session again from the project file as it is now, over <c>rdcore/host/project/reload</c>
/// between the language server and the component that owns the runtime session.
/// </summary>
/// <remarks>
/// A class module's symbol is composed from the project, not from the definition of its members: a class the project gained since the session was composed
/// is not one until the session is composed again. A program that is running or waits keeps the session it runs in.
/// </remarks>
public record class ProjectReload(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

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
/// Advertises that the declaring component lists what can be written at a position of a supplied module - <c>rdcore/session/completion</c>, the counterpart
/// to LSP's own <c>textDocument/completion</c> for a line typed at a prompt, which is in no document.
/// </summary>
public record class SessionCompletion(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the declaring component says what the call being written at a position of a supplied module takes - <c>rdcore/session/signatureHelp</c>,
/// the counterpart to LSP's own <c>textDocument/signatureHelp</c> for a line typed at a prompt, which is in no document.
/// </summary>
public record class SessionSignatureHelp(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the declaring component tells what the tokens of code fragments a client supplies are - <c>rdcore/session/semanticTokens</c>, the counterpart
/// to LSP's own <c>textDocument/semanticTokens/full</c> for code that is in no document: the condition of a breakpoint, a line typed at a prompt.
/// </summary>
public record class SessionSemanticTokens(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the declaring component reads and writes single bytes of a live runtime session's
/// memory — <c>PEEK</c> and <c>POKE</c>, unchecked.
/// </summary>
public record class SessionMemoryAccess(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the dialogs a program opens - <c>MsgBox</c>, <c>InputBox</c> - reach the client: the language server provides it by relaying them over
/// <c>rdcore/session/interaction/*</c>, and a client that expects it answers them (the interactive shell asks at its console).
/// </summary>
/// <remarks>
/// The environment host asks the language server whatever its client is (<c>rdcore/host/interaction/*</c>); a client that did not expect this capability is never
/// asked, and the language server answers that nobody was, so that the host shows the dialog of the platform it runs on.
/// </remarks>
public record class SessionInteraction(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that nobody is there to answer the dialogs a program opens: a client that expects it runs programs unattended (a CI run, <c>rdc --unattended</c>),
/// and the language server runs each program it asks for so that a dialog - <c>MsgBox</c>, <c>InputBox</c>, a form shown, modal or not - fails the run with
/// the untrappable run-time error <c>VBR50001</c>, which no <c>On Error</c> of the program catches.
/// </summary>
/// <remarks>
/// The dialogs are the standard library's, which every program has: a client opts out of answering them, rather than into it. A client that expects both this
/// and <see cref="SessionInteraction"/> runs unattended: a run that fails loudly is the safe reading of a contradiction. A test framework that mocks the dialogs
/// intercepts them before they open, so a run that reaches one has a dialog nothing intercepted.
/// </remarks>
public record class Unattended(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the language server asks the client for the indenter settings its user had in Rubberduck - <c>rdcore/formatting/indenterSettings</c> - and asks it to
/// take the <c>Formatting</c> settings mapped from them as its own - <c>rdcore/formatting/indenterSettings/apply</c>; a client that expects it answers both.
/// </summary>
/// <remarks>
/// A client owns its configuration and the platform writes none of it: only a client that expects this is asked, and it stores what it applies as a change of its own.
/// A client on a platform that has no Rubberduck settings answers that it found none.
/// </remarks>
public record class RubberduckIndenterSettings(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

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
/// <c>rdc.exe describe-ext</c> records it in the extension's manifest (<c>ExtensionInfo</c>), which is where the language server reads it — nothing is negotiated over
/// <c>rdcore/platform/initialize</c> for this capability.
/// </remarks>
public record class DiagnoseDocument(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the declaring component contributes <c>rdc.exe</c> command-line verbs.
/// </summary>
/// <remarks>
/// Declared via <c>[assembly: ProvidesCorePlatformClientCapability&lt;CliCommand&gt;]</c>. The CLI itself
/// declares it for its native verbs; an extension declares it to have <c>rdc.exe describe-ext</c> record
/// the capability in its manifest (<c>ExtensionInfo</c>). Providers are
/// <see cref="CoreServerComponent.ClientApp"/> and <see cref="CoreServerComponent.Extension"/>. Currently
/// informational: nothing is negotiated over <c>rdcore/platform/initialize</c> for this capability yet.
/// </remarks>
public record class CliCommand(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);

/// <summary>
/// Advertises that the language server says what a refactoring asks its user (<c>rdcore/refactor/prepare</c>), and answers the edit it makes with the
/// user's answers (<c>rdcore/refactor/apply</c>): the counterpart to LSP's own <c>codeAction/resolve</c> for a client that shows a dialog of its own before a refactoring is
/// applied.
/// </summary>
/// <remarks>
/// One pair of requests serves every refactoring, whose choices are described as data. A client that does not expect it is offered the same refactorings as
/// code actions, which complete with their defaults.
/// </remarks>
/// <param name="IsSupported">Whether the capability is expected, or provided.</param>
public record class Refactoring(bool IsSupported = false) : CorePlatformClientCapability(IsSupported);
