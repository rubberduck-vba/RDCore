using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.Expressions.Operators;

/// <summary>
/// Represents any operator expression; a valued expression that is associated to an <c>OperatorSymbol</c>.
/// </summary>
public abstract record class VBOperatorExpression(OperatorSymbol Symbol, Location Location) : ValuedExpression(Location) { }