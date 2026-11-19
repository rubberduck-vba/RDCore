using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Bench;

/// <summary>
/// A module of a workspace that is benchmarked: its name and kind, as the module says them, and where it is.
/// </summary>
/// <param name="Name">The name the module declares (<c>Attribute VB_Name</c>), or its file name when it declares none.</param>
/// <param name="Type">A standard module or a class module, as its header says.</param>
/// <param name="RelativePath">Where the module is, relative to the root of the workspace.</param>
/// <param name="Source">The source of the module.</param>
/// <param name="Companion">
/// The content of the companion file its source names (a form's <c>.frx</c>), beside it; empty when it names none, when the file is not there, and when it
/// cannot be read.
/// </param>
/// <param name="CompanionProblem">Why the companion file could not be read; null when it was, when the module names none, and when the file is not there.</param>
internal sealed record class BenchModule(string Name, ModuleType Type, string RelativePath, string Source, ReadOnlyMemory<byte> Companion = default,
    Exception? CompanionProblem = null);

/// <summary>
/// The code <c>rdc bench</c> runs: the modules of a workspace, and the project they make.
/// </summary>
/// <remarks>
/// A folder with a <c>.rdproj</c> is the project it describes: its modules, its references and its precompiler constants. A folder with none is the project
/// the language server makes of it (<see cref="IProjectFileLoader.LoadOrSynthesizeAsync"/>): every file under it that is written in the language, and a single
/// file is a project of that one module.
/// </remarks>
/// <param name="Root">The absolute path of the root of the workspace.</param>
/// <param name="Project">The project: what the session is composed from.</param>
/// <param name="Modules">The modules, in the order the project lists them.</param>
internal sealed record class BenchWorkspace(string Root, RDCoreProject Project, IReadOnlyList<BenchModule> Modules)
{
    /// <summary>
    /// The root of the workspace as its <c>file</c> URI (<see cref="SymbolUri.Root"/>): what its modules are addressed under.
    /// </summary>
    public Uri RootUri => SymbolUri.Root(Root);

    /// <summary>
    /// The <c>file</c> URI of a module of the workspace: the document its parse, and the instructions made of it, say they are written in.
    /// </summary>
    /// <remarks>
    /// Written as its URI, never as its path, as <see cref="SymbolUri.Root"/> writes a root: a parse travels as JSON, and the text of a path that begins with
    /// <c>/</c> is read back as a relative URI.
    /// </remarks>
    /// <param name="module">A module of the workspace.</param>
    public Uri DocumentOf(BenchModule module) => new(new Uri(Path.Combine(Root, module.RelativePath)).AbsoluteUri);

    /// <summary>
    /// The workspace at <paramref name="path"/>: a module file, or a folder.
    /// </summary>
    /// <param name="fileSystem">The file system the workspace is on.</param>
    /// <param name="projects">Reads a <c>.rdproj</c>, and makes the project of a folder that has none.</param>
    /// <param name="path">The file or the folder.</param>
    /// <param name="token">A token that cancels the reading.</param>
    /// <param name="language">The language whose files are the modules of a folder that has no <c>.rdproj</c>; RD-VBA when it is not given.</param>
    /// <param name="ansiCodePage">
    /// The code page of a module file that has no byte order mark and is not valid UTF-8 (<see cref="ModuleFileText.Decode(byte[], int)"/>): the
    /// environment's.
    /// </param>
    /// <exception cref="FileNotFoundException">There is no such file or folder.</exception>
    public static async Task<BenchWorkspace> LoadAsync(IFileSystem fileSystem, IProjectFileLoader projects, string path, CancellationToken token = default,
        SupportedLanguage? language = null, int ansiCodePage = ModuleFileText.DefaultAnsiCodePage)
    {
        var full = fileSystem.Path.GetFullPath(path);
        if (fileSystem.File.Exists(full))
        {
            var root = fileSystem.Path.GetDirectoryName(full)!;
            var name = fileSystem.Path.GetFileNameWithoutExtension(full);
            var module = new RDCoreModule { RelativeUri = fileSystem.Path.GetFileName(full) };
            return Compose(fileSystem, root, new RDCoreProject { Name = name }, [module], ansiCodePage);
        }

        if (!fileSystem.Directory.Exists(full))
        {
            throw new FileNotFoundException($"'{path}' is neither a module file nor a folder.", path);
        }

        var project = (await projects.LoadOrSynthesizeAsync(full, language ?? SupportedLanguages.RDVBA, token)).ProjectInfo;
        return Compose(fileSystem, full, project, project.Modules, ansiCodePage);
    }

    // the project lists the modules by where they are, and says which are document modules; the session reads the same files, in their own encoding, a form's
    // companion file with its module as the language server reads it, and the standard library is referenced first, as it always is.
    private static BenchWorkspace Compose(IFileSystem fileSystem, string root, RDCoreProject project, IReadOnlyList<RDCoreModule> listed, int ansiCodePage)
    {
        var modules = listed.Select(module =>
        {
            var path = fileSystem.Path.Combine(root, module.RelativeUri);
            var source = ModuleFileText.Decode(fileSystem.File.ReadAllBytes(path), ansiCodePage).Text;
            var companion = FormDesigner.ReadCompanion(fileSystem, path, source, out var problem);
            return new BenchModule(ModuleName.Resolve(source, module.RelativeUri), ModuleKinds.Of(module, source), module.RelativeUri, source, companion, problem);
        }).ToArray();

        // the standard library is every project's own, and no reference of it.
        return new BenchWorkspace(root, project with
        {
            Name = project.Name is { Length: > 0 } ? project.Name : "Bench",
            Modules = [.. listed.Select(module => new RDCoreModule { RelativeUri = module.RelativeUri, Super = module.Super })],
            References = [.. project.References.Where(reference => !reference.IsStandardLibrary)],
        }, modules);
    }
}
