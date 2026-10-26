using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;

namespace RDCore.CLI.Host.Symbols;

/// <summary>
/// Produces the module symbols declared by a <c>.rdproj</c>'s <see cref="RDCoreProject"/> — one per
/// source module, without parsing it.
/// </summary>
/// <remarks>
/// ⚖️ GPLv3. Runs in the environment-host process. Library reference symbols are the
/// <c>LibrarySymbolProvider</c>'s responsibility; member symbols come from the language server's
/// <c>SyntaxTreeSymbolProvider</c> over <c>rdcore/host/symbols/define</c>.
/// <para>
/// A module's name is its <c>Attribute VB_Name</c> and its kind is its <c>VERSION</c> header
/// (<see cref="ModuleName"/> / <see cref="ModuleHeader"/>) — both are read from the source, once per
/// module, not inferred from the file extension. The file name is the name fallback when the source
/// is unreadable or declares no attribute. The language server resolves the same name and kind from
/// the parsed module, so the members it defines parent onto the module symbol composed here.
/// </para>
/// </remarks>
public sealed class ProjectSymbolProvider(Uri workspaceRoot, RDCoreProject project, IFileSystem fileSystem) : ISymbolProvider
{
    public IEnumerable<Symbol> ProvideSymbols()
    {
        var modules = new List<Symbol>();
        foreach (var module in project.Modules)
        {
            var source = ReadSourceOrNull(module.RelativeUri);
            var name = ModuleName.Resolve(source, module.RelativeUri);

            // kind is the source's business: a VERSION header makes it a class/designer module.
            // .doccls modules carry no VBE header and are out of scope for now.
            modules.Add(ModuleHeader.IsClassModule(source) ?? false
                ? new VBClassModuleSymbol(workspaceRoot, workspaceRoot, name)
                : new VBStandardModuleSymbol(workspaceRoot, workspaceRoot, name));
        }

        // the project itself, by its name: what qualifies the names of its own modules and types (MVVM.AppContext, MS-VBAL §5.6.12), as a library's name
        // qualifies its own. It is of no library, which is what tells it from the libraries' projects. A module named as the project keeps the name, as the
        // language server's composition has it: MS-VBA refuses one, and the shell's own project is named as its one module.
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
            return fileSystem.File.Exists(path) ? fileSystem.File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // an unreadable module still gets a symbol, keyed on its file name.
            return null;
        }
    }
}
