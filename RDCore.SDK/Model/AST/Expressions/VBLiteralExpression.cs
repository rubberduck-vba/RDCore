using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.AST.Expressions
{
    /// <summary>
    /// A <c>BoundExpression</c> that statically resolves a <c>VBTypedValue</c> directly from the source tokens.
    /// </summary>
    /// <remarks>
    /// Unless specified otherwise in a derived node type, <strong>MS-VBAL 5.6.5 Literal Expressions</strong> defines the static and run-time 
    /// semantics of this node. <em>MS-VBAL 3.3 Lexical Tokens</em> static semantics being implemented at the parser level,
    /// the <c>VBTypedValue</c> has already resolved its <c>type-suffix</c> ("type hint").
    /// </remarks>
    /// <param name="SemanticId">The unique <c>Uri</c> identifying this specific expression node.</param>
    /// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
    /// <param name="StaticValue">The parsed literal value.</param>
    public sealed record class VBLiteralExpression(Uri SemanticId, Location Location, VBTypedValue StaticValue) 
        : BoundExpression(SemanticId, Location) { }
}
