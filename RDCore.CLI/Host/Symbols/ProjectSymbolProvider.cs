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
        foreach (var module in project.Modules)
        {
            var source = ReadSourceOrNull(module.RelativeUri);
            var name = ModuleName.Resolve(source, module.RelativeUri);

            // kind is the source's business: a VERSION header makes it a class/designer module.
            // .doccls modules carry no VBE header and are out of scope for now.
            yield return ModuleHeader.IsClassModule(source) ?? false
                ? new VBClassModuleSymbol(workspaceRoot, workspaceRoot, name)
                : new VBStandardModuleSymbol(workspaceRoot, workspaceRoot, name);
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
