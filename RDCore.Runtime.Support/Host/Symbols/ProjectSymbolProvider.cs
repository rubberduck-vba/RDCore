using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
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
/// A built program runs where its workspace's sources are not: the modules its build read of each file are given instead (<c>known</c>), and no source
/// of a file named there is read. A file of several modules (its <c>Class</c> and <c>Module</c> blocks) has a symbol for each. The parts of one module
/// (its <c>Partial</c> blocks) have one symbol, of the plain part's name and kind, or of the first part's when every part is <c>Partial</c>.
/// </para>
/// </remarks>
/// <param name="workspaceRoot">The workspace root the project's modules are relative to.</param>
/// <param name="project">The project.</param>
/// <param name="fileSystem">The file system the sources are read from.</param>
/// <param name="known">
/// The modules of each file whose source is not read (<see cref="ModuleKinds.ModulesOf(RDCoreModule, string?, SdkWorkspaceOptions)"/>), by its
/// <c>RelativeUri</c> in the project.
/// </param>
/// <param name="ansiCodePage">
/// The code page of a source that has no byte order mark and is not valid UTF-8 (<see cref="ModuleFileText.Decode(byte[], int)"/>): the environment's.
/// </param>
/// <param name="workspace">The workspace's settings, which say the files that declare modules in blocks; the defaults when they are not given.</param>
public sealed class ProjectSymbolProvider(Uri workspaceRoot, RDCoreProject project, IFileSystem fileSystem,
    IReadOnlyDictionary<string, IReadOnlyList<DeclaredModule>>? known = null, int ansiCodePage = ModuleFileText.DefaultAnsiCodePage,
    SdkWorkspaceOptions? workspace = null) : ISymbolProvider
{
    private readonly SdkWorkspaceOptions _workspace = workspace ?? new();

    // the modules the project's files declare, a block of a file one of them, as the symbols were last provided: none until they are.
    public int ModuleCount { get; private set; }

    public IEnumerable<Symbol> ProvideSymbols()
    {
        var modules = new List<Symbol>();
        var parts = new List<(RDCoreModule File, DeclaredModule Module)>();
        foreach (var module in project.Modules)
        {
            // a file of several modules declares each (ModuleKinds.ModulesOf): its own, which the project's word is about, then each block's.
            var declared = known is not null && known.TryGetValue(module.RelativeUri, out var given)
                ? given
                : ModuleKinds.ModulesOf(module, ReadSourceOrNull(module.RelativeUri), _workspace);
            parts.AddRange(declared.Select(each => (module, each)));
        }

        // the Partial parts of a name join its plain part, and the first of them is the module when every part of the name is Partial (ModuleAssembly).
        var plain = parts.Where(part => !part.Module.IsPartial).Select(part => part.Module.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var partial = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (module, (name, kind, isBlock)) in parts.Where(part => !part.Module.IsPartial
            || (!plain.Contains(part.Module.Name) && partial.Add(part.Module.Name))))
        {
            // the kind is the project's word for a document module, and the source's otherwise (ModuleKinds.Of), as the language server reads it. What a
            // document module's attributes make it is the language server's to send with its members (DefineSymbolsParams.Directives).
            // A document module's host class is the project's word too (ModuleKinds.HostClassOf): its code names that class's members.
            modules.Add(kind.IsClass()
                ? new VBClassModuleSymbol(workspaceRoot, workspaceRoot, name)
                {
                    IsDocument = kind == ModuleType.DocumentModule,
                    HostClass = !isBlock && module.Super is { } super ? ModuleKinds.HostClassOf(super) : null,
                }
                : new VBStandardModuleSymbol(workspaceRoot, workspaceRoot, name));
        }

        ModuleCount = modules.Count;

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

    private string? ReadSourceOrNull(string relativeUri)
    {
        try
        {
            var path = fileSystem.Path.Combine(workspaceRoot.LocalPath, relativeUri);
            return fileSystem.File.Exists(path) ? ModuleFileText.Decode(fileSystem.File.ReadAllBytes(path), ansiCodePage).Text : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // an unreadable module still gets a symbol, keyed on its file name.
            return null;
        }
    }
}
