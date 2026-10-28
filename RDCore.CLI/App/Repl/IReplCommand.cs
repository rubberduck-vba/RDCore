using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// What the shell does next once a command has run.
/// </summary>
public enum ReplCommandResult
{
    /// <summary>Print the ready banner and prompt again.</summary>
    Continue,

    /// <summary>End the session and exit the process.</summary>
    Exit,
}

/// <summary>
/// The interactive shell's console, as a command sees it.
/// </summary>
/// <remarks>
/// Plain lines are the shell's own voice — program output, listings, the ready banner — and are
/// written unstyled so they take the shell frame's colours. A <see cref="WriteMessage"/> is the
/// platform speaking: an error, a warning, something the theme should mark.
/// </remarks>
public interface IReplConsole
{
    /// <summary>
    /// Clears the console.
    /// </summary>
    void Clear();

    /// <summary>Writes one plain line in the shell's own colours.</summary>
    /// <param name="text">The line; empty writes a blank line.</param>
    void WriteLine(string text = "");

    /// <summary>Writes one line made of runs of text, each in the style of the theme it is of.</summary>
    /// <param name="runs">The runs, in order; the line is what they say.</param>
    void WriteLine(IReadOnlyList<ReplTextRun> runs);

    /// <summary>Writes one line of a listing: a margin, and the line itself, which a breakpoint or the statement a stopped program waits before marks as a whole.</summary>
    /// <param name="margin">The lanes of the margin - the mark of a breakpoint, at the edge, then the mark of a block that folds, next to the line - which are outside the marking.</param>
    /// <param name="runs">The runs of the line, in order.</param>
    /// <param name="style">How the whole line is marked, from its first character to the end of the console's width.</param>
    void WriteListingLine(IReadOnlyList<ReplTextRun> margin, IReadOnlyList<ReplTextRun> runs, ReplLineStyle style);

    /// <summary>How many cells wide a line of the console is; 0 when it cannot be told.</summary>
    int Width { get; }

    /// <summary>Whether the console draws Unicode symbols, emoji among them; a console that does not is written in ASCII.</summary>
    bool DrawsUnicode { get; }

    /// <summary>Writes a themed platform message.</summary>
    /// <param name="kind">The message kind, which selects the icon and accent style.</param>
    /// <param name="message">The message body.</param>
    /// <param name="verbose">An optional second, dimmer line of detail.</param>
    void WriteMessage(MessageKind kind, string message, string? verbose = null);

    /// <summary>
    /// Writes a finding of an analysis on two lines: its glyph, where it is, its code and the page that explains it, in the title style of its kind; and what it
    /// says under it, in the verbose style.
    /// </summary>
    /// <param name="kind">The message kind of the finding's severity, which selects the glyph and the styles.</param>
    /// <param name="title">Where the finding is, and its code.</param>
    /// <param name="helpUrl">The page that explains the finding, written after the title; a link where the console can draw one. <see langword="null"/> for none.</param>
    /// <param name="description">What the finding says.</param>
    void WriteFinding(MessageKind kind, string title, Uri? helpUrl, string description);
}

/// <summary>
/// The platform, as the interactive shell sees it: one language server, reached over LSP, and
/// whatever it told the shell it can provide.
/// </summary>
/// <remarks>
/// The shell is an LSP client and nothing more. It does not know that a parser or an environment
/// host exist, let alone address one — every capability it uses is one the language server answered
/// for in the platform handshake, and a command that needs one it did not get says so instead of
/// failing obscurely.
/// </remarks>
public interface IReplPlatformClient
{
    /// <summary>
    /// Whether the language server told this client, in the platform handshake, that it provides
    /// <typeparamref name="TCapability"/>.
    /// </summary>
    /// <typeparam name="TCapability">The capability to test for.</typeparam>
    bool Provides<TCapability>() where TCapability : CorePlatformClientCapability;

    /// <summary>
    /// Tells the language server that a document is open (<c>textDocument/didOpen</c>).
    /// </summary>
    /// <param name="document">The document's URI.</param>
    /// <param name="text">The complete text of the document.</param>
    /// <param name="version">The version of that text, which the client numbers.</param>
    /// <param name="token">A token that cancels the notification.</param>
    Task OpenDocumentAsync(Uri document, string text, int version, CancellationToken token);

    /// <summary>
    /// Tells the language server that an open document has a new text (<c>textDocument/didChange</c>), as the complete text.
    /// </summary>
    /// <param name="document">The document's URI.</param>
    /// <param name="version">The version of the new text; it is greater than the one before it.</param>
    /// <param name="text">The complete new text.</param>
    /// <param name="token">A token that cancels the notification.</param>
    Task ChangeDocumentAsync(Uri document, int version, string text, CancellationToken token);

    /// <summary>
    /// Tells the language server that a document is about to be saved (<c>textDocument/willSave</c>), and asks it for the edits it would have made first
    /// (<c>textDocument/willSaveWaitUntil</c>).
    /// </summary>
    /// <param name="document">The document's URI.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The edits to apply before the text is written; none, when the server has none.</returns>
    Task<IReadOnlyList<TextEdit>> WillSaveDocumentAsync(Uri document, CancellationToken token);

    /// <summary>
    /// Asks the language server what the tokens of a document are (<c>textDocument/semanticTokens/full</c>), which is what a listing is highlighted by.
    /// </summary>
    /// <param name="document">The document's URI; the language server answers for the text it has of it.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The tokens in the order they are in the document; none when the language server has none to give.</returns>
    Task<IReadOnlyList<ReplSemanticToken>> GetSemanticTokensAsync(Uri document, CancellationToken token);

    /// <summary>
    /// Asks the language server what folds in a document (<c>textDocument/foldingRange</c>), and keeps the blocks: a listing folds a block, not a procedure.
    /// </summary>
    /// <param name="document">The document's URI; the language server answers for the text it has of it.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The blocks, by the zero-based lines of the document they start and end at; none when the language server has none to give.</returns>
    Task<IReadOnlyList<ReplFold>> GetBlockFoldsAsync(Uri document, CancellationToken token);

    /// <summary>
    /// Asks the language server for the hints of a document (<c>textDocument/inlayHint</c>): the name of the parameter each argument is given to, and the type a declaration has because it states none.
    /// </summary>
    /// <param name="document">The document's URI; the language server answers for the text it has of it.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The hints, by the zero-based line and character of the document they are shown before; none when the language server has none to give.</returns>
    Task<IReadOnlyList<ReplInlayHint>> GetInlayHintsAsync(Uri document, CancellationToken token);

    /// <summary>
    /// Asks the language server what it finds in a document (<c>textDocument/diagnostic</c>): the diagnostics as an editor gets them, with what fixes them in
    /// their <c>data</c>.
    /// </summary>
    /// <param name="document">The document's URI.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The diagnostics; none when the server has none to give.</returns>
    Task<IReadOnlyList<Diagnostic>> PullDiagnosticsAsync(Uri document, CancellationToken token);

    /// <summary>
    /// Asks the language server what it finds in a document as <see cref="PullDiagnosticsAsync"/> does, naming the report the shell has: a program that did not
    /// change since is answered with an <c>unchanged</c> report, which costs no findings.
    /// </summary>
    /// <param name="document">The document's URI.</param>
    /// <param name="previousResultId">The identifier of the report the shell has; <see langword="null"/> for none.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The report; <see langword="null"/> when the server has none to give.</returns>
    Task<ReplDiagnosticReport?> PullDiagnosticReportAsync(Uri document, string? previousResultId, CancellationToken token);

    /// <summary>
    /// Asks the language server what can be done in a range of a document (<c>textDocument/codeAction</c>), about the diagnostics of the range and otherwise.
    /// </summary>
    /// <param name="document">The document's URI.</param>
    /// <param name="range">The range, in the text of the document.</param>
    /// <param name="diagnostics">The diagnostics of the range, as the server gave them.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The actions, in the order the server offers them.</returns>
    Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(Uri document, OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range, IReadOnlyList<Diagnostic> diagnostics,
        CancellationToken token);

    /// <summary>
    /// Asks the language server what a rename at a position of a document would rename (<c>textDocument/prepareRename</c>).
    /// </summary>
    /// <param name="document">The document's URI.</param>
    /// <param name="position">The position of the name.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The name as it is declared, or why it is not renamed.</returns>
    Task<ReplRenameAnswer<PlaceholderRange>> PrepareRenameAsync(Uri document, Position position, CancellationToken token);

    /// <summary>
    /// Asks the language server for the edits that rename the symbol named at a position of a document (<c>textDocument/rename</c>).
    /// </summary>
    /// <param name="document">The document's URI.</param>
    /// <param name="position">The position of the name.</param>
    /// <param name="newName">The new name.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The edits, or why there are none.</returns>
    Task<ReplRenameAnswer<WorkspaceEdit>> RenameAsync(Uri document, Position position, string newName, CancellationToken token);

    /// <summary>
    /// Tells the language server that a document was saved (<c>textDocument/didSave</c>).
    /// </summary>
    /// <param name="document">The document's URI.</param>
    /// <param name="text">The text that was written.</param>
    /// <param name="token">A token that cancels the notification.</param>
    Task SaveDocumentAsync(Uri document, string text, CancellationToken token);

    /// <summary>
    /// Tells the language server that a document is closed (<c>textDocument/didClose</c>).
    /// </summary>
    /// <param name="document">The document's URI.</param>
    /// <param name="token">A token that cancels the notification.</param>
    Task CloseDocumentAsync(Uri document, CancellationToken token);

    /// <summary>
    /// Asks the language server for the state of the platform's runtime session.
    /// </summary>
    /// <param name="waitMilliseconds">How long the language server may wait for the session to come up; <c>0</c> answers immediately.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<SessionStatusResult> GetSessionStatusAsync(int waitMilliseconds, CancellationToken token);

    /// <summary>
    /// Asks the language server to run one procedure of a module.
    /// </summary>
    /// <param name="source">The complete module source.</param>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="entryPoint">The parameterless procedure to invoke.</param>
    /// <param name="token">
    /// Cancelling this cancels the run itself, all the way into the interpreter loop — it is what a
    /// break at the keyboard turns into.
    /// </param>
    Task<ExecuteSessionResult> ExecuteAsync(string source, string moduleName, string entryPoint, CancellationToken token);

    /// <summary>
    /// Asks the language server to take a module out of the runtime session: what it declared and what it held. Defining a module again never
    /// does: the variables a program made outlive it.
    /// </summary>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<DiscardSessionResult> DiscardAsync(string moduleName, CancellationToken token);

    /// <summary>
    /// Asks the language server to take a module out of the runtime session like <see cref="DiscardAsync(string, CancellationToken)"/>, and to end the program that
    /// is running or waits first, wiping the session: what clearing the program means to a program that was stopped.
    /// </summary>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="endProgram">Whether the program that is running or waits is ended first.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<DiscardSessionResult> DiscardAsync(string moduleName, bool endProgram, CancellationToken token);

    /// <summary>
    /// Asks the language server to run one procedure of a module like <see cref="ExecuteAsync(string, string, string, CancellationToken)"/>, under a debugger when
    /// <paramref name="debug"/> is set: a <c>Stop</c> or a breakpoint then answers <see cref="ExecutionOutcome.Suspended"/> and the program waits.
    /// </summary>
    /// <param name="source">The complete module source.</param>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="entryPoint">The parameterless procedure to invoke.</param>
    /// <param name="debug">Whether the program runs under a debugger.</param>
    /// <param name="immediate">Whether the entry point is a statement typed at a prompt, which is run alongside a program that waits and leaves it waiting.</param>
    /// <param name="token">A token that cancels the request. For a program under a debugger a break is <see cref="PauseAsync"/>, not this.</param>
    Task<ExecuteSessionResult> ExecuteAsync(string source, string moduleName, string entryPoint, bool debug, bool immediate, CancellationToken token);

    /// <summary>
    /// Asks the language server to go on with the program that waits, to the next place it waits or to its end, or for one step.
    /// </summary>
    /// <param name="step">How far, or <see langword="null"/> for as far as it goes.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<ExecuteSessionResult> ResumeAsync(StepKind? step, CancellationToken token);

    /// <summary>
    /// Asks the language server to stop the program that is running, so that it waits where it is. The request that runs the program answers where.
    /// </summary>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugAck> PauseAsync(CancellationToken token);

    /// <summary>
    /// Asks the language server to move the point the program that waits goes on from, to a statement label or line number of its procedure.
    /// </summary>
    /// <param name="label">The label or line number.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugGotoResult> GotoAsync(string label, CancellationToken token);

    /// <summary>
    /// Asks the language server to set the lines of a module that a program under a debugger waits at.
    /// </summary>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="breakpoints">The breakpoints to set.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugBreakpointsResult> SetBreakpointsAsync(string moduleName, IReadOnlyList<SessionBreakpointLine> breakpoints, CancellationToken token);

    /// <summary>
    /// Asks the language server for the activations of the program that waits, innermost first.
    /// </summary>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugStackResult> GetStackAsync(CancellationToken token);

    /// <summary>
    /// Asks the language server for the variables of an activation of the program that waits, or the parts of one of them.
    /// </summary>
    /// <param name="frameId">The activation, by its place on the stack.</param>
    /// <param name="scope">Which of its variables.</param>
    /// <param name="reference">The reference of a variable that has parts, or <c>0</c>.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugVariablesResult> GetVariablesAsync(int frameId, HostVariableScope scope, int reference, CancellationToken token);

    /// <summary>
    /// Asks the language server to run a statement in an activation of the program that waits, as if it were written in the procedure that activation is of.
    /// </summary>
    /// <param name="frameId">The activation, by its place on the stack.</param>
    /// <param name="statement">The statement, as it was typed; <c>?</c> prints.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugEvaluateResult> ExecuteInFrameAsync(int frameId, string statement, CancellationToken token);

    /// <summary>
    /// Asks the language server to analyze a module and report what its diagnostics providers found.
    /// </summary>
    /// <param name="source">The complete module source.</param>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<AnalyzeSessionResult> AnalyzeAsync(string source, string moduleName, CancellationToken token);

    /// <summary>
    /// Asks the language server what can be written at a position of a module: the completion list of the line at the prompt.
    /// </summary>
    /// <param name="source">The complete module source.</param>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="line">The zero-based line of the cursor in <paramref name="source"/>.</param>
    /// <param name="character">The zero-based character of the cursor on <paramref name="line"/>.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <param name="triggerCharacter">The character typed that had the shell ask, when one did (<see cref="SessionCompletionParams.TriggerCharacter"/>).</param>
    Task<CompletionList> CompleteAsync(string source, string moduleName, int line, int character, CancellationToken token, string? triggerCharacter = null);

    /// <summary>
    /// Asks the language server what the call being written at a position of a module takes: the signature help of the line at the prompt.
    /// </summary>
    /// <param name="source">The complete module source.</param>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="line">The zero-based line of the cursor in <paramref name="source"/>.</param>
    /// <param name="character">The zero-based character of the cursor on <paramref name="line"/>.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The signature help; <see langword="null"/> where no call is being written.</returns>
    Task<SignatureHelp?> SignatureHelpAsync(string source, string moduleName, int line, int character, CancellationToken token);

    /// <summary>
    /// Asks the language server for the semantic tokens of code fragments - the conditions of breakpoints, say - with the names in them told apart by what the
    /// module declares (<c>rdcore/session/semanticTokens</c>).
    /// </summary>
    /// <param name="source">The complete module source.</param>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="fragments">The fragments of code, each as it is written.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The tokens of each fragment, in the order they were asked, positioned in the fragment.</returns>
    Task<IReadOnlyList<IReadOnlyList<ReplSemanticToken>>> GetFragmentTokensAsync(string source, string moduleName, IReadOnlyList<string> fragments, CancellationToken token);

    /// <summary>
    /// Says to the language server that the live settings changed (<c>workspace/didChangeConfiguration</c>): it asks the shell for them, and passes them on.
    /// </summary>
    /// <param name="token">A token that cancels the notification.</param>
    Task NotifyLiveSettingsChangedAsync(CancellationToken token);

    /// <summary>
    /// Registers the verbs of the shell's commands with the language server, described: what it completes their arguments by.
    /// </summary>
    /// <param name="verbs">The verbs, described.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<SessionCommandsResult> RegisterCommandsAsync(IReadOnlyList<VerbDescriptor> verbs, CancellationToken token);

    /// <summary>
    /// Asks the language server what can be written for the arguments of a command of the shell, as its registered verb describes them.
    /// </summary>
    /// <param name="source">The complete source of the module the command acts on.</param>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="command">The verb, and what is typed after it.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<CompletionList> CompleteCommandAsync(string source, string moduleName, SessionCommandCompletion command, CancellationToken token);

    /// <summary>
    /// Reads the byte at an address in the runtime session's memory.
    /// </summary>
    /// <param name="address">The address to read.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<PeekSessionResult> PeekAsync(int address, CancellationToken token);

    /// <summary>
    /// Writes a byte at an address in the runtime session's memory, unchecked.
    /// </summary>
    /// <param name="address">The address to write.</param>
    /// <param name="value">The byte to write there.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<PokeSessionResult> PokeAsync(int address, byte value, CancellationToken token);
}

/// <summary>
/// What the language server answers a rename request with: the answer, or why there is none, as the server said it.
/// </summary>
/// <param name="Value">The answer.</param>
/// <param name="Refusal">Why there is none: the message of the error the server answered with.</param>
public sealed record class ReplRenameAnswer<T>(T? Value, string? Refusal) where T : class;

/// <summary>
/// Everything a shell command acts on. Deliberately a growable type rather than a parameter list:
/// a later command needing a new fact gets it added here, without every command's signature moving.
/// </summary>
/// <param name="Program">The program buffer the numbered lines are kept in.</param>
/// <param name="Console">The shell's console.</param>
/// <param name="Platform">The language server this shell is a client of.</param>
/// <param name="Document">The program as a document of the language server, once it is a file that was loaded or saved.</param>
/// <param name="Commands">
/// Every command the shell offers. Carried here rather than injected into the one command that needs
/// it — <c>HELP</c> is itself in the list, so asking the container for the list while building it is
/// a cycle.
/// </param>
public sealed record class ReplCommandContext(
    ReplProgram Program,
    IReplConsole Console,
    IReplPlatformClient Platform,
    ReplDocument Document,
    IReadOnlyList<IReplCommand> Commands)
{
    /// <summary>
    /// What the shell knows of the program under a debugger: whether it is running or waits, where, and the lines that have breakpoints.
    /// </summary>
    public ReplDebugger Debugger { get; init; } = new();

    /// <summary>
    /// The blocks of the program that are folded in a listing (<c>FOLD</c>).
    /// </summary>
    public ReplFolds Folds { get; init; } = new();

    /// <summary>
    /// Whether a listing shows the hints of the language server (<c>INLAYS</c>).
    /// </summary>
    public ReplInlays Inlays { get; init; } = new();

    /// <summary>
    /// What the language server found in the program the last time the shell asked, which a listing marks the lines with a fix by.
    /// </summary>
    public ReplDiagnostics Diagnostics { get; init; } = new();
}

/// <summary>
/// A command of the interactive shell — <c>LIST</c>, <c>RUN</c>, <c>EXIT</c> — matched before the
/// line is treated as VBA.
/// </summary>
/// <remarks>
/// The sibling of <c>ICliCommand</c>, which is explicitly command-mode only: no workspace, no LSP
/// connection, one shot, an exit code. A shell command is the opposite on every count — it runs
/// inside a live, connected session and acts on it — so it shares the shape and nothing else.
/// </remarks>
public interface IReplCommand
{
    /// <summary>The command's primary name, as <c>HELP</c> lists it. Matched case-insensitively.</summary>
    string Name { get; }

    /// <summary>Alternative spellings that resolve to this command.</summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>A one-line description, for <c>HELP</c>.</summary>
    string Summary { get; }

    /// <summary>
    /// Runs the command.
    /// </summary>
    /// <param name="context">The live session the command acts on.</param>
    /// <param name="arguments">Everything the user typed after the verb, trimmed.</param>
    /// <param name="token">A token that cancels the command — what <kbd>Ctrl</kbd>+<kbd>Break</kbd> trips.</param>
    Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token);
}

/// <summary>
/// A command of the shell that describes its arguments (<see cref="VerbDescriptor"/>): what the language server completes them by, as they are typed.
/// </summary>
/// <remarks>
/// The arguments of a command are no VBA - but for the ones that are an expression of the program, which the descriptor says - and a command that describes none is
/// given no list after its verb at all.
/// </remarks>
public interface IReplCommandDescriptor
{
    /// <summary>The verb and the forms of its arguments, as they are now: a choice can be of what the shell has at the time (its themes).</summary>
    VerbDescriptor Descriptor { get; }
}
