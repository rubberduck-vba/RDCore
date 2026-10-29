using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Bench;

/// <summary>
/// A module of a workspace that is benchmarked: its name and kind, as the module says them, and where it is.
/// </summary>
/// <param name="Name">The name the module declares (<c>Attribute VB_Name</c>), or its file name when it declares none.</param>
/// <param name="Type">A standard module or a class module, as its header says.</param>
/// <param name="RelativePath">Where the module is, relative to the root of the workspace.</param>
/// <param name="Source">The source of the module.</param>
internal sealed record class BenchModule(string Name, ModuleType Type, string RelativePath, string Source);

/// <summary>
/// The code <c>rdc bench</c> runs: the modules of a workspace, and the project they make.
/// </summary>
/// <remarks>
/// A folder with a <c>.rdproj</c> is the project it describes - its modules, its references and its precompiler constants. A folder with none is a project of every
/// <c>.bas</c> and <c>.cls</c> file under it, and a single file is a project of that one module.
/// </remarks>
/// <param name="Root">The absolute path of the root of the workspace.</param>
/// <param name="Project">The project: what the session is composed from.</param>
/// <param name="Modules">The modules, in the order the project lists them.</param>
internal sealed record class BenchWorkspace(string Root, RDCoreProject Project, IReadOnlyList<BenchModule> Modules)
{
    /// <summary>The extensions of the module files of a folder that has no <c>.rdproj</c>.</summary>
    public static readonly IReadOnlyList<string> ModuleExtensions = [".bas", ".cls"];

    /// <summary>
    /// The workspace at <paramref name="path"/>: a <c>.bas</c> or <c>.cls</c> file, or a folder.
    /// </summary>
    /// <param name="fileSystem">The file system the workspace is on.</param>
    /// <param name="projects">Reads a <c>.rdproj</c>.</param>
    /// <param name="path">The file or the folder.</param>
    /// <param name="token">A token that cancels the reading.</param>
    /// <exception cref="FileNotFoundException">There is no such file or folder.</exception>
    public static async Task<BenchWorkspace> LoadAsync(IFileSystem fileSystem, IProjectFileLoader projects, string path, CancellationToken token = default)
    {
        var full = fileSystem.Path.GetFullPath(path);
        if (fileSystem.File.Exists(full))
        {
            var root = fileSystem.Path.GetDirectoryName(full)!;
            var name = fileSystem.Path.GetFileNameWithoutExtension(full);
            return Compose(fileSystem, root, new RDCoreProject { Name = name }, [new RDCoreModule { RelativeUri = fileSystem.Path.GetFileName(full) }]);
        }

        if (!fileSystem.Directory.Exists(full))
        {
            throw new FileNotFoundException($"'{path}' is neither a module file nor a folder.", path);
        }

        if (projects.TryLocate(full, out _))
        {
            var project = (await projects.LoadAsync(full, token)).ProjectInfo;
            return Compose(fileSystem, full, project, project.Modules);
        }

        var files = ModuleExtensions
            .SelectMany(extension => fileSystem.Directory.EnumerateFiles(full, "*" + extension, SearchOption.AllDirectories))
            .Select(file => fileSystem.Path.GetRelativePath(full, file))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Compose(fileSystem, full, new RDCoreProject { Name = fileSystem.Path.GetFileName(full.TrimEnd(fileSystem.Path.DirectorySeparatorChar)) },
            [.. files.Select(file => new RDCoreModule { RelativeUri = file })]);
    }

    // the project lists the modules by where they are, and says which are document modules; the session reads the same files, and the standard library is
    // referenced first, as it always is.
    private static BenchWorkspace Compose(IFileSystem fileSystem, string root, RDCoreProject project, IReadOnlyList<RDCoreModule> listed)
    {
        var modules = listed.Select(module =>
        {
            var source = fileSystem.File.ReadAllText(fileSystem.Path.Combine(root, module.RelativeUri));
            return new BenchModule(ModuleName.Resolve(source, module.RelativeUri), ModuleKinds.Of(module, source), module.RelativeUri, source);
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
