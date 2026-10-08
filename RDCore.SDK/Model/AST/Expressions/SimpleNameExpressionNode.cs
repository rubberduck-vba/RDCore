using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6.10 Simple Name Expression</strong><br/>
/// A <see cref="ExpressionNode"/> that statically resolves an expression consisting of a single identifier without any <em>qualifiers</em> or <em>arguments</em>.
/// </summary>
/// <remarks>
/// It is also the name of a member, after the dot of a member access.
/// <para>
/// 👉 The <em>type-declaration character</em> a name is written with is part of what was written, and of what it means: <c>Environ$</c> is not <c>Environ</c>. They are
/// two members of the library with two signatures (a <c>String</c> and a <c>Variant</c>), and a name that lost its <c>$</c> on the way to being resolved would be
/// given the type of the one it is not. See <see cref="LookupNames"/>.
/// </para>
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="IdentifierName">The parsed <em>identifier name</em>, without its type-declaration character.</param>
/// <param name="TypeHint">The type-declaration character the name is written with (<c>%</c>, <c>&amp;</c>, <c>^</c>, <c>!</c>, <c>#</c>, <c>@</c> or <c>$</c>), or <see langword="null"/> when it is written with none.</param>
public sealed record class SimpleNameExpressionNode(SyntaxNodeId Identity, SourceLocation Location, string IdentifierName, string? TypeHint = null)
    : ExpressionNode(Identity, Location, [])
{
    /// <summary>
    /// The name as it was written: the identifier name followed by its type-declaration character, if it has one.
    /// </summary>
    public string WrittenName => IdentifierName + TypeHint;

    /// <summary>
    /// The names a resolver is asked for, in the order it is asked.
    /// </summary>
    /// <remarks>
    /// A name written with a type-declaration character is looked for as written first, because a member of the library can be named with one (<c>Left$</c>,
    /// <c>Environ$</c>), and is a different member from the one without it. Failing that it is the declaration of that name, which a variable declared
    /// <c>Dim n%</c> is: its symbol is <c>n</c>, and <c>%</c> is how its type was said.
    /// </remarks>
    public IEnumerable<string> LookupNames
    {
        get
        {
            if (TypeHint is { Length: > 0 })
            {
                yield return WrittenName;
            }

            yield return IdentifierName;
        }
    }
}
