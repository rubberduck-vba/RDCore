namespace RDCore.SDK.Client;

/// <summary>
/// The names of the methods of the RDCore platform's own protocol (<c>rdcore/*</c>) that the components of the platform send one another, and no client sends:
/// the language server's to the environment host (<c>rdcore/host/*</c>), to the parser (<c>rdcore/parser/*</c>) and to a diagnostics provider
/// (<c>rdcore/diagnostics/*</c>), and the environment host's to the language server.
/// </summary>
/// <remarks>
/// The methods a client sends or answers are <see cref="RDCorePlatformProtocol"/>'s; the language server serves most of them with one of these.
/// </remarks>
public static class RDCoreComponentProtocol
{
    /// <summary>
    /// Asks the environment host for the state of the runtime session it owns. The language-server side of <see cref="RDCorePlatformProtocol.SessionStatus"/>.
    /// </summary>
    public const string HostSessionStatus = "rdcore/host/session/status";

    /// <summary>
    /// Asks the environment host to lower and run a parsed module in its runtime session. The language-server side of
    /// <see cref="RDCorePlatformProtocol.SessionExecute"/>.
    /// </summary>
    public const string HostExecute = "rdcore/host/execute";

    /// <summary>
    /// Asks the environment host to take a module out of its runtime session. The language-server side of <see cref="RDCorePlatformProtocol.SessionDiscard"/>.
    /// </summary>
    public const string HostDiscard = "rdcore/host/discard";

    /// <summary>
    /// Asks the environment host to compose its runtime session again from the project file as it is now: what the language server asks once a client said the
    /// <c>.rdproj</c> changed (<c>workspace/didChangeWatchedFiles</c>), before it defines the workspace again.
    /// </summary>
    public const string HostProjectReload = "rdcore/host/project/reload";

    /// <summary>
    /// Tells the language server what a program printed, as it prints: a notification from the environment host, sent for a program that was run with its
    /// output streamed (<c>HostExecuteParams.StreamOutput</c>).
    /// </summary>
    public const string HostOutput = "rdcore/host/output";

    /// <summary>
    /// Asks the language server to have the client answer a program's <c>MsgBox</c>: a request from the environment host, which waits for the answer as the
    /// program waits for the dialog. The language server asks the client with <see cref="RDCorePlatformProtocol.SessionMessageBox"/>.
    /// </summary>
    public const string HostMessageBox = "rdcore/host/interaction/msgBox";

    /// <summary>
    /// Asks the language server to have the client answer a program's <c>InputBox</c>. See <see cref="HostMessageBox"/>.
    /// </summary>
    public const string HostInputBox = "rdcore/host/interaction/inputBox";

    /// <summary>
    /// Asks the environment host to resume a program that waits at a stop, or to take one step of it. The language-server side of
    /// <see cref="RDCorePlatformProtocol.SessionDebugResume"/>.
    /// </summary>
    public const string HostDebugResume = "rdcore/host/debug/resume";

    /// <summary>
    /// Asks the environment host to stop a program that is running where it is, so that it waits. See <see cref="RDCorePlatformProtocol.SessionDebugPause"/>.
    /// </summary>
    public const string HostDebugPause = "rdcore/host/debug/pause";

    /// <summary>
    /// Asks the environment host to move the point a program that waits goes on from. See <see cref="RDCorePlatformProtocol.SessionDebugGoto"/>.
    /// </summary>
    public const string HostDebugGoto = "rdcore/host/debug/goto";

    /// <summary>
    /// Asks the environment host which edits made while a program waits are not applied to it: what resuming it would be refused for. See
    /// <see cref="RDCorePlatformProtocol.SessionDebugEditStatus"/>.
    /// </summary>
    public const string HostDebugEditStatus = "rdcore/host/debug/editStatus";

    /// <summary>
    /// Asks the environment host to set the lines of a module that a program under a debugger waits at. See <see cref="RDCorePlatformProtocol.SessionDebugBreakpoints"/>.
    /// </summary>
    public const string HostDebugBreakpoints = "rdcore/host/debug/breakpoints";

    /// <summary>Asks the environment host for the call stack of the program that waits. See <see cref="RDCorePlatformProtocol.SessionDebugStack"/>.</summary>
    public const string HostDebugStack = "rdcore/host/debug/stack";

    /// <summary>
    /// Asks the environment host for the variables of an activation of the program that waits. See <see cref="RDCorePlatformProtocol.SessionDebugVariables"/>.
    /// </summary>
    public const string HostDebugVariables = "rdcore/host/debug/variables";

    /// <summary>
    /// Asks the environment host for the value of an expression in an activation of the program that waits. See <see cref="RDCorePlatformProtocol.SessionDebugEvaluate"/>.
    /// </summary>
    public const string HostDebugEvaluate = "rdcore/host/debug/evaluate";

    /// <summary>
    /// Asks the environment host to run a statement in an activation of the program that waits. See <see cref="RDCorePlatformProtocol.SessionDebugExecute"/>.
    /// </summary>
    public const string HostDebugExecute = "rdcore/host/debug/execute";

    /// <summary>Asks the environment host to say which run-time errors a program under a debugger waits at, where they are raised.</summary>
    public const string HostDebugErrorBreak = "rdcore/host/debug/errorBreak";

    /// <summary>
    /// Asks the environment host to end a program that is running or waits. See <see cref="RDCorePlatformProtocol.SessionDebugTerminate"/>.
    /// </summary>
    public const string HostDebugTerminate = "rdcore/host/debug/terminate";

    /// <summary>
    /// Reads one byte of the environment host's runtime session memory. The language-server side of <see cref="RDCorePlatformProtocol.SessionPeek"/>.
    /// </summary>
    public const string HostPeek = "rdcore/host/memory/peek";

    /// <summary>
    /// Writes one byte of the environment host's runtime session memory. The language-server side of <see cref="RDCorePlatformProtocol.SessionPoke"/>.
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
    /// Asks the environment host for the semantic model of the code it holds: what the language server hands a diagnostics provider.
    /// </summary>
    public const string HostSemantics = "rdcore/host/semantics";

    /// <summary>
    /// Hands a diagnostics-provider extension a parsed document and asks for the diagnostics it finds.
    /// </summary>
    public const string DiagnoseDocument = "rdcore/diagnostics/document";
}
