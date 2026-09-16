using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a <c>For...Next</c> loop construct.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="ControlExpression">A numeric expression that resolves to the loop control variable.</param>
/// <param name="StartExpression">A numeric expression that evaluates to the initial value of the loop counter.</param>
/// <param name="EndExpression">A numeric expression that evaluates to the final value of the loop counter.</param>
/// <param name="StepExpression">A numeric expression that evaluates to the iteration increment of the control variable, or <c>null</c> when the loop declares no <c>Step</c> clause (MS-VBAL's implicit default of <c>1</c> is a runtime concern, not a syntax one).</param>
/// <param name="Body">The executable statements in the body of the loop.</param>
public record class ForStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode ControlExpression, ExpressionNode StartExpression, ExpressionNode EndExpression, ExpressionNode? StepExpression, StatementBlock Body)
    : StatementNode(Identity, SourceLocation, StepExpression is null ? [ControlExpression, StartExpression, EndExpression] : [ControlExpression, StartExpression, EndExpression, StepExpression]);

