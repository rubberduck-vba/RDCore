using RDCore.Runtime.LanguageCore.Services;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.10
public static class DeclarationShadowing
{
    public static HiddenDeclaration? Of(Symbol declared, ISymbolResolver resolver, ScopeTree scopes, Uri module)
    {
        // Me is the object a member is called on, which no other declaration can be named (MS-VBAL 5.3.1.5).
        if (string.Equals(declared.Name, "Me", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hidden = declared switch
        {
            // a local or a parameter hides what the name means in the rest of its module.
            VBLocalVariableSymbol or VBLocalConstantSymbol => resolver.ResolveValue(declared.Name, ScopeKind.Module, module).Symbol,
            // a declaration of the module hides what the name would mean in the module had the module not declared it: a Public member of another module,
            // a member of a library, or a member of the host's class in a document module. A name the code declares by writing it, and a member a form's
            // designer declares, are not written as a declaration.
            VBModuleFieldVariableMemberSymbol or VBConstantMemberSymbol or VBFunctionMemberSymbol or VBProcedureMemberSymbol
                or VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol
                when !declared.GetProperty(SymbolProperties.ImplicitlyDeclared) && !FormObject.IsDeclaredBy(declared)
                => new ScopeTreeSymbolResolver(scopes).ResolveValueWithout(declared, module).Symbol,
            _ => null,
        };

        return hidden is not null && hidden.SemanticId != declared.SemanticId
            ? new HiddenDeclaration(hidden.Uri, QualifiedNameOf(hidden))
            : null;
    }

    // the symbol as code names it, after the module or library that declares it.
    private static string QualifiedNameOf(Symbol symbol)
        => SymbolUri.QualifiedNameOf(symbol.Uri) is { Length: > 0 } name ? name : symbol.Name;
}
