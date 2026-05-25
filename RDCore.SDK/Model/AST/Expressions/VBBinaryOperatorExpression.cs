using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// An <em>infix</em> <c>VBOperatorExpression</c> that accepts a <em>left</em> and a <em>right</em> operand on either of its sides.
/// </summary>
/// <remarks>
/// Unless specified otherwise in a derived node type, <strong>MS-VBAL 5.6.9 Operator Expressions</strong> defines the static and run-time semantics of this node.
/// </remarks>
/// <param name="Symbol">The <c>OperatorSymbol</c> associated with this <em>operator expression</em>.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
/// <param name="Left">The left-hand side (LHS) operand of this <em>binary operator expression</em></param>
/// <param name="Right">The right-hand side (RHS) operand of this <em>binary operator expression</em></param>
public record class VBBinaryOperatorExpression(OperatorSymbol Symbol, Location Location, BoundExpression Left, BoundExpression Right) 
    : VBOperatorExpression(Symbol, Location) { }
