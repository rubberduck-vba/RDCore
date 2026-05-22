using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.AST.Operators;

/// <summary>
/// A unary operator expression operates with a single <c>ValuedExpression</c> operand.
/// </summary>
public record class VBUnaryOperatorExpression(OperatorSymbol Symbol, ValuedExpression Expression, Location Location)
    : VBOperatorExpression(Symbol, Location) { }
