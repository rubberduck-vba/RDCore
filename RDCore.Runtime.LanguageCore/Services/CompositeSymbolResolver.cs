using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Model.Symbols;

namespace RDCore.Runtime.LanguageCore.Services;

public sealed class CompositeSymbolResolver(params ISymbolResolver[] resolvers) : ISymbolResolver
{
    public SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle)
        => FirstBound(resolver => resolver.ResolveValue(name, scope, handle));

    public SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle)
        => FirstBound(resolver => resolver.ResolveType(name, scope, handle));

    public SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle)
        => FirstBound(resolver => resolver.ResolveQualifier(name, scope, handle));

    public SymbolResolutionResult ResolveProjectType(VBProjectSymbol project, string name)
        => FirstBound(resolver => resolver.ResolveProjectType(project, name));

    public SymbolResolutionResult ResolveConditionalConstant(string name, ScopeKind scope, Uri handle)
        => FirstBound(resolver => resolver.ResolveConditionalConstant(name, scope, handle));

    public SymbolResolutionResult ResolveMember(Symbol owner, string name, Uri handle)
        => FirstBound(resolver => resolver.ResolveMember(owner, name, handle));

    private SymbolResolutionResult FirstBound(Func<ISymbolResolver, SymbolResolutionResult> resolve)
    {
        foreach (var resolver in resolvers)
        {
            var result = resolve(resolver);
            if (!result.IsUnbound)
            {
                return result;
            }
        }

        return SymbolResolutionResult.Unbound;
    }
}
