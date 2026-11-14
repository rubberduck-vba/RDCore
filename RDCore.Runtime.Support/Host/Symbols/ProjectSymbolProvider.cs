using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using RDCore.SDK.Platform.Components;

namespace RDCore.Runtime.Support.Host.Symbols;

/// <summary>
/// Produces the module symbols declared by a <c>.rdproj</c>'s <see cref="RDCoreProject"/> — one per
/// source module, without parsing it.
/// </summary>
/// <remarks>
/// ⚖️ GPLv3. Runs in the environment-host process. Library reference symbols are the
/// <c>LibrarySymbolProvider</c>'s responsibility; member symbols come from the language server's
/// <c>SyntaxTreeSymbolProvider</c> over <c>rdcore/host/symbols/define</c>.
/// <para>
/// A module's name is its <c>Attribute VB_Name</c> and its kind is the project's <c>Super</c> or its
/// <c>VERSION</c> header (<see cref="ModuleName"/> / <see cref="ModuleKinds"/>) — read once per
/// module, not inferred from the file extension. The file name is the name fallback when the source
/// is unreadable or declares no attribute. The language server resolves the same name and kind from
/// the parsed module, so the members it defines parent onto the module symbol composed here.
/// </para>
/// <para>
/// A built program runs where its workspace's sources are not: the name and the kind its build read of each module are given instead (<c>known</c>),
/// and no source of a module named there is read.
/// </para>
/// </remarks>
/// <param name="workspaceRoot">The workspace root the project's modules are relative to.</param>
/// <param name="project">The project.</param>
/// <param name="fileSystem">The file system the sources are read from.</param>
/// <param name="known">The name and the kind of each module whose source is not read, by its <c>RelativeUri</c> in the project.</param>
public sealed class ProjectSymbolProvider(Uri workspaceRoot, RDCoreProject project, IFileSystem fileSystem,
    IReadOnlyDictionary<string, (string Name, ModuleType Kind)>? known = null) : ISymbolProvider
{
    public IEnumerable<Symbol> ProvideSymbols()
    {
        var modules = new List<Symbol>();
        foreach (var module in project.Modules)
        {
            // the kind is the project's word for a document module, and the source's otherwise (ModuleKinds.Of), as the language server reads it. What a
            // document module's attributes make it is the language server's to send with its members (DefineSymbolsParams.Directives).
            // A document module's host class is the project's word too (ModuleKinds.HostClassOf): its code names that class's members.
            var (name, kind) = known is not null && known.TryGetValue(module.RelativeUri, out var given) ? given : Read(module);
            modules.Add(kind.IsClass()
                ? new VBClassModuleSymbol(workspaceRoot, workspaceRoot, name)
                {
                    IsDocument = kind == ModuleType.DocumentModule,
                    HostClass = module.Super is { } super ? ModuleKinds.HostClassOf(super) : null,
                }
                : new VBStandardModuleSymbol(workspaceRoot, workspaceRoot, name));
        }

        // the project itself, by its name: what qualifies the names of its own modules and types (MVVM.AppContext, MS-VBAL §5.6.12), as a library's name
        // qualifies its own. It is of no library, which is what tells it from the libraries' projects. A module named as the project keeps the name, as the
        // language server's composition has it: MS-VBA refuses one, and a project file can still name one so.
        if (project.Name is { Length: > 0 } projectName
            && !modules.Any(module => string.Equals(module.Name, projectName, StringComparison.OrdinalIgnoreCase)))
        {
            yield return new VBProjectSymbol(workspaceRoot, projectName);
        }

        foreach (var module in modules)
        {
            yield return module;
        }
    }

    // the module's name and kind, as its source says them.
    private (string Name, ModuleType Kind) Read(RDCoreModule module)
    {
        var source = ReadSourceOrNull(module.RelativeUri);
        return (ModuleName.Resolve(source, module.RelativeUri), ModuleKinds.Of(module, source));
    }

    private string? ReadSourceOrNull(string relativeUri)
    {
        try
        {
            var path = fileSystem.Path.Combine(workspaceRoot.LocalPath, relativeUri);
            return fileSystem.File.Exists(path) ? fileSystem.File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // an unreadable module still gets a symbol, keyed on its file name.
            return null;
        }
    }
}
