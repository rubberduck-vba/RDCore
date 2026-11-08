using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Model.Symbols;

namespace RDCore.Runtime.LanguageCore.Services;

/// <summary>
/// Chains <see cref="ISymbolResolver"/>s: <see cref="ResolveValue"/> tries each in order and returns the
/// first result that is not <see cref="SymbolResolutionResult.IsUnbound"/>: a bound symbol, or an
/// error result (a duplicate or ambiguous name found by an earlier resolver is not masked by a later
/// fallback). The result is unbound only when every resolver is unbound.
/// </summary>
/// <remarks>
/// A compile-time composite: it binds names, and holds no storage. Used to layer a workspace's
/// <see cref="ScopeTreeSymbolResolver"/> over an intrinsic type-name resolver, most specific first.
/// </remarks>
/// <param name="resolvers">The resolvers to try, in priority order.</param>
public sealed class CompositeSymbolResolver(params ISymbolResolver[] resolvers) : ISymbolResolver
{
    /// <inheritdoc/>
    public SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle)
        => FirstBound(resolver => resolver.ResolveValue(name, scope, handle));

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle)
        => FirstBound(resolver => resolver.ResolveType(name, scope, handle));

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle)
        => FirstBound(resolver => resolver.ResolveQualifier(name, scope, handle));

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveProjectType(VBProjectSymbol project, string name)
        => FirstBound(resolver => resolver.ResolveProjectType(project, name));

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveConditionalConstant(string name, ScopeKind scope, Uri handle)
        => FirstBound(resolver => resolver.ResolveConditionalConstant(name, scope, handle));

    /// <inheritdoc/>
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
