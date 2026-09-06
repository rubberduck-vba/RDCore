using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;

namespace RDCore.CLI.Host.Symbols;

/// <summary>
/// Produces the module symbols declared by a <c>.rdproj</c>'s <see cref="RDCoreProject"/> — one per
/// source module, without parsing it.
/// </summary>
/// <remarks>
/// ⚖️ GPLv3. Runs in the environment-host process. Library reference symbols are the
/// <c>LibrarySymbolProvider</c>'s responsibility; member symbols come from the language server's
/// <c>SyntaxTreeSymbolProvider</c> over <c>rdcore/host/symbols/define</c>.
/// </remarks>
public sealed class ProjectSymbolProvider(Uri workspaceRoot, RDCoreProject project) : ISymbolProvider
{
    public IEnumerable<Symbol> ProvideSymbols()
    {
        foreach (var module in project.Modules)
        {
            var name = module.DefaultName;
            var isClass = module.Super is not null
                || string.Equals(Path.GetExtension(module.RelativeUri), ".cls", StringComparison.OrdinalIgnoreCase);

            yield return isClass
                ? new VBClassModuleSymbol(workspaceRoot, workspaceRoot, name)
                : new VBStandardModuleSymbol(workspaceRoot, workspaceRoot, name);
        }
    }
}
