using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a <c>Do... Loop While</c> construct.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="ConditionExpression">An object or variant expression that controls loop entry and continuation.</param>
/// <param name="Body">The executable statements in the body of the loop.</param>
/// <remarks>
/// This loop construct exits when the <c>ConditionExpression</c> evaluates to <c>False</c>.
/// </remarks>
public record DoLoopWhileStatement(Uri SemanticId, Location Location, BoundExpression ConditionExpression, StatementBlock Body)
    : BoundStatement(SemanticId, Location, $"{Tokens.Loop}-{Tokens.While}", [ConditionExpression]);

