using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.10
public static class DeclarationShadowing
{
    public static HiddenDeclaration? Of(Symbol declared, ISymbolResolver resolver, Uri module)
    {
        // Me is the object a member is called on, which no other declaration can be named (MS-VBAL 5.3.1.5).
        if (declared is not (VBLocalVariableSymbol or VBLocalConstantSymbol) || string.Equals(declared.Name, "Me", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resolution = resolver.ResolveValue(declared.Name, ScopeKind.Module, module);
        return resolution.Symbol is { } hidden && hidden.SemanticId != declared.SemanticId
            ? new HiddenDeclaration(hidden.Uri, QualifiedNameOf(hidden))
            : null;
    }

    // the symbol as code names it, after the module or library that declares it.
    private static string QualifiedNameOf(Symbol symbol)
        => SymbolUri.QualifiedNameOf(symbol.Uri) is { Length: > 0 } name ? name : symbol.Name;
}
