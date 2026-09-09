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
/// A module's name is its <c>Attribute VB_Name</c>, so each module's source is read to resolve it;
/// the file name is the fallback when the source is unreadable or declares no such attribute. The
/// language server resolves the same name from the parsed AST, so the members it defines parent onto
/// the module symbol composed here.
/// </para>
/// </remarks>
public sealed class ProjectSymbolProvider(Uri workspaceRoot, RDCoreProject project, IFileSystem fileSystem) : ISymbolProvider
{
    public IEnumerable<Symbol> ProvideSymbols()
    {
        foreach (var module in project.Modules)
        {
            var name = ModuleName.Resolve(ReadSourceOrNull(module.RelativeUri), module.RelativeUri);
            var isClass = module.Super is not null
                || string.Equals(Path.GetExtension(module.RelativeUri), ".cls", StringComparison.OrdinalIgnoreCase);

            yield return isClass
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
