using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.Expressions.Operators;

/// <summary>
/// A unary operator expression operates with a single <c>ValuedExpression</c> operand.
/// </summary>
public record class VBUnaryOperatorExpression(OperatorSymbol Symbol, Location Location, ValuedExpression Expression) 
    : VBOperatorExpression(Symbol, Location) { }
