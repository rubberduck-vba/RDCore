using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.LanguageCore.Flow;

/// <summary>
/// What is certain about a branch from the way it is written: a condition that is the literal <c>True</c> or <c>False</c>, and a <c>For</c> loop whose bounds are literals.
/// </summary>
/// <remarks>
/// Only a literal is looked into. A constant, or an expression that reduces to one, is a value the static pass does not fold, and a branch that depends on one is
/// taken to go either way.
/// </remarks>
internal static class ConstantFlow
{
    /// <summary>
    /// The outcome that is certain for the condition of a <see cref="InstructionKind.ConditionalBranch"/> (<see langword="true"/>: control falls through into the block it
    /// guards) or of a <see cref="InstructionKind.LoopBack"/> (<see langword="true"/>: control goes back to the start of the loop), or <see langword="null"/> when
    /// it is not certain.
    /// </summary>
    public static bool? OutcomeOf(Instruction instruction)
    {
        var (condition, negated) = instruction.Node switch
        {
            IfBlockStatementNode node => (node.ConditionExpression, false),
            ElseIfBlockStatementNode node => (node.ConditionExpression, false),
            InlineIfStatementNode node => (node.ConditionExpression, false),
            WhileWendStatementNode node => (node.ConditionExpression, false),
            DoWhileLoopStatementNode node => (node.ConditionExpression, false),
            DoLoopWhileStatementNode node => (node.ConditionExpression, false),
            DoUntilLoopStatementNode node => (node.ConditionExpression, true),
            DoLoopUntilStatementNode node => (node.ConditionExpression, true),
            _ => (null, false),
        };

        return condition is LiteralExpressionNode { StaticValue: VBBooleanValue literal }
            ? (literal.Value.StoredValue != 0) != negated
            : null;
    }

    /// <summary>
    /// Whether the body of a <c>For</c> loop certainly runs at least once (<see langword="true"/>) or certainly never runs (<see langword="false"/>), when its bounds say
    /// so; <see langword="null"/> when they do not.
    /// </summary>
    public static bool? RunsAtLeastOnce(Instruction opener)
    {
        if (opener.Node is not ForStatementNode loop
            || !TryGetNumber(loop.StartExpression, out var start)
            || !TryGetNumber(loop.EndExpression, out var end))
        {
            return null;
        }

        var step = 1d;
        if (loop.StepExpression is { } stepExpression && !TryGetNumber(stepExpression, out step))
        {
            return null;
        }

        return step switch
        {
            > 0 => start <= end,
            < 0 => start >= end,
            _ => null,
        };
    }

    private static bool TryGetNumber(ExpressionNode expression, out double number)
    {
        switch (expression)
        {
            case LiteralExpressionNode { StaticValue: VBNumericTypedValue literal }:
                number = literal.AsDouble;
                return true;

            case VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp } negation when negation.Children is [ExpressionNode operand] && TryGetNumber(operand, out var operandValue):
                number = -operandValue;
                return true;

            default:
                number = 0;
                return false;
        }
    }
}
