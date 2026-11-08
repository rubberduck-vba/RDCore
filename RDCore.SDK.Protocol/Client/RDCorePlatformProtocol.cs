namespace RDCore.SDK.Client;

/// <summary>
/// The names of the methods of the RDCore platform's own protocol (<c>rdcore/*</c>) that a client sends or answers, which travel on the same JSON-RPC connection
/// as <strong>LSP</strong>: the handshake, those a client sends the language server (<c>rdcore/session/*</c>), and those the language server sends a client.
/// </summary>
/// <remarks>
/// The language server serves a <c>rdcore/session/*</c> request with the component that owns what it asks about - the environment host, for the runtime session and
/// the program it runs - over methods of its own that no client sends (<c>RDCoreComponentProtocol</c>, in <c>RDCore.SDK</c>).
/// </remarks>
public static class RDCorePlatformProtocol
{
    /// <summary>
    /// The platform handshake: what a client says it expects of the component it connected to, once LSP's <c>initialize</c> is answered, and is answered what that
    /// component provides.
    /// </summary>
    public const string PlatformInitialize = "rdcore/platform/initialize";

    /// <summary>
    /// Asks the language server for the state of the platform's runtime session.
    /// </summary>
    public const string SessionStatus = "rdcore/session/status";

    /// <summary>
    /// Asks the language server to run one procedure of a module the client supplies.
    /// </summary>
    public const string SessionExecute = "rdcore/session/execute";

    /// <summary>
    /// Asks the language server to take a module the client supplied out of the runtime session.
    /// </summary>
    public const string SessionDiscard = "rdcore/session/discard";

    /// <summary>
    /// Asks a client to answer a program's <c>MsgBox</c>: sent to a client that expects the <c>SessionInteraction</c> capability, while the program waits for the
    /// answer as it waits for the dialog.
    /// </summary>
    public const string SessionMessageBox = "rdcore/session/interaction/msgBox";

    /// <summary>
    /// Asks a client to answer a program's <c>InputBox</c>. See <see cref="SessionMessageBox"/>.
    /// </summary>
    public const string SessionInputBox = "rdcore/session/interaction/inputBox";

    /// <summary>Asks the language server to resume, or step, the program a client ran under a debugger.</summary>
    public const string SessionDebugResume = "rdcore/session/debug/resume";

    /// <summary>Asks the language server to stop the program that is running where it is, so that it waits.</summary>
    public const string SessionDebugPause = "rdcore/session/debug/pause";

    /// <summary>Asks the language server to move the point the program that waits goes on from.</summary>
    public const string SessionDebugGoto = "rdcore/session/debug/goto";

    /// <summary>Asks the language server which edits made while a program waits are not applied to it: what resuming it would be refused for.</summary>
    public const string SessionDebugEditStatus = "rdcore/session/debug/editStatus";

    /// <summary>Asks the language server to end the program that is running or waits.</summary>
    public const string SessionDebugTerminate = "rdcore/session/debug/terminate";

    /// <summary>Asks the language server to set the lines of a module that a program under a debugger waits at.</summary>
    public const string SessionDebugBreakpoints = "rdcore/session/debug/breakpoints";

    /// <summary>Asks the language server for the call stack of the program that waits.</summary>
    public const string SessionDebugStack = "rdcore/session/debug/stack";

    /// <summary>Asks the language server for the variables of an activation of the program that waits.</summary>
    public const string SessionDebugVariables = "rdcore/session/debug/variables";

    /// <summary>Asks the language server for the value of an expression, as text, in an activation of the program that waits.</summary>
    public const string SessionDebugEvaluate = "rdcore/session/debug/evaluate";

    /// <summary>Asks the language server to run a statement, as text, in an activation of the program that waits.</summary>
    public const string SessionDebugExecute = "rdcore/session/debug/execute";

    /// <summary>
    /// Asks the language server to analyze a module the client supplies.
    /// </summary>
    public const string SessionAnalyze = "rdcore/session/analyze";

    /// <summary>
    /// Asks the language server what can be written at a position of a module the client supplies.
    /// </summary>
    public const string SessionCompletion = "rdcore/session/completion";

    /// <summary>
    /// Asks the client for the indenter settings its user had in Rubberduck, as Rubberduck has them. A request the language server sends.
    /// </summary>
    public const string RubberduckIndenterSettings = "rdcore/formatting/indenterSettings";

    /// <summary>
    /// Asks the client to take the <c>Formatting</c> settings the language server mapped from Rubberduck's indenter settings as its own. A request the language server sends.
    /// </summary>
    public const string RubberduckIndenterSettingsApply = "rdcore/formatting/indenterSettings/apply";

    /// <summary>
    /// Asks the language server what the call being written at a position of a module the client supplies takes.
    /// </summary>
    public const string SessionSignatureHelp = "rdcore/session/signatureHelp";

    /// <summary>
    /// Asks the language server what the tokens of code fragments the client supplies are, with the legend of <c>textDocument/semanticTokens/full</c>.
    /// </summary>
    public const string SessionSemanticTokens = "rdcore/session/semanticTokens";

    /// <summary>
    /// Registers the verbs of a client's own commands with the language server, described, which completes them as they are typed.
    /// </summary>
    public const string SessionCommands = "rdcore/session/commands";

    /// <summary>
    /// Reads one byte of the runtime session's memory.
    /// </summary>
    public const string SessionPeek = "rdcore/session/memory/peek";

    /// <summary>
    /// Writes one byte of the runtime session's memory.
    /// </summary>
    public const string SessionPoke = "rdcore/session/memory/poke";

    /// <summary>
    /// Asks the language server what a refactoring of a document's range asks its user, and what it refuses or would change, before it is applied: sent by a
    /// client that expects the <c>Refactoring</c> capability.
    /// </summary>
    public const string RefactorPrepare = "rdcore/refactor/prepare";

    /// <summary>
    /// Asks the language server for the edit a refactoring of a document's range makes with its user's answers, which the client applies.
    /// </summary>
    public const string RefactorApply = "rdcore/refactor/apply";
}
