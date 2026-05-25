using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// A <c>BoundExpression</c> that is associated to an <c>OperatorSymbol</c>.
/// </summary>
/// <remarks>
/// Unless specified otherwise in a derived node type, <strong>MS-VBAL 5.6.9 Operator Expressions</strong> defines the static and run-time semantics of this node.
/// </remarks>
/// <param name="Symbol">The <c>OperatorSymbol</c> associated with this <em>operator expression</em>.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
public abstract record class VBOperatorExpression(OperatorSymbol Symbol, Location Location) 
    : BoundExpression(Location) { }