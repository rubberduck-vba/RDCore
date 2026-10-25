using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Resolves a name the way it was written, type-declaration character and all.
/// </summary>
/// <remarks>
/// <c>Environ$</c> and <c>Environ</c> are two members of the library, one that answers a <c>String</c> and one that answers a <c>Variant</c>, and a call that
/// resolved the first to the second would be analysed, and run, with the wrong type. A simple name finds the library's member named with the character whenever its
/// identifier binds a member of the library, or nothing (<see cref="ResolveValue"/>); a member after a dot is looked for as it was written first, and only when
/// the owner has no member of that name is it the member of the identifier (<see cref="ResolveMember"/>).
/// </remarks>
public static class SymbolResolverNameExtensions
{
    /// <summary>
    /// Resolves a name written as <paramref name="name"/>, as <see cref="ISymbolResolver.ResolveValue"/> would.
    /// </summary>
    /// <remarks>
    /// <strong>MS-VBAL §5.6.10</strong> picks "the first tier where the name value of &lt;name&gt; matches", and only then judges the character: "If &lt;name&gt;
    /// specifies a type character, and this type character's associated type does not match the declared type of the match, the simple name expression is
    /// invalid." The character is not part of the name being matched, so a typed name binds through the same tiers its identifier does: a local, a parameter, a
    /// field or a procedure named <c>Left</c> is what <c>Left$</c> means wherever it is what <c>Left</c> means.
    /// <para>
    /// The library is where that is not the whole answer. It declares its <c>$</c> functions as members of their own, named with the character (<c>Hex$</c>
    /// beside <c>Hex</c>), each returning a <c>String</c> where its twin returns a <c>Variant</c>. So when the identifier binds a member of the library, or binds
    /// nothing at all, the name as written binds the library's member named with the character, if there is one; when there is none, it is the identifier's
    /// result - which is how <c>n%</c> finds the variable <c>n</c>. An ambiguous or duplicated identifier is an error, and is the answer as it stands.
    /// </para>
    /// <para>
    /// 👉 Only the library names a member with the character in it: a workspace declaration's name is its identifier without one (<c>Dim s$</c> declares
    /// <c>s</c>), so the name as written can only ever find a library member. A library constant carries no <see cref="SymbolProperties.ExternalTarget"/>, so
    /// the identifier's constant is the answer (<c>vbCrLf$</c> is <c>vbCrLf</c>). The workspace's own names outrank the library's at the project tier, so a
    /// public <c>Left</c> of another module is what <c>Left$</c> means too. A member of a referenced library carries an
    /// <see cref="SymbolProperties.ExternalTarget"/> as well, but it is that library's (<see cref="SymbolProperties.Library"/>), not the standard library's,
    /// and its twins are not the standard library's to stand in for it: it is the answer as it stands.
    /// </para>
    /// </remarks>
    /// <param name="resolver">The resolver.</param>
    /// <param name="name">The name, with the type-declaration character it was written with.</param>
    /// <param name="scope">The scope the name is written in.</param>
    /// <param name="handle">The <see cref="Symbol.Uri"/> the name is resolved from.</param>
    public static SymbolResolutionResult ResolveValue(this ISymbolResolver resolver, SimpleNameExpressionNode name, ScopeKind scope, Uri handle)
    {
        var bare = resolver.ResolveValue(name.IdentifierName, scope, handle);
        // a member of the standard library is the only one that declares twins; a referenced library's, and the workspace's declarations, are an answer as they stand.
        if (name.TypeHint is not { Length: > 0 } || bare.IsError || bare.IsResolved && !StdLibSymbolProvider.IsLibraryMember(bare.Symbol!))
        {
            return bare;
        }

        var written = resolver.ResolveValue(name.WrittenName, scope, handle);
        return written.IsUnbound ? bare : written;
    }

    /// <summary>
    /// Resolves a member written as <paramref name="member"/> of <paramref name="owner"/>, as <see cref="ISymbolResolver.ResolveMember"/> would.
    /// </summary>
    /// <remarks>
    /// A member written with a type-declaration character is looked for as written first, because a member of the library can be named with one
    /// (<c>Strings.Left$</c>), and is a different member from the one without it. Failing that it is the member of the identifier, which a field declared
    /// <c>Public n%</c> is: its symbol is <c>n</c>, and <c>%</c> is how its type was said. When neither is declared, the answer is that the identifier is not.
    /// </remarks>
    /// <param name="resolver">The resolver.</param>
    /// <param name="owner">The symbol whose member it is.</param>
    /// <param name="member">The name of the member, with the type-declaration character it was written with.</param>
    /// <param name="handle">The <see cref="Symbol.Uri"/> the member is resolved from.</param>
    public static SymbolResolutionResult ResolveMember(this ISymbolResolver resolver, Symbol owner, SimpleNameExpressionNode member, Uri handle)
    {
        if (member.TypeHint is { Length: > 0 } && resolver.ResolveMember(owner, member.WrittenName, handle) is { IsUnbound: false } written)
        {
            return written;
        }

        return resolver.ResolveMember(owner, member.IdentifierName, handle);
    }
}
