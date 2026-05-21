using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Semantics.Runtime.Expressions;
using RDCore.SDK.Semantics.Static.Expressions;

namespace RDCore.SDK.Model.Expressions;

/// <summary>
/// Represents a <em>literal expression</em>, which statically resolves a <c>VBTypedValue</c> directly from the syntax tree.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL 3.3 Lexical Tokens</strong> static semantics being implemented at the parser level,
/// the <c>VBTypedValue</c> has already resolved its <c>type-suffix</c> ("type hint").
/// </remarks>
public sealed record class LiteralExpression(Location Location) : ValuedExpression(Location)
{
    /// <summary>
    /// Creates a new <c>LiteralExpression</c> at the specified location, with the specified value.
    /// </summary>
    /// <param name="location">The document location of the expression.</param>
    /// <param name="value">The literal value of the expression.</param>
    public LiteralExpression(Location location, VBTypedValue value) : this(location)
    {
        ResolvedValue = value;
        ResolvedType = value.TypeInfo;
    }
}
