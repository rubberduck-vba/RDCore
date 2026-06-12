using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics;

namespace RDCore.SDK.Model.AST.Expressions
{
    /// <summary>
    /// A <em>prefix</em> <c>VBOperatorExpression</c> that accepts a single operand.
    /// </summary>
    /// <remarks>
    /// Unless specified otherwise in a derived node type, <strong>MS-VBAL 5.6.9 Operator Expressions</strong> defines the static and run-time semantics of this node.
    /// </remarks>
    /// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
    /// <param name="Symbol">The <c>OperatorSymbol</c> associated with this <em>operator expression</em>.</param>
    /// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
    /// <param name="Expression">The operand of this <em>unary operator expression</em></param>
    public record class VBUnaryOperatorExpression<TContext, TFlags>(Uri SemanticId, OperatorSymbol<TContext, TFlags> Symbol, Location Location, VBOperatorExpression<TContext, TFlags> Expression) 
        : VBOperatorExpression<TContext, TFlags>(SemanticId, Symbol, Expression.ResultSymbol, Location) 
        where TContext : SemanticContext<TFlags>, new()
        where TFlags : struct, Enum
    { }
}
