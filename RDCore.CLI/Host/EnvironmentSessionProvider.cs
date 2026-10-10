using Microsoft.Extensions.Logging;
using RDCore.CLI.Host.Symbols;
using RDCore.Runtime.Execution;
using RDCore.External;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;

namespace RDCore.CLI.Host;

/// <summary>
/// Owns the environment host's single <see cref="IRuntimeSession"/>. The session is composed once,
/// when the language server sends the LSP <c>initialize</c> carrying the workspace root, from the
/// <c>.rdproj</c> project structure and its precompiler constants. Member symbols are added
/// afterwards, as the language server sends their descriptors over <c>rdcore/host/symbols/define</c>.
/// </summary>
/// <remarks>
/// Registered as a singleton in the outer host container so the app can compose it on
/// <c>initialize</c>, and bridged into the language-server handler container so the
/// <c>rdcore/host/symbols/define</c> handler resolves the same instance.
/// </remarks>
public interface IEnvironmentSessionProvider
{
    /// <summary>
    /// Whether <see cref="Compose"/> has run and <see cref="Session"/> is available.
    /// </summary>
    bool IsComposed { get; }

    /// The name of the project the session was composed from; an empty string before <see cref="Compose"/>
    /// has run, or when the project declares no name.
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// The number of modules the project the session was composed from declares; <c>0</c> before
    /// <see cref="Compose"/> has run.
    /// </summary>
    int ModuleCount { get; }

    /// <summary>
    /// The session's output channel, routed: each run points it at its own buffer for the duration,
    /// so the output of one run never reaches the caller of another.
    /// </summary>
    RuntimeOutputRouter Output { get; }

    /// <summary>
    /// Where the lines of a program run with streamed output are said as they are printed: the lines, and how many lines have been said in all. Whoever owns the
    /// connection to the language server sets it; a program that asks for its output to be streamed when there is nowhere to say it has it in the answers, as any other.
    /// </summary>
    Action<IReadOnlyList<string>, long>? OutputStreamed { get; set; }

    /// <summary>
    /// The composed runtime session.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The session has not been composed yet — it is composed on the LSP <c>initialize</c> handshake.
    /// </exception>
    IRuntimeSession Session { get; }

    /// <summary>
    /// The code of the composed session: the lowered procedures of every module loaded into it. Replaced, empty, each
    /// time the session is composed.
    /// </summary>
    ProgramImage Image { get; }

    /// <summary>
    /// The owner of the program that runs in the composed session. Replaced each time the session is composed.
    /// </summary>
    ProgramExecution Execution { get; }

    /// <summary>
    /// What a program reaches outside the platform - the automation servers that the objects of a referenced library are held by, and the native libraries
    /// <c>Declare</c> statements name: those of the machine this host runs on, unless it was given others.
    /// </summary>
    /// <remarks>
    /// Of the host and not of a run, because the objects a program made outlive it in the session and have to be let go of by what made them.
    /// </remarks>
    ExternalWorld Outside { get; }

    /// <summary>
    /// Composes the session from a loaded project's module structure and precompiler constants.
    /// Replaces any previously composed session.
    /// </summary>
    /// <param name="project">The <c>.rdproj</c> project structure.</param>
    /// <param name="workspaceRoot">The workspace root the project's modules are relative to.</param>
    IRuntimeSession Compose(RDCoreProject project, Uri workspaceRoot);
}

/// <inheritdoc cref="IEnvironmentSessionProvider"/>
public sealed class EnvironmentSessionProvider(
    IRuntimeEnvironmentProfile environment,
    IFileSystem fileSystem,
    ILogger<EnvironmentSessionProvider> logger,
    ILibrarySource? librarySource = null,
    ExternalWorld? outside = null) : IEnvironmentSessionProvider
{
    private IRuntimeSession? _session;

    /// <inheritdoc/>
    public ExternalWorld Outside => outside ?? MachineExternal.World;

    /// <inheritdoc/>
    public bool IsComposed => _session is not null;

    /// <inheritdoc/>
    public string ProjectName { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public int ModuleCount { get; private set; }

    /// <inheritdoc/>
    public RuntimeOutputRouter Output { get; } = new();

    /// <inheritdoc/>
    public Action<IReadOnlyList<string>, long>? OutputStreamed { get; set; }

    private readonly Lock _eventLinesLock = new();
    private readonly List<string> _eventLines = [];
    private long _eventLinesSaid;

    /// <summary>
    /// What the handlers of events printed while no program was running, in order.
    /// </summary>
    /// <remarks>
    /// The events of a server's object are raised when the server pleases, and nothing is waiting for an answer to read what their handlers print. It is said as it is printed
    /// (<see cref="OutputStreamed"/>) to whoever listens, and kept here for whoever asks.
    /// </remarks>
    public IReadOnlyList<string> EventLines
    {
        get
        {
            lock (_eventLinesLock)
            {
                return [.. _eventLines];
            }
        }
    }

    private IDisposable BeginEvent()
    {
        var previous = Output.Target;
        Output.Target = new RuntimeOutputBuffer(line =>
        {
            lock (_eventLinesLock)
            {
                _eventLines.Add(line);
            }

            OutputStreamed?.Invoke([line], Interlocked.Increment(ref _eventLinesSaid));
        });

        return new Restore(() => Output.Target = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    /// <inheritdoc/>
    public IRuntimeSession Session => _session ?? throw new InvalidOperationException(
        "The runtime session has not been composed yet; it is composed on the LSP initialize handshake.");

    /// <inheritdoc/>
    public ProgramImage Image { get; private set; } = new();

    /// <inheritdoc/>
    public ProgramExecution Execution { get; private set; } = default!;

    /// <inheritdoc/>
    public IRuntimeSession Compose(RDCoreProject project, Uri workspaceRoot)
    {
        var configuration = new ConfigurationSymbolProvider(environment, project);
        var modules = new ProjectSymbolProvider(workspaceRoot, project, fileSystem);
        // the standard library and the environment's own globals resolve in the session too, so a name
        // the language server bound to one of them binds to the same symbol here.
        var stdLib = new StdLibSymbolProvider(workspaceRoot, environment.Is64Bit);

        // and the libraries the project references, by name: the same symbols the language server bound the workspace's names to.
        var referenced = LibrarySymbolProvider.Load(librarySource ?? new DirectoryLibrarySource(fileSystem, string.Empty), project.References);
        foreach (var problem in referenced.Problems)
        {
            logger.LogWarning("📚 The library '{library}' could not be loaded ({kind}{related}); a type of it is not a type of the project.",
                problem.Name, problem.Kind, problem.Related.IsEmpty ? string.Empty : ": " + string.Join(" → ", problem.Related));
        }

        var libraries = new LibrarySymbolProvider(workspaceRoot, referenced, environment.Is64Bit);

        _session = RuntimeSessionComposer.Compose(environment, MapReferences(project.References), [configuration, stdLib, libraries, modules], Output);
        // an event that something outside the workspace raises while no program runs prints where this host says: said as it is printed, and kept.
        _session.Turn.EventScope = BeginEvent;
        Image = new ProgramImage();
        Execution = new ProgramExecution(this);
        ProjectName = project.Name;
        ModuleCount = project.Modules.Length;

        // a project that references a library can make a call to its objects as soon as it runs, and what makes the call is got ready now; one that references none
        // makes no such call, and nothing is started for it.
        if (environment.AllowAutomation && !referenced.Libraries.IsEmpty)
        {
            Outside.Automation.Prepare();
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "🧠 Runtime session composed: {modules} module(s), {references} reference(s), {constants} precompiler constant(s) (built-ins included).",
                project.Modules.Length, _session.References.Count, configuration.ProvideSymbols().Count());
        }

        return _session;
    }

    // the .rdproj declares references in precedence order (RD-VBAL §2.3.1.2); the list index is the rank.
    private static IReadOnlyList<ReferencePriorityInfo> MapReferences(RDCoreReference[] references)
        => [.. references.Select((reference, rank) => new ReferencePriorityInfo(reference.Name, rank))];
}
