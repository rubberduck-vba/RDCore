using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Semantics;

namespace RDCore.SDK.Model.AST.Expressions
{
    /// <summary>
    /// An <em>infix</em> <c>VBOperatorExpression</c> (bound) that accepts a <em>left</em> and a <em>right</em> operand on either of its sides.
    /// </summary>
    /// <remarks>
    /// Unless specified otherwise in a derived node type, <strong>MS-VBAL 5.6.9 Operator Expressions</strong> defines the static and run-time semantics of this node.
    /// </remarks>
    /// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
    /// <param name="Symbol">The <c>OperatorSymbol</c> associated with this <em>operator expression</em>.</param>
    /// <param name="ResultSymbol">The <see cref="OperatorExpressionValueSymbol"/> associated with the <em>result</em> of this <em>operator expression</em>.</param>
    /// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
    /// <param name="Left">The left-hand side (LHS) operand of this <em>binary operator expression</em></param>
    /// <param name="Right">The right-hand side (RHS) operand of this <em>binary operator expression</em></param>
    public record class VBBinaryOperatorExpression<TContext, TFlags>(Uri SemanticId, 
        OperatorSymbol<TContext, TFlags> Symbol, 
        OperatorExpressionValueSymbol ResultSymbol,
        Location Location, 
        BoundExpression Left, 
        BoundExpression Right) 
        : VBOperatorExpression<TContext, TFlags>(SemanticId, Symbol, ResultSymbol, Location) 
        where TContext : SemanticContext<TFlags>, new()
        where TFlags : struct, Enum { }
}
