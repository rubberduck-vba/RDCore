using Microsoft.Extensions.Logging;
using RDCore.Runtime.LanguageCore.StdLib;
using RDCore.Runtime.Support.Host.Symbols;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.External.Application;
using RDCore.External;
using RDCore.External.Settings;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.Support.Host;

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

    /// <summary>
    /// The name of the project the session was composed from; an empty string before <see cref="Compose"/>
    /// has run, or when the project declares no name.
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// The root the session was composed under, which its modules are addressed under; <see langword="null"/> before <see cref="Compose"/> has run.
    /// </summary>
    Uri? WorkspaceRoot { get; }

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
    /// Where the dialogs a program opens are asked to be answered: the language server, which asks its client. Whoever owns the connection to the language server
    /// sets it; while it is <see langword="null"/>, a program's dialogs are those of the platform the host runs on.
    /// </summary>
    IHostInteractionChannel? Interaction { get; set; }

    /// <summary>
    /// Where a handler of an event of the session that stops while no program runs is told, as the request that runs a program answers where it waits: whoever
    /// owns the connection to the language server sets it, and it answers whether somebody takes the stop. While it is <see langword="null"/>, such a handler is
    /// stopped by a <c>Stop</c> and a breakpoint, as a program that is not under a debugger is.
    /// </summary>
    /// <remarks>
    /// Called while the handler waits for the answer; once it is taken, the handler is the program that <see cref="Execution"/> resumes, steps and ends.
    /// </remarks>
    Func<ExecuteResult, Task<bool>>? SessionStopped { get; set; }

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
    /// What a program reaches outside the platform - the automation servers that the objects of a referenced library are held by, the native libraries
    /// <c>Declare</c> statements name, the dialogs, the desktop and the settings: those of the machine this host runs on, unless it was given others.
    /// </summary>
    /// <remarks>
    /// Of the host and not of a run, because the objects a program made outlive it in the session and have to be let go of by what made them. Once a session is
    /// composed, it is the session's: its settings are those of the workspace it was composed from, its dialogs are answered by the client when the client shows
    /// dialogs, and their title is the name of its project.
    /// </remarks>
    ExternalWorld Outside { get; }

    /// <summary>
    /// Composes the session from a loaded project's module structure and precompiler constants.
    /// Replaces any previously composed session.
    /// </summary>
    /// <remarks>
    /// The current directory of the session is then the workspace root (RD-VBAL §6.1.2.5), at start-up and after a reload alike; a root that is the shell's
    /// scratch workspace (<see cref="ScratchWorkspace.Is"/>), that is not a full path on this platform, or that is not there, leaves it where it was.
    /// The session it replaces first gives the process back the directory that session was composed in.
    /// </remarks>
    /// <param name="project">The <c>.rdproj</c> project structure.</param>
    /// <param name="workspaceRoot">The workspace root the project's modules are relative to.</param>
    IRuntimeSession Compose(RDCoreProject project, Uri workspaceRoot);

    /// <summary>
    /// Composes the session as <see cref="Compose(RDCoreProject, Uri)"/> does, in a language the project is written in from then on: its libraries, the
    /// implicit references of the language among them, are that language's. Every session composed after it is of that language.
    /// </summary>
    /// <param name="project">The <c>.rdproj</c> project structure.</param>
    /// <param name="workspaceRoot">The workspace root the project's modules are relative to.</param>
    /// <param name="language">The language the project is written in.</param>
    IRuntimeSession Compose(RDCoreProject project, Uri workspaceRoot, SupportedLanguage language);

    /// <summary>
    /// The module symbols a project lists, each a standard module or a class as <see cref="Compose"/> composes it, without composing anything: what a project
    /// gained while a program is in progress is.
    /// </summary>
    /// <param name="project">The <c>.rdproj</c> project structure.</param>
    /// <param name="workspaceRoot">The workspace root the project's modules are relative to.</param>
    IReadOnlyList<VBModuleSymbol> ModulesOf(RDCoreProject project, Uri workspaceRoot);
}

/// <inheritdoc cref="IEnvironmentSessionProvider"/>
public sealed class EnvironmentSessionProvider(
    IRuntimeEnvironmentProfile environment,
    IFileSystem fileSystem,
    ILogger<EnvironmentSessionProvider> logger,
    ILibrarySource? librarySource = null,
    ExternalWorld? outside = null) : IEnvironmentSessionProvider, IDisposable
{
    private IRuntimeSession? _session;
    private ExternalWorld? _world;

    // the environment the sessions are composed in: the host's, in the language the project was last composed in.
    private IRuntimeEnvironmentProfile _environment = environment;

    // the directory the process was in when the session was composed, given back when the session is replaced or the provider disposed.
    private string? _directoryBefore;

    /// <inheritdoc/>
    public ExternalWorld Outside => _world ?? outside ?? MachineExternal.World;

    /// <inheritdoc/>
    public IHostInteractionChannel? Interaction { get; set; }

    /// <inheritdoc/>
    public bool IsComposed => _session is not null;

    /// <inheritdoc/>
    public string ProjectName { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public Uri? WorkspaceRoot { get; private set; }

    /// <inheritdoc/>
    public int ModuleCount { get; private set; }

    /// <inheritdoc/>
    public RuntimeOutputRouter Output { get; } = new();

    /// <inheritdoc/>
    public Action<IReadOnlyList<string>, long>? OutputStreamed { get; set; }

    /// <inheritdoc/>
    public Func<ExecuteResult, Task<bool>>? SessionStopped
    {
        get => _sessionStopped;
        set
        {
            _sessionStopped = value;
            if (_session is not null)
            {
                Execution.SessionStopsChanged();
            }
        }
    }

    private Func<ExecuteResult, Task<bool>>? _sessionStopped;

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

    public IReadOnlyList<VBModuleSymbol> ModulesOf(RDCoreProject project, Uri workspaceRoot)
        => [.. new ProjectSymbolProvider(workspaceRoot, project, fileSystem).ProvideSymbols().OfType<VBModuleSymbol>()];

    /// <inheritdoc/>
    public IRuntimeSession Compose(RDCoreProject project, Uri workspaceRoot) => Compose(project, workspaceRoot, known: null);

    /// <inheritdoc/>
    public IRuntimeSession Compose(RDCoreProject project, Uri workspaceRoot, SupportedLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (language.Id != _environment.Language.Id)
        {
            _environment = RDCore.SDK.Runtime.RuntimeEnvironmentProfile.Of(_environment) with { SourceLanguage = language };
        }

        return Compose(project, workspaceRoot, known: null);
    }

    /// <summary>
    /// Composes the session as <see cref="Compose(RDCoreProject, Uri)"/> does, the name and the kind of each module in <paramref name="known"/> given rather
    /// than read from its source: a built program's, whose sources are not where it runs.
    /// </summary>
    /// <param name="project">The <c>.rdproj</c> project structure.</param>
    /// <param name="workspaceRoot">The workspace root the project's modules are relative to.</param>
    /// <param name="known">The name and the kind of each module whose source is not read, by its <c>RelativeUri</c> in the project.</param>
    public IRuntimeSession Compose(RDCoreProject project, Uri workspaceRoot, IReadOnlyDictionary<string, (string Name, ModuleType Kind)>? known)
    {
        // a reload starts where the host did, as at start-up, and not where the session it replaces left the process.
        LeaveSession();

        _world = ComposeWorld(project, workspaceRoot);
        var (session, referenced, constants) = ComposeSession(_environment, fileSystem, librarySource, logger, project, workspaceRoot, Output, known);
        _session = session;
        // an event that something outside the workspace raises while no program runs prints where this host says: said as it is printed, and kept.
        _session.Turn.EventScope = BeginEvent;
        Image = NewImage(logger);
        Execution = new ProgramExecution(this, logger);
        Execution.SessionStopsChanged();
        ProjectName = project.Name;
        WorkspaceRoot = workspaceRoot;
        ModuleCount = project.Modules.Length;
        // App says the program is the workspace project, run from its folder; a built program says otherwise once it is composed.
        SessionApplication.Of(_session).Runs(project.Name, FolderOf(workspaceRoot) ?? string.Empty);
        // and its forms keep the system's theme as the project says, until the program says otherwise: the host knows it before it makes the first one.
        SessionApplication.Of(_session).KeepSystemTheme = project.KeepSystemTheme;
        Outside.Forms.KeepSystemTheme = project.KeepSystemTheme;
        _directoryBefore = CurrentDirectory();
        EnterWorkspaceRoot(workspaceRoot);

        // a project that references a library can make a call to its objects as soon as it runs, and what makes the call is got ready now; one that references none
        // makes no such call, and nothing is started for it. A console application's user interface is its console: what makes the call, which hosts its forms
        // too, is started when the program first makes one.
        if (_environment.AllowAutomation && !referenced.Libraries.IsEmpty && !RDCore.Runtime.Support.Build.ProgramDialogs.IsConsole(project.Build))
        {
            Outside.Automation.Prepare();
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "🧠 Runtime session composed: {modules} module(s), {references} reference(s), {constants} precompiler constant(s) (built-ins included); engine {engine}, {configuration} build.",
                project.Modules.Length, _session.References.Count, constants, _environment.ExecutionEngine, _environment.Configuration);
        }

        return _session;
    }

    /// <summary>
    /// Composes a session of a project: its precompiler constants, the standard library, the libraries the project references and its modules, which is what
    /// the runtime session and an analysis session (<see cref="ProjectAnalysisSessions"/>) are composed of alike.
    /// </summary>
    /// <param name="environment">The environment the session runs in.</param>
    /// <param name="fileSystem">The file system the modules are read from.</param>
    /// <param name="librarySource">Where the referenced libraries are found; the platform's library folder when <see langword="null"/>.</param>
    /// <param name="logger">Where a library that cannot be loaded is said.</param>
    /// <param name="project">The <c>.rdproj</c> project structure.</param>
    /// <param name="workspaceRoot">The root the project's modules are relative to, and addressed under.</param>
    /// <param name="output">The session's output channel.</param>
    /// <param name="known">The name and the kind of each module whose source is not read, by its <c>RelativeUri</c> in the project.</param>
    /// <returns>The session, the libraries it references, and how many precompiler constants it has (built-ins included).</returns>
    internal static (IRuntimeSession Session, ReferencedLibraries Referenced, int Constants) ComposeSession(
        IRuntimeEnvironmentProfile environment, IFileSystem fileSystem, ILibrarySource? librarySource, ILogger logger, RDCoreProject project, Uri workspaceRoot,
        RuntimeOutputRouter output, IReadOnlyDictionary<string, (string Name, ModuleType Kind)>? known = null)
    {
        var configuration = new ConfigurationSymbolProvider(environment, project);
        var modules = new ProjectSymbolProvider(workspaceRoot, project, fileSystem, known);
        // the standard library and the environment's own globals resolve in the session too, so a name
        // the language server bound to one of them binds to the same symbol here.
        var stdLib = new StdLibSymbolProvider(workspaceRoot, environment.Is64Bit);

        // and the libraries the project references, by name: the same symbols the language server bound the workspace's names to.
        var source = librarySource ?? new DirectoryLibrarySource(fileSystem, string.Empty);
        var referenced = LibrarySymbolProvider.Load(source, project.References, environment.Language);
        foreach (var problem in referenced.Problems)
        {
            logger.LogWarning("📚 The library '{library}' could not be loaded ({kind}{related}); a type of it is not a type of the project.",
                problem.Name, problem.Kind, problem.Related.IsEmpty ? string.Empty : ": " + string.Join(problem.Kind == LibraryProblemKind.Cyclic ? " → " : ", ", problem.Related));
        }

        var libraries = new LibrarySymbolProvider(workspaceRoot, referenced, environment.Is64Bit);

        // the descriptions of the libraries it does not reference are there for what tooling knows of an object created by name, and for nothing else.
        var session = RuntimeSessionComposer.Compose(environment, MapReferences(project.References, environment.Language), [configuration, stdLib, libraries, modules], output, libraries: source);
        // a predefined constant the project defines has the project's value on every platform a #If is evaluated for, and not that platform's.
        RDCore.Runtime.Semantics.Precompiler.ProjectConditionalConstants.Set(session, project.PrecompilerConstants.Select(constant => constant.Key));
        // an error raised with no source is the project's (Err.Source).
        if (project.Name is { Length: > 0 } name)
        {
            session.Errors.ProjectName = name;
        }

        return (session, referenced, configuration.ProvideSymbols().Count());
    }

    /// <summary>
    /// The code of a session, empty: a procedure the compiler refuses runs in the interpreter, and that is said.
    /// </summary>
    /// <param name="logger">Where a refused procedure is said.</param>
    internal static ProgramImage NewImage(ILogger logger)
    {
        var image = new ProgramImage();
        image.CompilationRefused += (procedure, exception)
            => logger.LogWarning(exception, "⚙️ {procedure} was not compiled: the compiler failed, and it runs in the interpreter.", procedure.Name);
        return image;
    }

    // the folder a file: root names, as the platform spells it. A root that is not a full path on this platform (file:///C:/... on Linux) names none here:
    // resolving it would read the current directory, which may be gone.
    private static string? FolderOf(Uri workspaceRoot)
    {
        if (!workspaceRoot.IsFile || !Path.IsPathFullyQualified(workspaceRoot.LocalPath))
        {
            return null;
        }

        // a client's file: URI may spell the drive in lower case (file:///c:/...), which CurDir and App.Path would give back as it was set: a drive is its
        // upper-case letter.
        var root = WorkspacePaths.Normalized(workspaceRoot.LocalPath);
        return OperatingSystem.IsWindows() && root.Length >= 2 && root[1] == ':' && char.IsAsciiLetterLower(root[0])
            ? char.ToUpperInvariant(root[0]) + root[1..]
            : root;
    }

    // a program starts in the folder of its workspace, as opening a project in VB6 makes its folder current: a relative path is a file of the workspace. The
    // directory is the session's, which ChDir moves too; End leaves it where the program put it. The shell's scratch workspace is no folder of the user's.
    private void EnterWorkspaceRoot(Uri workspaceRoot)
    {
        if (!workspaceRoot.IsFile)
        {
            return;
        }

        var root = workspaceRoot.LocalPath;
        RDCoreDiagnosticCode? error = VBRuntimeErrorId.PathNotFound.ToCode();
        if (FolderOf(workspaceRoot) is { } folder)
        {
            root = folder;
            if (ScratchWorkspace.Is(fileSystem, root))
            {
                return;
            }

            error = _session!.Files.TryChangeDirectory(root);
        }

        if (error is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning("📂 The workspace root {root} could not be made the current directory ({error}): it is left where it was.", root, error);
        }
    }

    // the directory is the process's, so a session that ends gives it back. Otherwise Windows could not delete the folder the process is in, and on Linux,
    // once that folder is deleted, every later read of the current directory fails. When the directory the session found is gone, the process stays where it is.
    private void LeaveSession()
    {
        // a program that waits at a stop holds a thread of its own, and the session it runs in, until it is ended: nothing resumes it once its session is left.
        Execution?.EndWaiting();

        // the files a program left open are the session's, and close with it: a session replaced or let go of holds none of them open, whichever way its
        // program ended (an internal error, which closes nothing, included).
        _ = _session?.Files.CloseAll();

        // and the evaluation of its code is the session's too: a session replaced or let go of has no use for what is still to be found out, which can
        // take seconds and gigabytes (an array of millions of elements).
        Image.Semantics.StopEvaluations();

        if (_session is null || _directoryBefore is not { } before)
        {
            return;
        }

        _directoryBefore = null;
        if (string.Equals(CurrentDirectory(), before, WorkspacePaths.Comparison))
        {
            return;
        }

        RDCoreDiagnosticCode? error;
        try
        {
            error = _session.Files.TryChangeDirectory(before);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = VBRuntimeErrorId.PathNotFound.ToCode();
        }

        if (error is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning("📂 The directory {directory} the session was composed in could not be made the current directory again ({error}).", before, error);
        }
    }

    // null when the current directory was deleted, which Linux lets a process's be.
    private string? CurrentDirectory()
    {
        try
        {
            return _session!.Files.TryGetCurrentDirectory(null, out var path) is null ? path : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => LeaveSession();

    // the settings are the workspace's (RD-VBAL 2.1.1), and a dialog is the client's to answer when it shows dialogs, the platform's when it does not.
    private ExternalWorld ComposeWorld(RDCoreProject project, Uri workspaceRoot)
    {
        var host = outside ?? MachineExternal.World;
        var settings = new JsonWorkspaceSettings(fileSystem, workspaceRoot.IsFile ? workspaceRoot.LocalPath : workspaceRoot.OriginalString);
        return host with
        {
            Interaction = new RelayedUserInteraction(this, host.Interaction, logger),
            DialogTitle = project.Name is { Length: > 0 } name ? name : host.DialogTitle,
            WorkspaceSettings = settings,
            ProgramSettings = MachinePlatform.ProgramSettings(_environment, settings),
        };
    }

    // the .rdproj declares references in precedence order, the earliest highest (RD-VBAL §2.3.1.3, MS-VBAL §4.1); the list index is the rank. The standard
    // library, which every project has and no .rdproj lists, is the first of them, and the language's implicit references come next, as LibrarySymbolProvider.Load
    // ranks them; a second reference to a library already there is left out, as Load leaves it out.
    private static IReadOnlyList<ReferencePriorityInfo> MapReferences(RDCoreReference[] references, SupportedLanguage language)
        => [new ReferencePriorityInfo(StandardLibrary.LibraryName, 0),
            .. language.ImplicitReferences.Concat(references.Where(reference => !reference.IsStandardLibrary).Select(reference => reference.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select((name, rank) => new ReferencePriorityInfo(name, rank + 1))];
}
