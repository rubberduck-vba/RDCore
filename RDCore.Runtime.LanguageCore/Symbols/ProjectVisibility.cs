using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.LanguageCore.Symbols;

// MS-VBAL §5.2.3, §5.6.10: what the code outside a module sees of its members. The other modules of its project see a member declared Public, Global
// or Friend, or with no access modifier unless it is a module variable or a constant, which are private by default. Another project sees what
// ReferencedProjectSymbols makes accessible. Code of no known project sees none of them.
public sealed class ProjectVisibility : IProjectVisibility
{
    public static ProjectVisibility Instance { get; } = new();

    private ProjectVisibility()
    {
    }

    public static ScopeTree ScopeTreeOf(IEnumerable<Symbol> symbols) => ScopeTreeBuilder.Build(symbols, Instance);

    // the root of the project the code in a scope is of: its project scope's; null under no project.
    public static Uri? ProjectOf(LexicalScope scope) => scope.SelfAndAncestors().FirstOrDefault(each => each.Kind == LexicalScopeKind.Project)?.Uri;

    public static Uri? ProjectOf(ISessionSymbols symbols, Uri handle)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        return symbols.ScopeOf(handle) is { } scope ? ProjectOf(scope) : null;
    }

    public bool IsProjectVisible(Symbol member, VBModuleSymbol module)
    {
        ArgumentNullException.ThrowIfNull(member);

        return member is AccessibleTypedSymbol accessible && accessible.AccessModifier switch
        {
            AccessModifier.Private => false,
            AccessModifier.Public or AccessModifier.Global or AccessModifier.Friend => true,
            _ => member is not (VBModuleFieldVariableMemberSymbol or VBConstantMemberSymbol),
        };
    }

    public bool IsVisibleFrom(Symbol member, VBModuleSymbol module, Uri? originProject)
    {
        ArgumentNullException.ThrowIfNull(module);

        return originProject switch
        {
            null => false,
            _ when string.Equals(originProject.AbsoluteUri, module.WorkspaceRoot.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
                => IsProjectVisible(member, module),
            _ => ReferencedProjectSymbols.IsAccessible(member, module),
        };
    }
}
