using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Static;

// the names of the document modules a name written in a module can refer to: each name of a document module, of the module's project or of a project it
// references, that the module looks up as one. MS-VBA's host makes such a name refer to its document; RD-VBA has no host, and such a name that the code
// declares for itself refers to what its binding says.
public static class DocumentModuleNames
{
    public static ImmutableArray<string> Of(IRuntimeSymbolResolver resolver, LexicalScope global, Uri module) => [.. global.Declarations
        .OfType<VBClassModuleSymbol>()
        .Where(candidate => candidate.IsDocument)
        .Select(candidate => candidate.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(name => resolver.ResolveType(name, ScopeKind.Global, module).Symbol is VBClassModuleSymbol { IsDocument: true })];
}
