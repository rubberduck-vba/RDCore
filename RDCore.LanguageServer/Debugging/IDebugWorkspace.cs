using RDCore.SDK.Platform.Protocol;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// The program a debug adapter debugs: the workspace it is made of, brought up on the platform, and the files it is made of.
/// </summary>
/// <remarks>
/// A debugger client speaks of files and the lines in them; the platform speaks of modules and the lines in them. This is where the two meet.
/// </remarks>
internal interface IDebugWorkspace
{
    /// <summary>
    /// Brings the platform up and has the workspace loaded, parsed, and defined in the environment host, so that a module can be run and breakpoints can be set in it.
    /// </summary>
    /// <param name="token">A token that cancels the request.</param>
    /// <exception cref="InvalidOperationException">The workspace could not be loaded, or the platform could not be brought up.</exception>
    Task OpenAsync(CancellationToken token);

    /// <summary>
    /// The modules a file is the source of: its own, and the module of each of its blocks (<strong>RD-VBAL §5.1</strong>).
    /// </summary>
    /// <param name="path">The path of the file, as the client says it.</param>
    /// <param name="file">The modules of the file, and the lines each is written on.</param>
    /// <returns>Whether the file is part of the workspace.</returns>
    bool TryGetFile(string path, out DebugFile file);

    /// <summary>
    /// The file a module is the source of: a block's module is in the file that holds the block.
    /// </summary>
    /// <param name="moduleName">The programmatic name of the module.</param>
    /// <param name="path">The path of the file.</param>
    /// <returns>Whether the module is part of the workspace.</returns>
    bool TryGetPath(string moduleName, out string path);

    /// <summary>
    /// The modules of the program that declare a procedure it can start with (<see cref="RDCore.SDK.Workspace.ProgramEntryPoint"/>), by programmatic name and in
    /// order of name, as the environment host defined them: none before the workspace is open, and none of another project's.
    /// </summary>
    /// <param name="entryPoint">The name of the procedure.</param>
    /// <param name="token">Cancels the request.</param>
    Task<IReadOnlyList<string>> ModulesDeclaringAsync(string entryPoint, CancellationToken token);

    /// <summary>
    /// Runs a procedure of a module of the workspace under a debugger.
    /// </summary>
    /// <param name="moduleName">The programmatic name of the module.</param>
    /// <param name="entryPoint">The name of the parameterless procedure to run.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>Where the program waits, or how it ended.</returns>
    Task<ExecuteResult> StartAsync(string moduleName, string entryPoint, CancellationToken token);

    /// <summary>
    /// Runs a statement typed in the debug console while nothing waits, under a debugger, as a statement typed at a prompt runs: in a module of its own, which
    /// sees the workspace's public names. <c>?</c> at the start of it is <c>Debug.Print</c>.
    /// </summary>
    /// <param name="text">What was typed: one statement.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>Where the statement waits, or how it ended; a syntax error when it is not one statement.</returns>
    Task<ExecuteResult> RunStatementAsync(string text, CancellationToken token);

    /// <summary>
    /// Reads the files of the workspace's modules again, and has the environment host load each one whose text is not what it loaded: the edits saved while the
    /// program waits, which it is checked against before it goes on.
    /// </summary>
    /// <remarks>
    /// A debug adapter has no editor telling it of edits: what is saved is what it is told, when the program is asked to go on. A file whose text is what was
    /// loaded is nothing changed, and nothing is loaded; what another request is loading still is waited for.
    /// </remarks>
    /// <param name="token">A token that cancels the reading.</param>
    /// <returns>
    /// Why the program can't go on with what is saved, in the user's words - a file that can't be read, a module the host could not load (which the next request
    /// loads again), a module named otherwise by its source - or <see langword="null"/> when it can, and the edits are for the host to check.
    /// </returns>
    Task<string?> ReloadChangedAsync(CancellationToken token);

    /// <summary>
    /// Lets go of the platform: the components it was brought up with are asked to shut down.
    /// </summary>
    Task CloseAsync();

    /// <summary>
    /// Completes when a component the debugging cannot go on without is lost for good, with its name.
    /// </summary>
    Task<string> Lost { get; }
}

// the modules a file of the workspace is the source of: its own, and each block's, written from its header line to its end line (RD-VBAL §5.1). A line is in
// the block whose lines hold it, and in the file's own module otherwise. A file of one module has no blocks.
internal sealed record class DebugFile(string Module, IReadOnlyList<(string Name, int FirstLine, int LastLine)> Blocks)
{
    public static DebugFile Of(string module) => new(module, []);

    // the file's own module first, then each block's, in the order they are written.
    public IEnumerable<string> Modules => [Module, .. Blocks.Select(block => block.Name)];

    // the module a zero-based line of the file is in.
    public string ModuleAt(int line) => Blocks.FirstOrDefault(block => block.FirstLine <= line && line <= block.LastLine).Name ?? Module;
}
