using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a <c>For...Next</c> loop construct.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="ControlExpression">A numeric expression that resolves to the loop control variable.</param>
/// <param name="StartExpression">A numeric expression that evaluates to the initial value of the loop counter.</param>
/// <param name="EndExpression">A numeric expression that evaluates to the final value of the loop counter.</param>
/// <param name="StepExpression">A numeric expression that evaluates to the iteration increment of the control variable.</param>
/// <param name="Body">The executable statements in the body of the loop.</param>
public record class ForStatement(Uri SemanticId, Location Location, BoundExpression ControlExpression, BoundExpression StartExpression, BoundExpression EndExpression, BoundExpression StepExpression, StatementBlock Body)
    : BoundStatement(SemanticId, Location, $"{Tokens.For}-{Tokens.Next}", [ControlExpression, StartExpression, EndExpression, StepExpression]);

