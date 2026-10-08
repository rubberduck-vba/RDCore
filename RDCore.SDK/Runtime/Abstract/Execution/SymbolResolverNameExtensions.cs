using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Resolves a name the way it was written, type-declaration character and all.
/// </summary>
/// <remarks>
/// <c>Environ$</c> and <c>Environ</c> are two members of the library, one that answers a <c>String</c> and one that answers a <c>Variant</c>, and a call that
/// resolved the first to the second would be analysed, and run, with the wrong type. The name is therefore looked for as it was written first
/// (<see cref="SimpleNameExpressionNode.LookupNames"/>); and only when the library has no member of that name is it the declaration of the identifier, as a variable
/// declared <c>Dim n%</c> is.
/// </remarks>
public static class SymbolResolverNameExtensions
{
    /// <summary>
    /// Resolves a name written as <paramref name="name"/>, as <see cref="ISymbolResolver.ResolveValue"/> would.
    /// </summary>
    /// <param name="resolver">The resolver.</param>
    /// <param name="name">The name, with the type-declaration character it was written with.</param>
    /// <param name="scope">The scope the name is written in.</param>
    /// <param name="handle">The <see cref="Symbol.Uri"/> the name is resolved from.</param>
    public static SymbolResolutionResult ResolveValue(this ISymbolResolver resolver, SimpleNameExpressionNode name, ScopeKind scope, Uri handle)
        => ResolveFirst(name, lookup => resolver.ResolveValue(lookup, scope, handle));

    /// <summary>
    /// Resolves a member written as <paramref name="member"/> of <paramref name="owner"/>, as <see cref="ISymbolResolver.ResolveMember"/> would.
    /// </summary>
    /// <param name="resolver">The resolver.</param>
    /// <param name="owner">The symbol whose member it is.</param>
    /// <param name="member">The name of the member, with the type-declaration character it was written with.</param>
    /// <param name="handle">The <see cref="Symbol.Uri"/> the member is resolved from.</param>
    public static SymbolResolutionResult ResolveMember(this ISymbolResolver resolver, Symbol owner, SimpleNameExpressionNode member, Uri handle)
        => ResolveFirst(member, lookup => resolver.ResolveMember(owner, lookup, handle));

    // the first name that is anything but unbound; and if none is, the last answer there was, which is that the bare name is not declared.
    private static SymbolResolutionResult ResolveFirst(SimpleNameExpressionNode name, Func<string, SymbolResolutionResult> resolve)
    {
        var result = default(SymbolResolutionResult);
        foreach (var lookup in name.LookupNames)
        {
            result = resolve(lookup);
            if (!result.IsUnbound)
            {
                return result;
            }
        }

        return result;
    }
}
