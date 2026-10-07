using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// Reduces the bound of an array dimension to the integer it is: <c>ReDim</c>'s run-time expressions
/// (<strong>MS-VBAL §5.4.3.3</strong>) and the constant expressions of a fixed-size array's declaration
/// (<strong>§5.2.3.1.3</strong>) alike.
/// </summary>
/// <param name="Expressions">Evaluates the bound expression.</param>
/// <param name="LetCoercion">Let-coerces the bound to <c>Integer</c>, the type a subscript is.</param>
public sealed record class ArrayBoundEvaluator(
    RuntimeExpressionEvaluator Expressions,
    ILetCoercionRuntimeSemanticsProvider LetCoercion)
{
    /// <summary>
    /// Evaluates a bound.
    /// </summary>
    /// <remarks>
    /// "dynamic-lower-bound = integer-expression" - a bound is Let-coerced to Integer like any other subscript, so a Double bound
    /// rounds rather than being refused.
    /// </remarks>
    /// <param name="session">The session the expression is evaluated in.</param>
    /// <param name="context">The scope its names resolve from.</param>
    /// <param name="expression">The bound.</param>
    /// <param name="value">What it came to.</param>
    /// <param name="failure">What stopped it, when it could not be evaluated.</param>
    /// <returns><see langword="false"/> when it could not be evaluated.</returns>
    public bool TryEvaluate(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression,
        out int value, out RuntimeExecutionOutcome failure)
    {
        value = 0;
        var evaluated = Expressions.Evaluate(session, expression, context);
        if (!evaluated.IsSuccess)
        {
            failure = evaluated.IsInternalError
                ? RuntimeExecutionOutcome.InternalError
                : RuntimeExecutionOutcome.Error(evaluated.ErrorInfo!);
            return false;
        }

        var coerced = LetCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, expression, new()
        {
            NodeId = expression.Identity,
            SourceValue = evaluated.Result!,
            DestinationTypeDesc = new(VBIntegerType.TypeInfo),
        });

        if (!coerced.IsSuccess)
        {
            failure = RuntimeExecutionOutcome.Error(coerced.ErrorInfo!);
            return false;
        }

        value = Convert.ToInt32(coerced.Result!.Handle.Value.BoxedValue);
        failure = RuntimeExecutionOutcome.Next;
        return true;
    }
}
