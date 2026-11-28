using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Build;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Hosting;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using System.IO.Abstractions;

namespace RDCore.Runtime.Composer;

/// <summary>
/// What an RD-VBA program is composed with by <see cref="ProgramComposer.ComposeAsync"/>.
/// </summary>
public sealed record class ProgramCompositionOptions
{
    /// <summary>
    /// The environment the program's machines run in: its language, pointer width, regional settings and build configuration.
    /// <see cref="RuntimeEnvironmentProfile.Default"/> when it is not given. The language a <c>.rdproj</c> or <c>.vbp</c> says is the program's over this
    /// one's.
    /// </summary>
    public IRuntimeEnvironmentProfile? Environment { get; init; }

    /// <summary>
    /// The folder of the descriptions of the libraries a project references, read as the platform's <c>Symbols</c> folder is
    /// (<see cref="DirectoryLibrarySource"/>). The libraries the platform itself provides, such as <c>VB</c>, are found without it; a reference to another
    /// library finds nothing when no folder is given.
    /// </summary>
    public string? LibrariesFolder { get; init; }

    /// <summary>
    /// The workspace's settings, which say the files that declare modules in blocks. The defaults when they are not given.
    /// </summary>
    public SdkWorkspaceOptions? Workspace { get; init; }

    /// <summary>
    /// What the composition and the machines log to. Nothing is logged when it is not given.
    /// </summary>
    public ILoggerFactory? Loggers { get; init; }

    /// <summary>
    /// The executable the machines' external host is started from: the process that makes their calls to the outside world of the machine, such as the forms
    /// they make, the automation servers they create objects of and the native libraries they call. It is <c>rdc</c>, run in external mode, as
    /// <c>rdc run</c> runs a program. <c>rdc</c> beside the host's own assemblies when it is not given. Nothing is started until a machine reaches out, and
    /// the machines of every program given the same executable share one external host.
    /// </summary>
    public string? ExternalHost { get; init; }
}

/// <summary>
/// A program that could not be composed: a module of it has a syntax error, or does not load.
/// </summary>
/// <param name="problems">What kept each module from being parsed or loaded, one line for each, starting with the module's name.</param>
public sealed class ProgramCompositionException(IReadOnlyList<string> problems) : Exception(string.Join(System.Environment.NewLine, problems))
{
    /// <summary>
    /// What kept each module from being parsed or loaded, one line for each, starting with the module's name.
    /// </summary>
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Composes an RD-VBA program from its files, in process, as <c>rdc run</c> loads it: for development and tests. A deployed host loads the composition
/// <c>rdc compose</c> wrote instead, with <c>RDCore.Runtime.Hosting</c>, which has no parser.
/// </summary>
public static class ProgramComposer
{
    /// <summary>
    /// Composes the program of a path: the files are read and parsed, the symbols of every module are defined and bound, and its code is loaded, once.
    /// </summary>
    /// <remarks>
    /// Each machine the program makes (<see cref="IComposedProgram.CreateMachineAsync"/>) has a session of its own, composed from what was defined; no file is
    /// read again, so a change to a file after the program is composed reaches none of its machines. Machines may be made concurrently, and run at the same
    /// time, each on a thread of its own.
    /// </remarks>
    /// <param name="path">
    /// A <c>.rdproj</c> file or its folder, a <c>.vbp</c> file or its folder (the only <c>.vbp</c> in it), a folder of module files, or a module file (the
    /// project of its folder when the folder has a <c>.rdproj</c> that lists it).
    /// </param>
    /// <param name="options">What the program is composed with; the defaults of <see cref="ProgramCompositionOptions"/> when it is not given.</param>
    /// <param name="cancellationToken">Cancels the composition.</param>
    /// <returns>The program; its owner disposes it, after its machines.</returns>
    /// <exception cref="FileNotFoundException">There is no such file or folder.</exception>
    /// <exception cref="InvalidOperationException">A reference of the project to a project names no project of the workspace, or more than one.</exception>
    /// <exception cref="ProgramCompositionException">A module of the program has a syntax error, or does not load.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async Task<IComposedProgram> ComposeAsync(
        string path, ProgramCompositionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var (image, services, loaded, _) = await ComposeImageAsync(path, options, cancellationToken);
        return new ComposedProgram(image, services, loaded);
    }

    // the image of the program of a path, what its machines use, the session it was composed in, which the caller disposes or gives a machine, and the
    // libraries its project references. rdc gives the file system and the libraries of its platform (libraries), over the options' folder.
    internal static async Task<(ProgramCompositionImage Image, ProgramServices Services, EnvironmentSessionProvider Loaded,
        IReadOnlyList<LibraryDescription> Libraries)> ComposeImageAsync(
        string path, ProgramCompositionOptions? options, CancellationToken cancellationToken, IFileSystem? files = null, ILibrarySource? platform = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        options ??= new ProgramCompositionOptions();
        var fileSystem = files ?? new FileSystem();
        var loggers = options.Loggers ?? NullLoggerFactory.Instance;
        var libraries = platform ?? PlatformLibraries.With(
            options.LibrariesFolder is { } folder ? new DirectoryLibrarySource(fileSystem, folder) : new CompositeLibrarySource());
        var workspaceOptions = options.Workspace ?? new SdkWorkspaceOptions();
        var services = new ProgramServices(
            fileSystem, libraries, loggers, Microsoft.Extensions.Options.Options.Create(new SdkAppOptions { Workspace = workspaceOptions }),
            options.ExternalHost is { } executable ? MachineExternal.WorldOf(executable) : null);

        // a project file is read as the folder it is in, which is its project's: a file is otherwise a module.
        var full = fileSystem.Path.GetFullPath(path);
        if (fileSystem.File.Exists(full) && (string.Equals(fileSystem.Path.GetFileName(full), ProjectFile.FileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileSystem.Path.GetExtension(full), ".vbp", StringComparison.OrdinalIgnoreCase)))
        {
            path = fileSystem.Path.GetDirectoryName(full)!;
        }

        var requested = options.Environment ?? RuntimeEnvironmentProfile.Default;
        var workspace = await ProgramWorkspace.LoadAsync(fileSystem, new ProjectFileLoader(fileSystem), path, cancellationToken,
            ansiCodePage: requested.AnsiCodePage, inItsProject: true, workspace: workspaceOptions);

        // the program is composed in the environment its image records, as each of its machines is, wherever the image is loaded.
        var language = WorkspaceProgramLoader.InTheLanguageOf(workspace, requested);
        var environment = BuiltEnvironment.Of(language);
        var profile = environment.Profile with { ExecutionEngine = language.ExecutionEngine };
        var definitions = new List<DefineSymbolsParams>();
        var loader = new WorkspaceProgramLoader(fileSystem, new ErrorDetailsRecorder(), loggers, libraries, services.Options);
        var loaded = await loader.LoadAsync(workspace, profile, services.Outside, cancellationToken, definitions);
        if (loaded.Problems.Count > 0)
        {
            loaded.Provider.Dispose();
            throw new ProgramCompositionException(loaded.Problems);
        }

        var modules = workspace.Modules.SelectMany(module => module.Declared.Select(
            declared => new BuiltModule(module.RelativePath, declared.Name, declared.Kind) { IsBlock = declared.IsBlock, IsPartial = declared.IsPartial }))
            .ToArray();
        var image = new ProgramCompositionImage(
            workspace.Project, workspace.RootUri, environment, language.ExecutionEngine, modules, definitions, loaded.Documents);
        return (image, services, loaded.Provider, loaded.Libraries);
    }
}
