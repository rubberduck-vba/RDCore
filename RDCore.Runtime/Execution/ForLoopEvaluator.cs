using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Semantics;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.Plans;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Facts;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Evaluates a <c>For</c> loop's own operand expressions, counter assignment, increment, and range test —
/// <strong>MS-VBAL §5.4.2.3</strong>.
/// </summary>
/// <remarks>
/// Every step is the real runtime semantics VBA source itself would go through — Let-assignment, addition, the
/// relational operators (<see cref="IOperatorRuntimeSemanticsProvider"/>) — never a hand-rolled counter/bound
/// comparison. The location-bearing node passed to each is always a real node the loop already has: the loop's
/// own counter expression, or (for the counter's initial assignment) the loop's own start expression — never a
/// synthetic stand-in.
/// </remarks>
public sealed class ForLoopEvaluator(
    RuntimeExpressionEvaluator expressionEvaluator, IOperatorRuntimeSemanticsProvider operators, ILetCoercionRuntimeSemanticsProvider letCoercion)
{
    /// <summary>
    /// Evaluates a single operand expression (<c>start-value</c>, <c>end-value</c>, <c>step-increment</c>).
    /// </summary>
    public RuntimeSemanticsEvaluationResult EvaluateOperand(IRuntimeSession session, ExpressionNode expression, RuntimeEvaluationContext context)
        => expressionEvaluator.Evaluate(session, expression, context);

    /// <summary>
    /// Steps the loop on payloads, when its counter holds one (IR lowering S1.2): what <see cref="Increment"/>, <see cref="AssignCounter"/> and
    /// <see cref="IsOutOfRange"/> do, with no value made of the counter.
    /// </summary>
    /// <returns><see langword="false"/> when the loop is to be stepped the way it always is.</returns>
    public bool TryStepUnboxed(IRuntimeSession session, ForLoopState state, RuntimeEvaluationContext context, out bool isOutOfRange)
        => expressionEvaluator.TryStepForUnboxed(session, state, context, out isOutOfRange);

    /// <summary>
    /// The start, the end and the step of a loop as it counts with them: numbers of the one type the loop counts in (<strong>MS-VBAL §5.4.2.3</strong>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// "If the value of &lt;start-value&gt;, &lt;end-value&gt;, and &lt;step-increment&gt; are not Let-coercible to Double, error 13 (Type mismatch) is
    /// raised immediately" - a <c>Null</c> raising what Let-coercing it raises, 94, as in MS-VBA. The specification then proceeds "using the original,
    /// uncoerced values", and compares the counter with the end as numbers: the comparison of a number with a <c>String</c> (<strong>§5.6.9.5</strong>),
    /// which a number is always less than, would never end <c>For i = 1 To "40"</c>, which MS-VBA counts 40 times.
    /// </para>
    /// <para>
    /// What MS-VBA counts in, recorded in Excel: a counter of a declared type counts in that type, and its start, end and step are Let-coerced to it
    /// (<c>For k = 1 To 2.6</c> counts to 3 with an <c>Integer</c>). A <c>Variant</c> counter counts in the widest type of its start, end and step - <c>Byte</c>,
    /// then <c>Integer</c> (a <c>Boolean</c> too), <c>Long</c>, <c>LongLong</c>, <c>Double</c> (a <c>Single</c> and a <c>String</c> too), <c>Currency</c>, then
    /// <c>Date</c> and <c>Decimal</c>, of which the end's type wins over the start's. <c>Empty</c> has no type here, and nor does the step a loop that
    /// writes none counts with.
    /// </para>
    /// </remarks>
    /// <param name="session">The session the loop runs in.</param>
    /// <param name="forStatement">The loop: its start, end and step expressions are where their coercions are stated, and their errors raised.</param>
    /// <param name="counterType">The declared type of the counter.</param>
    /// <param name="start">The start value, as it was evaluated.</param>
    /// <param name="end">The end value, as it was evaluated.</param>
    /// <param name="step">The step, as it was evaluated; <see langword="null"/> when the loop writes none.</param>
    /// <param name="counted">The start, the end and the step, in the type the loop counts in.</param>
    /// <returns>The error one of them raised, or <see langword="null"/>.</returns>
    public IVBRaisableError? Operands(IRuntimeSession session, ForStatementNode forStatement, VBType counterType,
        VBTypedValue start, VBTypedValue end, VBTypedValue? step, out (VBTypedValue Start, VBTypedValue End, VBTypedValue Step) counted)
    {
        counted = default;
        VBTypedValue?[] values = [start, end, step];
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] is not { } value)
            {
                continue;
            }

            // an object is the value of its default member, which is what has a type to count in.
            var read = ObjectDefaultMember.ValueOf(session, value);
            if (!read.IsSuccess)
            {
                return read.ErrorInfo ?? VBRuntimeErrorInfo.For(VBRuntimeErrorId.InternalError, forStatement.SourceLocation, Exceptions.VBForLoopOperandIsNotANumber_Verbose);
            }

            values[index] = read.Result!;
        }

        var loopType = LoopTypeOf(counterType, values[0]!, values[1]!, values[2]);

        // MS-VBAL 5.4.2.3: "If no <step-clause> is present, the <step-increment> value is the integer data value 1."
        values[2] ??= new VBIntegerValue(1);
        ExpressionNode[] expressions = [forStatement.StartExpression, forStatement.EndExpression, forStatement.StepExpression ?? forStatement.ControlExpression];
        var inLoopType = new VBTypedValue[3];
        for (var index = 0; index < values.Length; index++)
        {
            // a loop that counts in Date reads a String as a date only, not as the number a Let-coercion would also read it as: "1/1/1900" is a start,
            // "1" is a type mismatch (MS-VBA, recorded in Excel).
            if (loopType is VBDateType && values[index] is VBStringValue text && !ValueConversions.TryInterpretDate(text.Value, out _, session.Environment.Regional))
            {
                return VBRuntimeErrorInfo.For(VBRuntimeErrorId.TypeMismatch, expressions[index].Location,
                    string.Format(Exceptions.VBForLoopOperandNotOfLoopType_Verbose, loopType.Name));
            }

            // a value that is no number - "abc", Null, an Error, an array - is what Let-coercing it to the loop's type raises: 13, or 94 for a Null.
            var frame = new LetCoercionStackFrame(expressions[index].Identity, InputIndex.CoercionSourceValue, values[index]!, new VBTypeDescValue(loopType), ConversionSite.LoopBound);
            var coerced = letCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, expressions[index], frame);
            if (!coerced.IsSuccess)
            {
                return coerced.ErrorInfo
                    ?? VBRuntimeErrorInfo.For(VBRuntimeErrorId.InternalError, expressions[index].Location, Exceptions.VBForLoopOperandIsNotANumber_Verbose);
            }

            inLoopType[index] = coerced.Result!;
        }

        counted = (inLoopType[0], inLoopType[1], inLoopType[2]);
        return null;
    }

    /// <summary>
    /// Whether a loop's step is negative, which is the way it counts: down to its end, rather than up to it.
    /// </summary>
    /// <param name="step">The step, in the type the loop counts in (<see cref="Operands"/>).</param>
    public static bool CountsDown(VBTypedValue step)
        => ValueConversions.ToNumeric(step, VBDoubleType.TypeInfo) is { IsSuccess: true, Value: VBDoubleValue { Value: < 0 } };

    // the type the loop counts in: the counter's declared type, or what a Variant counter takes from the start, the end and the step (see Operands).
    private static VBType LoopTypeOf(VBType counterType, VBTypedValue start, VBTypedValue end, VBTypedValue? step)
    {
        switch (counterType)
        {
            case VBNumericType numeric:
                return numeric;
            case VBDateType:
                return counterType;
            case VBEnumType:
                return VBLongType.TypeInfo;
            case VBLongPtrType_x64:
                return VBLongLongType.TypeInfo;
            case VBLongPtrType_x86:
                return VBLongType.TypeInfo;
        }

        (int Rank, VBType Type)? widest = null;
        foreach (var (value, isEnd, isStep) in new[] { (start, false, false), (end, true, false), (step, false, true) })
        {
            if (value is null || RankOf(value) is not { } ranked)
            {
                continue;
            }

            // a Date and a Decimal are of a rank: the end's type wins over the start's, and the step's over neither.
            if (widest is not { } current || ranked.Rank > current.Rank || (ranked.Rank == current.Rank && isEnd && !isStep))
            {
                widest = ranked;
            }
        }

        return widest?.Type ?? VBByteType.TypeInfo;
    }

    private static (int Rank, VBType Type)? RankOf(VBTypedValue value) => value.TypeInfo switch
    {
        VBEmptyType => null,
        VBByteType => (1, VBByteType.TypeInfo),
        VBBooleanType or VBIntegerType => (2, VBIntegerType.TypeInfo),
        VBLongType or VBLongPtrType_x86 => (3, VBLongType.TypeInfo),
        VBLongLongType or VBLongPtrType_x64 => (4, VBLongLongType.TypeInfo),
        VBSingleType or VBDoubleType or VBStringType => (5, VBDoubleType.TypeInfo),
        VBCurrencyType => (6, VBCurrencyType.TypeInfo),
        VBDateType => (7, VBDateType.TypeInfo),
        VBDecimalType => (7, VBDecimalType.TypeInfo),
        _ => (5, VBDoubleType.TypeInfo),
    };

    /// <summary>
    /// Let-assigns the loop's counter symbol to <paramref name="value"/>, returning its own (possibly
    /// coerced) resulting value — MS-VBAL comparisons and the next increment both operate on this, never
    /// on the raw <paramref name="value"/> that was assigned.
    /// </summary>
    /// <remarks>
    /// The counter is assigned through the slot its name is bound to when it can be (IR lowering S0.8c); an assignment that cannot - or that fails, which
    /// raises its error at <paramref name="locationNode"/> - is the let-assignment operation it always was.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult AssignCounter(
        IRuntimeSession session, ForLoopState state, ExpressionNode locationNode, VBTypedValue value, RuntimeEvaluationContext context)
    {
        // a counter of a type no plan converts to - a Variant - is not looked for on every iteration only to be declined.
        if (state.Counter is ITypedSymbol { ResolvedType: var counterType } && PlanExecutor.IsPlannedDestination(counterType)
            && expressionEvaluator.TryAssignBound(session, state.ControlExpression, value, context) is { } assigned)
        {
            return assigned;
        }

        // every Next assigns the counter at its own expression, with the assignment the loop made once; the start value is assigned at its own.
        var assignment = ReferenceEquals(locationNode, state.ControlExpression)
            ? state.CounterAssignment
            : new VBBinaryOperatorExpressionNode(OperatorSymbolNames.BinaryAssignmentValueOp, locationNode.Identity, locationNode.Location, locationNode, locationNode);
        return operators.EvaluateBinaryOperator(session, assignment, state.CounterTarget, value);
    }

    /// <summary>
    /// <c>counter + step</c> (<strong>MS-VBAL §5.6.9.3</strong>) — the value to Let-assign back to the
    /// counter on every <c>Next</c>.
    /// </summary>
    public RuntimeSemanticsEvaluationResult Increment(IRuntimeSession session, ForLoopState state, VBTypedValue counter)
        => operators.EvaluateBinaryOperator(session, Tokens.AdditionOp, state.ControlExpression, counter, state.Step, ConversionSite.LoopBound);

    /// <summary>
    /// Whether the loop has run out of range and should complete — steps 1/2 of the algorithm: a
    /// non-negative <c>step-increment</c> completes when the counter exceeds <c>end-value</c>; a negative
    /// one completes when the counter falls below it.
    /// </summary>
    public RuntimeSemanticsEvaluationResult IsOutOfRange(IRuntimeSession session, ForLoopState state, VBTypedValue counter)
        => operators.EvaluateBinaryOperator(session,
            state.CountsDown ? Tokens.CompareLessThanOp : Tokens.CompareGreaterThanOp,
            state.ControlExpression, counter, state.End, ConversionSite.LoopBound);
}
