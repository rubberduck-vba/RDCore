using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Facts;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// Reduces the bound of an array dimension to the integer it is: <c>ReDim</c>'s run-time expressions
/// (<strong>MS-VBAL §5.4.3.3</strong>) and the constant expressions of a fixed-size array's declaration
/// (<strong>§5.2.3.1.3</strong>) alike.
/// </summary>
/// <param name="Expressions">Evaluates the bound expression.</param>
/// <param name="LetCoercion">Let-coerces the bound to <c>Long</c>, the type a bound is.</param>
public sealed record class ArrayBoundEvaluator(
    RuntimeExpressionEvaluator Expressions,
    ILetCoercionRuntimeSemanticsProvider LetCoercion)
{
    /// <summary>
    /// Evaluates a bound.
    /// </summary>
    /// <remarks>
    /// "dynamic-lower-bound = integer-expression", whose value is "the value of its &lt;expression&gt; Let-coerced to Long"
    /// (<strong>MS-VBAL §5.6.16.4</strong>), as a fixed-size array's bounds are (<strong>§5.2.3.1.3</strong>): a Double bound rounds rather
    /// than being refused, and a bound past the range of an Integer is no overflow.
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
        => TryEvaluate(session, context, expression, out value, out _, out failure);

    /// <inheritdoc cref="TryEvaluate(IRuntimeSession, RuntimeEvaluationContext, ExpressionNode, out int, out RuntimeExecutionOutcome)"/>
    /// <param name="isKnown">Whether the bound is known. When it is not, <paramref name="value"/> is the one its type assumes in its place.</param>
    public bool TryEvaluate(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression,
        out int value, out bool isKnown, out RuntimeExecutionOutcome failure)
    {
        value = 0;
        isKnown = true;
        // a Single, Double or Date bound is converted to a Long from the extended format it is computed in (MS-VBAL §5.6.9.3; s42 E08).
        var evaluated = Expressions.EvaluateLetCoercedTo(session, expression, VBLongType.TypeInfo, context);
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
            DestinationTypeDesc = new(VBLongType.TypeInfo),
            Site = ConversionSite.Subscript,
        });

        if (!coerced.IsSuccess)
        {
            failure = RuntimeExecutionOutcome.Error(coerced.ErrorInfo!);
            return false;
        }

        value = Convert.ToInt32(coerced.Result!.Handle.Value.BoxedValue);
        isKnown = !coerced.Result.IsIndeterminate;
        failure = RuntimeExecutionOutcome.Next;
        return true;
    }
}
