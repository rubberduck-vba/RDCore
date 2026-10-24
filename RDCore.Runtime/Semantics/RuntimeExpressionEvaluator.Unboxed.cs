using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Plans;

namespace RDCore.Runtime.Semantics;

// IR lowering S1.2: expressions of numeric scalar variables evaluated on payloads (ScalarPayload), with no value made of what they evaluate to. A node the
// payload evaluation does not do - or one that would raise an error - declines it, and the boxed evaluation evaluates the expression as it always has: the
// nodes it does are literals, variables and operators, which can be evaluated again with no other effect, so the error the boxed evaluation raises is the
// one the expression raises.
public sealed partial class RuntimeExpressionEvaluator
{
    /// <summary>
    /// Let-assigns <paramref name="value"/> to <paramref name="target"/> on payloads, when the target is a variable that holds a payload and the value is an
    /// expression evaluated on them.
    /// </summary>
    /// <returns><see langword="false"/> when the assignment is not done here, and is to be done the way it always is.</returns>
    internal bool TryAssignUnboxed(IRuntimeSession session, ExpressionNode target, ExpressionNode value, RuntimeEvaluationContext context)
    {
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables || target is not (SimpleNameExpressionNode or IndexExpressionNode))
        {
            return false;
        }

        // MS-VBAL §5.3.1: the name of a function in its own body is its function result variable, which holds a payload of its scalar type (S1.3).
        var bound = BoundTreeOf(session, target, context);
        if (bound is BoundName { Symbol: var function } && ResultFrameOf(session, function, context) is { } frame)
        {
            if (!TryEvaluatePayload(session, BoundTreeOf(session, value, context), out var result)
                || !TryConvertPayload(result, ((ITypedSymbol)function).ResolvedType, out var assigned))
            {
                return false;
            }

            frame.AssignResult(assigned);
            return true;
        }

        // an element of an array of a scalar type, written in the cell it is kept in (S1.6).
        if (bound is BoundIndex index)
        {
            return ElementCellOf(session, index) is { } element
                && TryEvaluatePayload(session, BoundTreeOf(session, value, context), out var written) && TryWritePayload(element, written);
        }

        if (bound is not BoundVariable variable)
        {
            return false;
        }

        var (cell, referenced) = BindingOf(session, variable);
        if (referenced is null && cell is ScalarCell or VariantCell)
        {
            return TryEvaluatePayload(session, BoundTreeOf(session, value, context), out var payload) && TryWritePayload(cell, payload);
        }

        // a String variable - not a fixed-length one, which pads or truncates what it is assigned - is assigned the text as it is (S1.4b).
        if (referenced is null && cell is ValueBindingHandle text && variable.Variable is ITypedSymbol { ResolvedType: VBStringType and not VBFixedStringType })
        {
            if (!TryEvaluateText(session, BoundTreeOf(session, value, context), out var assigned))
            {
                return false;
            }

            text.Store(assigned, isKnown: true);
            return true;
        }

        return false;
    }

    // MS-VBAL §5.6.9.4, §5.6.5: an expression of Strings evaluated to the runtime value of the String it is, with no value made of it (S1.4b): a String
    // literal, a String variable, parentheses, and two of them joined by & (or by +, of two Strings). A String that ends with a lone byte is the boxed
    // evaluation's, which joins it byte by byte.
    private bool TryEvaluateText(IRuntimeSession session, BoundNode node, out VBRuntimeValue<string> text)
    {
        text = default;
        switch (node)
        {
            case BoundLiteral { Literal.StaticValue: VBStringValue { RuntimeValue: VBRuntimeValue<string> literal } }:
                text = literal;
                return true;
            case BoundVariable variable:
                if (BindingOf(session, variable) is (IKnowledgeBinding { IsKnown: true, Value: VBRuntimeValue<string> held }, null))
                {
                    text = held;
                    return true;
                }

                return false;
            case BoundUnary { Operation.Token: SDK.Model.Symbols.Operators.OperatorSymbolNames.UnaryArithmeticPrecedenceOp
                or SDK.Model.Symbols.Operators.OperatorSymbolNames.UnaryLetCoerceOp, Operand: var enclosed }:
                return TryEvaluateText(session, enclosed, out text);
            case BoundBinary { Plan: { IsDeferred: false, EffectiveType: VBStringType } plan, Left: var left, Right: var right }
                when plan.Operator is Tokens.ConcatOp or Tokens.AdditionOp:
                if (!TryEvaluateText(session, left, out var lhs) || !TryEvaluateText(session, right, out var rhs))
                {
                    return false;
                }

                // vbNullString joins as the zero-length string it reads as: what is joined is never vbNullString (VBStringValue.Concat).
                text = new VBRuntimeValue<string>(string.Concat(lhs.Value, rhs.Value));
                return true;
            default:
                return false;
        }
    }

    // MS-VBAL §5.6.9.5: two Strings compared under the comparison mode of the module, as the boxed relational operators compare them
    // (StringComparisonRules), to a Boolean.
    private bool TryCompareText(IRuntimeSession session, OperationPlan plan, BoundNode left, BoundNode right, out ScalarPayload result)
    {
        result = default;
        if (!TryEvaluateText(session, left, out var lhs) || !TryEvaluateText(session, right, out var rhs))
        {
            return false;
        }

        var order = session.CurrentStringComparison().Compare(lhs.Value.AsSpan(), rhs.Value.AsSpan());
        bool? isTrue = plan.Operator switch
        {
            Tokens.CompareEqualOp => order == 0,
            Tokens.CompareNotEqualOp => order != 0,
            Tokens.CompareLessThanOp => order < 0,
            Tokens.CompareLessThanOrEqualOp => order <= 0,
            Tokens.CompareGreaterThanOp => order > 0,
            Tokens.CompareGreaterThanOrEqualOp => order >= 0,
            _ => null,
        };

        result = isTrue is { } value ? ScalarPayload.OfBoolean(value) : default;
        return isTrue is not null;
    }

    // a payload written to a cell that holds one: let-coerced exactly to the type of a scalar variable, and as it is to a Variant, which takes the value's
    // type (MS-VBAL §5.5.1.2.12, S1.4).
    private static bool TryWritePayload(IBindingHandle cell, ScalarPayload payload)
    {
        switch (cell)
        {
            case ScalarCell scalar when TryConvertPayload(payload, scalar.Type, out var converted):
                scalar.Write(converted);
                return true;
            case VariantCell { HoldsNoObject: true } variant:
                variant.Write(payload);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Let-assigns <paramref name="value"/>, already evaluated, to <paramref name="target"/> on payloads, when the target is a variable that holds a payload
    /// and the value is a scalar that converts to its type exactly: what a call returned (IR lowering S1.3) - or a Variant that holds one, whose value is
    /// what is let-assigned (MS-VBAL §5.5.1.2.12), and a String assigned to a String variable as it is (S1.9).
    /// </summary>
    /// <returns><see langword="false"/> when the assignment is not done here, and is to be done the way it always is.</returns>
    internal bool TryAssignValueUnboxed(IRuntimeSession session, ExpressionNode target, VBTypedValue value, RuntimeEvaluationContext context)
    {
        if (value is VBVariantValue { IsIndeterminate: false, TypedValue: { } held } && (ScalarPayload.IsScalar(held.TypeInfo) || held is VBStringValue))
        {
            value = held;
        }

        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables
            || value is not { IsIndeterminate: false, Handle: ValueBindingHandle boxed }
            || BoundVariableOf(session, target, context) is not { } variable)
        {
            return false;
        }

        // a String variable - not a fixed-length one, which pads or truncates what it is assigned - holds the String it is assigned (S1.4b).
        if (value is VBStringValue && boxed.Value is VBRuntimeValue<string> assigned)
        {
            if (BindingOf(session, variable) is (ValueBindingHandle text, null) && variable.Variable is ITypedSymbol { ResolvedType: VBStringType and not VBFixedStringType })
            {
                text.Store(assigned, isKnown: true);
                return true;
            }

            return false;
        }

        return ScalarPayload.IsScalar(value.TypeInfo) && ScalarPayload.TryOf(value.TypeInfo, boxed.Value, out var payload)
            && BindingOf(session, variable) is (ScalarCell or VariantCell, null) and var (cell, _) && TryWritePayload(cell, payload);
    }

    // a known value of a scalar type, or a String, let-assigned to a Variant (MS-VBAL §5.5.1.2.12): the Variant holds the very value, which nothing can change -
    // with no let-coercion made of it (S1.9). Of a Variant, it is the value the Variant holds (whose type a VBVariantValue says is its own). An object, an
    // array, a record and what is not known are let-coerced as they always are.
    private bool TryHoldInVariant(VBTypedValue value, out IRuntimeValue held)
    {
        held = null!;
        if (value is VBVariantValue { IsIndeterminate: false, TypedValue: { } inner })
        {
            value = inner;
        }

        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables || value is VBVariantValue || value.IsIndeterminate
            || !(value is VBStringValue { RuntimeValue: VBRuntimeValue<string> }
                || ScalarPayload.IsScalar(value.TypeInfo) && ScalarPayload.TryOf(value.TypeInfo, value.RuntimeValue, out _)))
        {
            return false;
        }

        held = VariantCell.Holding(value.TypeInfo, value.RuntimeValue);
        return true;
    }

    // the activation whose function result variable a name in the body of a function is (LetAssignmentEvaluator.Assign has the same test): a function of a
    // scalar type, whose body this is.
    private static CallStackFrame? ResultFrameOf(IRuntimeSession session, Symbol symbol, RuntimeEvaluationContext context)
        => symbol is (VBFunctionMemberSymbol or VBPropertyGetMemberSymbol) and ITypedSymbol { ResolvedType: var type } && ScalarPayload.IsScalar(type)
            && (ReferenceEquals(symbol.Uri, context.Scope) || symbol.Uri.AbsoluteUri == context.Scope.AbsoluteUri)
            && session.CallStack.Current is CallStackFrame frame
                ? frame
                : null;

    /// <summary>
    /// Steps a For loop whose counter holds a payload (MS-VBAL §5.4.2.3): the counter is given its value plus the step, and compared with the end - in
    /// the counter's type, which the end and the step are of.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="state">The loop.</param>
    /// <param name="context">Where the counter is resolved.</param>
    /// <param name="isOutOfRange">Whether the counter is past the end, and the loop ends.</param>
    /// <returns><see langword="false"/> when the loop is not stepped here - the sum would raise an error, or a value is not a payload - and is to be stepped the way it always is.</returns>
    internal bool TryStepForUnboxed(IRuntimeSession session, ForLoopState state, RuntimeEvaluationContext context, out bool isOutOfRange)
    {
        isOutOfRange = false;
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables
            || state.RuntimeState is not ForLoopPayloads loop || loop.IsDeclined
            || !(loop.IsReady && ReferenceEquals(loop.Scopes, session.Symbols.Scopes)) && !TryPrepareFor(session, state, context, loop))
        {
            return false;
        }

        switch (BindingOf(session, loop.Counter!))
        {
            case (ScalarCell cell, null) when cell.TryRead(out var value) && ReferenceEquals(cell.Type, loop.Step.Type)
                && TryOperate(Tokens.AdditionOp, cell.Type, value, loop.Step, out var sum):
                cell.Write(sum);
                isOutOfRange = sum.IsIntegral
                    ? state.CountsDown ? sum.Integral < loop.End.Integral : sum.Integral > loop.End.Integral
                    : state.CountsDown ? sum.Floating < loop.End.Floating : sum.Floating > loop.End.Floating;
                return true;
            case (VariantCell variant, null):
                return TryStepVariantFor(variant, loop, state.CountsDown, out isOutOfRange);
            default:
                return false;
        }
    }

    // what every Next of the loop steps with, worked out by the first (S1): the counter as it is bound, and the step and the end as payloads - of the type
    // the loop counts in, which neither changes while the loop runs. A loop whose step or end is no payload is never stepped here.
    private bool TryPrepareFor(IRuntimeSession session, ForLoopState state, RuntimeEvaluationContext context, ForLoopPayloads loop)
    {
        if (state.Step.IsIndeterminate || state.End.IsIndeterminate
            || !ScalarPayload.IsScalar(state.Step.TypeInfo) || !ScalarPayload.IsScalar(state.End.TypeInfo)
            || !ScalarPayload.TryOf(state.Step.TypeInfo, state.Step.RuntimeValue, out loop.Step)
            || !ScalarPayload.TryOf(state.End.TypeInfo, state.End.RuntimeValue, out loop.End)
            || BoundVariableOf(session, state.ControlExpression, context) is not { } counter)
        {
            loop.IsDeclined = true;
            return false;
        }

        (loop.Counter, loop.Scopes, loop.IsReady) = (counter, session.Symbols.Scopes, true);
        return true;
    }

    // a Variant counter (S1.4), which the loop gave a value of the type it counts in (ForLoopEvaluator.Operands): counter + step and the comparison with
    // the end are Variant operations, planned for what the counter holds - a sum that overflows declines, and the boxed loop widens the counter as MS-VBA
    // does. The plans are made again only when the counter's subtype changes.
    private static bool TryStepVariantFor(VariantCell cell, ForLoopPayloads loop, bool countsDown, out bool isOutOfRange)
    {
        isOutOfRange = false;
        if (!cell.HoldsNoObject || !cell.TryRead(out var value))
        {
            return false;
        }

        if (!ReferenceEquals(loop.PlannedFor, value.Type))
        {
            loop.Sum = OperationPlanner.ForValues(Tokens.AdditionOp, value.Type, loop.Step.Type);
            loop.Comparison = loop.Sum.EffectiveType is { } summed
                ? OperationPlanner.ForValues(countsDown ? Tokens.CompareLessThanOp : Tokens.CompareGreaterThanOp, summed, loop.End.Type)
                : null;
            loop.PlannedFor = value.Type;
        }

        if (loop.Comparison is null || !TryOperateBinary(loop.Sum!, value, loop.Step, out var sum) || !TryOperateBinary(loop.Comparison, sum, loop.End, out var past))
        {
            return false;
        }

        cell.Write(sum);
        isOutOfRange = past.IsTrue;
        return true;
    }

    private bool TryEvaluatePayload(IRuntimeSession session, BoundNode node, out ScalarPayload payload)
    {
        payload = default;
        switch (node)
        {
            case BoundLiteral literal:
                return ScalarPayload.TryOf(literal.Literal.StaticValue.TypeInfo, literal.Literal.StaticValue.RuntimeValue, out payload);
            case BoundVariable variable:
                return BindingOf(session, variable) switch
                {
                    (ScalarCell cell, null) => cell.TryRead(out payload),
                    // a Variant's payload is of its subtype (S1.4).
                    (VariantCell cell, null) => cell.TryRead(out payload),
                    _ => false,
                };
            // a constant - a Const, a member of an enumeration - bare or qualified, folded once: the same value wherever it is read.
            case BoundName { Symbol: var symbol } when IsConstant(symbol):
                return TryReadConstant(session, node, symbol, out payload);
            case BoundQualifiedName { Member: var member } when IsConstant(member):
                return TryReadConstant(session, node, member, out payload);
            // MS-VBAL §5.6.6: what is in parentheses is a value - of a payload, the payload, whether the parentheses group an operand (the precedence
            // operator) or coerce what they enclose (the let-coercion operator, which converts nothing of a scalar).
            case BoundUnary { Operation.Token: SDK.Model.Symbols.Operators.OperatorSymbolNames.UnaryArithmeticPrecedenceOp
                or SDK.Model.Symbols.Operators.OperatorSymbolNames.UnaryLetCoerceOp, Operand: var enclosed }:
                return TryEvaluatePayload(session, enclosed, out payload);
            case BoundUnary { Plan: { } plan, Operand: var operand } when plan.Operator is Tokens.NegationOp or Tokens.LogicalNotOp:
                return TryEvaluatePayload(session, operand, out var value) && TryOperateUnary(PlanFor(plan, value), value, out payload);
            // two Strings compared (S1.4b).
            case BoundBinary { Plan: { IsDeferred: false, EffectiveType: VBStringType } plan, Left: var left, Right: var right }:
                return TryCompareText(session, plan, left, right, out payload);
            case BoundBinary { Plan: { } plan, Left: var left, Right: var right }:
                return TryEvaluatePayload(session, left, out var lhs) && TryEvaluatePayload(session, right, out var rhs)
                    && TryOperateBinary(PlanFor(plan, lhs, rhs), lhs, rhs, out payload);
            // an element of an array of a scalar type, read in the cell it is kept in (S1.6).
            case BoundIndex index:
                return ElementCellOf(session, index) is { } element && element.TryRead(out payload);
            default:
                return false;
        }
    }

    private static bool IsConstant(Symbol symbol) => symbol is VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol;

    // MS-VBAL §5.2.3.2, §5.2.3.4: a constant's value is its expression folded, which does not change for as long as the declaration does - and a name
    // bound to it is bound again when it changes. So it is read as the boxed evaluation reads it, once for each place it is named, and kept as a payload.
    // A constant that is not of a scalar type, or whose value could not be folded, is the boxed evaluation's every time.
    private bool TryReadConstant(IRuntimeSession session, BoundNode node, Symbol constant, out ScalarPayload payload)
    {
        if (!_constantPayloads.TryGetValue(node, out var known))
        {
            var read = ReadSymbol(session, new RuntimeEvaluationContext(constant.ParentUri), constant, nameOfEnclosingFunctionIsItsResult: false);
            known = read is { IsSuccess: true, Result: { IsIndeterminate: false } value }
                && ScalarPayload.TryOf(value.TypeInfo, value.RuntimeValue, out var folded)
                    ? new ConstantPayload(true, folded)
                    : default;
            _constantPayloads[node] = known;
        }

        payload = known.Payload;
        return known.IsScalar;
    }

    private readonly record struct ConstantPayload(bool IsScalar, ScalarPayload Payload);

    private readonly ConcurrentDictionary<BoundNode, ConstantPayload> _constantPayloads = new(ReferenceEqualityComparer.Instance);

    // the cell of an element of an array of a scalar type a variable holds - a(i, j) - when each subscript is evaluated on payloads and selects an element
    // (S1.6). A subscript is let-coerced to a Long, as the boxed evaluation coerces it (EvaluateSubscripts): one that would raise an error - past the range
    // of a Long, outside its dimension - is that evaluation's to raise, and so is an array that is not known, or one whose elements are kept as values.
    private ScalarCell? ElementCellOf(IRuntimeSession session, BoundIndex index)
    {
        if (index.Callee is not BoundVariable variable
            || BindingOf(session, variable).Handle is not ValueBindingHandle { Value: VBRuntimeValue<VBRuntimeArrayValue> { Value.Array: { IsIndeterminate: false } array } }
            || index.Arguments.Length != array.Rank || array.Rank > MaxPayloadRank)
        {
            return null;
        }

        Span<int> subscripts = stackalloc int[MaxPayloadRank];
        for (var k = 0; k < index.Arguments.Length; k++)
        {
            if (!TryEvaluatePayload(session, index.Arguments[k], out var subscript) || !TryConvertPayload(subscript, VBLongType.TypeInfo, out var integral))
            {
                return null;
            }

            subscripts[k] = (int)integral.Integral;
        }

        var flat = array.IndexOf(subscripts[..index.Arguments.Length]);
        return flat < 0 ? null : array.CellAt(flat) as ScalarCell;
    }

    // MS-VBAL §5.6.13 sets no limit on the rank of an array; MS-VBA's is 60. An element of more dimensions than this is read boxed.
    private const int MaxPayloadRank = 8;

    // MS-VBAL §5.6.9: a plan that depends on what a Variant operand holds is made again for the types of the payloads (S1.4) - the value types the
    // boxed evaluation makes it for (PlanExecutor); any other plan is final.
    private static OperationPlan PlanFor(OperationPlan plan, ScalarPayload operand)
        => plan.IsDeferred ? OperationPlanner.ForValue(plan.Operator, operand.Type) : plan;

    private static OperationPlan PlanFor(OperationPlan plan, ScalarPayload left, ScalarPayload right)
        => plan.IsDeferred ? OperationPlanner.ForValues(plan.Operator, left.Type, right.Type) : plan;

    private static bool TryOperateUnary(OperationPlan plan, ScalarPayload operand, out ScalarPayload result)
    {
        result = default;
        return plan.EffectiveType is { } effective && TryConvertPayload(operand, effective, out var converted)
            && (plan.Operator == Tokens.NegationOp ? TryNegate(converted, out result) : TryNot(converted, out result));
    }

    private static bool TryOperateBinary(OperationPlan plan, ScalarPayload lhs, ScalarPayload rhs, out ScalarPayload result)
    {
        result = default;
        return plan.OperandDestination is { } destination
            && TryConvertPayload(lhs, destination, out var l) && TryConvertPayload(rhs, destination, out var r)
            && (TryOperate(plan.Operator, plan.EffectiveType!, l, r, out result) || TryCompare(plan.Operator, l, r, out result)
                || TryLogical(plan.Operator, plan.EffectiveType!, l, r, out result));
    }

    /// <summary>
    /// Calls <paramref name="procedure"/>, a procedure of the workspace every parameter of which is a scalar passed by value, with its arguments evaluated
    /// on payloads and let-coerced to the parameters' types (IR lowering S1.3): each is what the parameter's cell is made of, and no value is made of any.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the call is not made here - before any argument is evaluated with an effect, so it is to be made the way it always is.
    /// <see langword="true"/> when it was made: <paramref name="result"/> is what it returned, or the error it raised.
    /// </returns>
    /// <param name="boundArguments">How each argument is passed, bound with the call (S1.7): an argument named or written in any order is given to its
    /// parameter. <see langword="null"/> takes <paramref name="argumentNodes"/> as they are written, one positional argument for each parameter.</param>
    internal bool TryInvokeScalars(IRuntimeSession session, RuntimeEvaluationContext context, VBTypeMemberSymbol procedure,
        ImmutableArray<VBParameterSymbol> parameters, ImmutableArray<ExpressionNode> argumentNodes, ImmutableArray<BoundArgument>? boundArguments,
        out RuntimeSemanticsEvaluationResult result)
    {
        result = default;
        var given = boundArguments ?? default;
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables
            || parameters.IsDefaultOrEmpty || given.IsDefault && (argumentNodes.IsDefault || argumentNodes.Length != parameters.Length)
            || InvokerOf(procedure) is not { } invoker)
        {
            return false;
        }

        var arguments = ArrayPool<ScalarPayload>.Shared.Rent(parameters.Length);
        try
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                // a Variant parameter holds a scalar as it is, of its own type (MS-VBAL §5.5.1.2.12): its payload, which is not converted (S1.7).
                var isVariant = parameter.ResolvedType is VBVariantType;
                if (RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind) || parameter is ParamArrayParameterSymbol
                    || !(isVariant || ScalarPayload.IsScalar(parameter.ResolvedType))
                    || ArgumentOf(i) is not { } argument
                    || !TryEvaluatePayload(session, argument, out var payload))
                {
                    return false;
                }

                if (isVariant)
                {
                    arguments[i] = payload;
                }
                else if (!TryConvertPayload(payload, parameter.ResolvedType, out arguments[i]))
                {
                    return false;
                }
            }

            result = invoker.InvokeScalars(procedure, arguments.AsSpan(0, parameters.Length));
            return true;
        }
        finally
        {
            ArrayPool<ScalarPayload>.Shared.Return(arguments);
        }

        // the node of the argument parameter i is given: of a call bound with its arguments, the one passed by value; of any other, the positional one
        // written at i. An omitted argument has none.
        BoundNode? ArgumentOf(int i)
            => !given.IsDefault
                ? given[i] is { Passing: ArgumentPassing.Value, Value: { } value } ? value : null
                : argumentNodes[i] is NamedArgumentNode or MissingArgumentNode ? null : BoundTreeOf(session, argumentNodes[i], context);
    }

    // the invoker that runs a procedure of the workspace, called on nothing: the one the binding factory binds it to (a member of anything else is bound
    // to something else, which is the factory's decision), or the evaluator's own when there is no factory.
    private RuntimeProcedureInvoker? InvokerOf(VBTypeMemberSymbol procedure)
        => Bindings is { } bindings
            ? bindings.ForMember(procedure) is CallableBindingHandle { Invoker: RuntimeProcedureInvoker invoker, Receiver: null } ? invoker : null
            : ProcedureInvoker as RuntimeProcedureInvoker;

    /// <summary>
    /// The argument of a <c>ByRef</c> parameter of a scalar type that is a variable of the caller held in a cell of its activation, of the parameter's very
    /// type: the cell, which the parameter's slot is to hold too (IR lowering S1.3, <see cref="CellReference"/>).
    /// </summary>
    /// <returns><see langword="false"/> when the argument is not passed this way, and is to be passed the way it always is.</returns>
    internal bool TryReferenceCell(IRuntimeSession session, ExpressionNode argument, VBParameterSymbol parameter, RuntimeEvaluationContext context, out IRuntimeValue reference)
    {
        reference = null!;
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables || parameter is ParamArrayParameterSymbol
            || BoundVariableOf(session, argument, context) is not { } variable
            || BindingOf(session, variable) is not (ScalarCell cell, null)
            || !cell.Type.Equals(parameter.ResolvedType))
        {
            return false;
        }

        reference = new CellReference(cell);
        return true;
    }

    /// <summary>
    /// Evaluates the argument a parameter is passed by value on payloads (MS-VBAL §5.3.1.11), let-coerced to the parameter's type, with no value made of it
    /// before: a scalar to a parameter of a scalar type, which its cell is made of (IR lowering S1.3); a scalar or a String to a <c>Variant</c>, which holds
    /// it as it is, of its own type (§5.5.1.2.12); and a String to a <c>String</c>, as it is (S1.9).
    /// </summary>
    /// <param name="bound">The argument's node, bound with the call; <see langword="null"/> binds <paramref name="argument"/> here.</param>
    /// <returns><see langword="false"/> when the argument is not evaluated here, and is to be evaluated the way it always is.</returns>
    internal bool TryEvaluateArgumentUnboxed(
        IRuntimeSession session, ExpressionNode argument, BoundNode? bound, VBType parameterType, RuntimeEvaluationContext context, out IRuntimeValue value)
    {
        value = null!;
        var isVariant = parameterType is VBVariantType;
        var isText = parameterType is VBStringType and not VBFixedStringType;
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables || !(isVariant || isText || ScalarPayload.IsScalar(parameterType)))
        {
            return false;
        }

        var node = bound ?? BoundTreeOf(session, argument, context);
        // a String is evaluated with no effect, so one that is not evaluated here is evaluated as a scalar next, or as it always is.
        if ((isText || isVariant) && TryEvaluateText(session, node, out var text))
        {
            value = isVariant ? VariantCell.Holding(VBStringType.TypeInfo, text) : text;
            return true;
        }

        if (isText)
        {
            return false;
        }

        if (!TryEvaluatePayload(session, node, out var payload))
        {
            return false;
        }

        if (isVariant)
        {
            value = VariantCell.Holding(payload.Type, payload.Box());
            return true;
        }

        if (!TryConvertPayload(payload, parameterType, out var converted))
        {
            return false;
        }

        value = converted.Box();
        return true;
    }

    /// <summary>
    /// Evaluates a condition on payloads - of <c>If</c>, a loop, <c>Debug.Assert</c> - when it is an expression evaluated on them: its value let-coerced to a
    /// <c>Boolean</c> is whether it is not zero (MS-VBAL §5.5.1.2.2).
    /// </summary>
    /// <returns><see langword="false"/> when the condition is not evaluated here, and is to be evaluated the way it always is.</returns>
    internal bool TryEvaluateConditionUnboxed(IRuntimeSession session, ExpressionNode condition, RuntimeEvaluationContext context, out bool isTrue)
    {
        isTrue = false;
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables
            || !TryEvaluatePayload(session, BoundTreeOf(session, condition, context), out var payload))
        {
            return false;
        }

        isTrue = payload.IsTrue;
        return true;
    }

    /// <summary>
    /// Evaluates the selector of a <c>Select Case</c> on payloads (MS-VBAL §5.4.2.10's select-expression, evaluated once), when it is an expression
    /// evaluated on them: what each <c>Case</c> compares, with no value made of it (IR lowering S1.6).
    /// </summary>
    /// <returns><see langword="false"/> when the selector is not evaluated here, and is to be evaluated the way it always is.</returns>
    internal bool TryEvaluateSelectorUnboxed(IRuntimeSession session, ExpressionNode selector, RuntimeEvaluationContext context, out ScalarPayload payload)
    {
        payload = default;
        return UnboxedEvaluation.IsEnabled && Observation is null && !AssumesVariables
            && TryEvaluatePayload(session, BoundTreeOf(session, selector, context), out payload);
    }

    /// <summary>
    /// Compares the selector of a <c>Select Case</c> with the value of a <c>Case</c> on payloads (MS-VBAL §5.4.2.10): <c>selector operator value</c>, when the
    /// selector is a payload and the value an expression evaluated on them.
    /// </summary>
    /// <returns><see langword="false"/> when the comparison is not made here, and is to be made the way it always is.</returns>
    internal bool TryCompareUnboxed(IRuntimeSession session, string comparisonOperator, VBTypedValue selector, ExpressionNode value, RuntimeEvaluationContext context, out bool isMatch)
    {
        isMatch = false;
        return !selector.IsIndeterminate && ScalarPayload.TryOf(selector.TypeInfo, selector.RuntimeValue, out var lhs)
            && TryCompareUnboxed(session, comparisonOperator, lhs, value, context, out isMatch);
    }

    /// <inheritdoc cref="TryCompareUnboxed(IRuntimeSession, string, VBTypedValue, ExpressionNode, RuntimeEvaluationContext, out bool)"/>
    /// <remarks>The selector evaluated on payloads itself (<see cref="TryEvaluateSelectorUnboxed"/>).</remarks>
    internal bool TryCompareUnboxed(IRuntimeSession session, string comparisonOperator, ScalarPayload lhs, ExpressionNode value, RuntimeEvaluationContext context, out bool isMatch)
    {
        isMatch = false;
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables
            || !TryEvaluatePayload(session, BoundTreeOf(session, value, context), out var rhs)
            || OperationPlanner.ForValues(comparisonOperator, lhs.Type, rhs.Type) is not { OperandDestination: { } destination }
            || !TryConvertPayload(lhs, destination, out var l) || !TryConvertPayload(rhs, destination, out var r)
            || !TryCompare(comparisonOperator, l, r, out var result))
        {
            return false;
        }

        isMatch = result.IsTrue;
        return true;
    }

    // the conversions that are exact - what a value of one numeric scalar type is in another, with no rounding to decide: an integer in the range of an
    // integer type, an integer as a floating-point value, a Single as a Double. Anything else is the boxed coercion's (MS-VBAL §5.5.1.2.1), which rounds.
    private static bool TryConvertPayload(ScalarPayload payload, VBType destination, out ScalarPayload converted)
    {
        converted = payload;
        if (ReferenceEquals(payload.Type, destination) || payload.Type.Equals(destination))
        {
            return true;
        }

        // MS-VBAL §5.5.1.2.2: any number is True when it is not zero, and a Boolean is the -1 or 0 it holds - which a Byte does not hold, its True being
        // 255: the boxed coercion's.
        if (destination is VBBooleanType)
        {
            converted = ScalarPayload.OfBoolean(payload.IsTrue);
            return true;
        }

        // a Currency is a whole number in an integer type when it is one, which nothing rounds; as a floating-point value it is the Double nearest a
        // decimal number, which is the boxed coercion's.
        if (payload.Type is VBCurrencyType)
        {
            return destination is VBByteType or VBIntegerType or VBLongType or VBLongLongType && payload.Integral % ScalarPayload.CurrencyScale == 0
                && ScalarPayload.TryIntegral(destination, payload.Integral / ScalarPayload.CurrencyScale, out converted);
        }

        if (destination is VBCurrencyType)
        {
            return TryConvertToCurrency(payload, out converted);
        }

        return (payload.IsIntegral, destination) switch
        {
            (true, VBByteType or VBIntegerType or VBLongType or VBLongLongType) => ScalarPayload.TryIntegral(destination, payload.Integral, out converted),
            (true, VBDoubleType or VBDateType) => ScalarPayload.TryFloating(destination, payload.Integral, out converted),
            (true, VBSingleType) when payload.Type is not VBLongLongType => ScalarPayload.TryFloating(destination, payload.Integral, out converted),
            // a Date is its serial (MS-VBAL §5.5.1.2.3), and a number in the range of the serials is a Date.
            (false, VBDoubleType) => ScalarPayload.TryFloating(destination, payload.Floating, out converted),
            (false, VBDateType) => ScalarPayload.TryFloating(destination, payload.Floating, out converted),
            _ => false,
        };
    }

    // MS-VBAL §5.5.1.2.1: a whole number is a Currency when it is in its range; a Double when it has no more than four decimal places - and few enough
    // digits that the Decimal the boxed coercion makes of it, rounded to 15 significant digits, is the decimal number it is the Double nearest to.
    private static bool TryConvertToCurrency(ScalarPayload payload, out ScalarPayload converted)
    {
        converted = default;
        if (payload.IsIntegral)
        {
            if (payload.Integral is < long.MinValue / ScalarPayload.CurrencyScale or > long.MaxValue / ScalarPayload.CurrencyScale)
            {
                return false;
            }

            converted = ScalarPayload.OfCurrency(payload.Integral * ScalarPayload.CurrencyScale);
            return true;
        }

        var scaled = payload.Floating * ScalarPayload.CurrencyScale;
        if (payload.Type is not VBDoubleType || Math.Abs(scaled) >= 1e15 || scaled != Math.Floor(scaled) || scaled / ScalarPayload.CurrencyScale != payload.Floating)
        {
            return false;
        }

        converted = ScalarPayload.OfCurrency((long)scaled);
        return true;
    }

    // MS-VBAL §5.6.9.3: the operators on their effective type, as the boxed evaluation does them (checked, in the type's own width); a result that does not
    // fit, a division by zero and a result that is not finite are errors, which the boxed evaluation raises.
    private static bool TryOperate(string token, VBType effective, ScalarPayload l, ScalarPayload r, out ScalarPayload result)
    {
        result = default;
        try
        {
            return (effective, token) switch
            {
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.AdditionOp) => ScalarPayload.TryIntegral(effective, checked(l.Integral + r.Integral), out result),
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.SubtractionOp) => ScalarPayload.TryIntegral(effective, checked(l.Integral - r.Integral), out result),
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.MultiplicationOp) => ScalarPayload.TryIntegral(effective, checked(l.Integral * r.Integral), out result),
                // §5.6.9.3.5-6: a quotient that is truncated, and the remainder that has the dividend's sign - in the integer effective type the
                // operands were coerced to (a floating-point operand is rounded to it, which is the boxed coercion's).
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.IntegerDivisionOp) when r.Integral != 0 => ScalarPayload.TryIntegral(effective, checked(l.Integral / r.Integral), out result),
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.ModuloOp) when r.Integral != 0 => ScalarPayload.TryIntegral(effective, checked(l.Integral % r.Integral), out result),
                // a Currency is its ten-thousandths: a sum is theirs, and a product is theirs divided by 10,000 - when nothing rounds it, which the boxed
                // evaluation does, to even (recorded in Excel).
                (VBCurrencyType, Tokens.AdditionOp) => Currency(checked(l.Integral + r.Integral), out result),
                (VBCurrencyType, Tokens.SubtractionOp) => Currency(checked(l.Integral - r.Integral), out result),
                (VBCurrencyType, Tokens.MultiplicationOp) => TryMultiplyCurrency(l.Integral, r.Integral, out result),
                // a Date operation is evaluated in Double (OperationPlan.OperandDestination), and is a Date when it is a date's serial.
                (VBDateType, Tokens.AdditionOp) => ScalarPayload.TryFloating(effective, l.Floating + r.Floating, out result),
                (VBDateType, Tokens.SubtractionOp) => ScalarPayload.TryFloating(effective, l.Floating - r.Floating, out result),
                (VBSingleType, Tokens.AdditionOp) => ScalarPayload.TryFloating(effective, (float)l.Floating + (float)r.Floating, out result),
                (VBSingleType, Tokens.SubtractionOp) => ScalarPayload.TryFloating(effective, (float)l.Floating - (float)r.Floating, out result),
                (VBSingleType, Tokens.MultiplicationOp) => ScalarPayload.TryFloating(effective, (float)l.Floating * (float)r.Floating, out result),
                (VBSingleType, Tokens.DivisionOp) when r.Floating != 0 => ScalarPayload.TryFloating(effective, (float)l.Floating / (float)r.Floating, out result),
                (VBDoubleType, Tokens.AdditionOp) => ScalarPayload.TryFloating(effective, l.Floating + r.Floating, out result),
                (VBDoubleType, Tokens.SubtractionOp) => ScalarPayload.TryFloating(effective, l.Floating - r.Floating, out result),
                (VBDoubleType, Tokens.MultiplicationOp) => ScalarPayload.TryFloating(effective, l.Floating * r.Floating, out result),
                (VBDoubleType, Tokens.DivisionOp) when r.Floating != 0 => ScalarPayload.TryFloating(effective, l.Floating / r.Floating, out result),
                _ => false,
            };
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    // §5.6.9.8: the bits of the operands, in the effective type - a Boolean's -1 or 0 giving -1 or 0 - which no operation takes out of its range. Apart from
    // the arithmetic operators, which would otherwise test these tokens too.
    private static bool TryLogical(string token, VBType effective, ScalarPayload l, ScalarPayload r, out ScalarPayload result)
    {
        result = default;
        if (!l.IsIntegral)
        {
            return false;
        }

        long? bits = token switch
        {
            Tokens.LogicalAndOp => l.Integral & r.Integral,
            Tokens.LogicalOrOp => l.Integral | r.Integral,
            Tokens.LogicalXOrOp => l.Integral ^ r.Integral,
            Tokens.LogicalEqvOp => Invert(effective, l.Integral ^ r.Integral),
            Tokens.LogicalImpOp => Invert(effective, l.Integral) | r.Integral,
            _ => null,
        };

        return bits is { } value && ScalarPayload.TryIntegral(effective, value, out result);
    }

    // MS-VBAL §5.6.9.5: a comparison of two operands of its effective type, which they were let-coerced to, is a Boolean - the payloads are finite, so any
    // two compare.
    private static bool TryCompare(string token, ScalarPayload l, ScalarPayload r, out ScalarPayload result)
    {
        var order = l.IsIntegral ? l.Integral.CompareTo(r.Integral) : l.Floating.CompareTo(r.Floating);
        bool? isTrue = token switch
        {
            Tokens.CompareEqualOp => order == 0,
            Tokens.CompareNotEqualOp => order != 0,
            Tokens.CompareLessThanOp => order < 0,
            Tokens.CompareLessThanOrEqualOp => order <= 0,
            Tokens.CompareGreaterThanOp => order > 0,
            Tokens.CompareGreaterThanOrEqualOp => order >= 0,
            _ => null,
        };

        result = isTrue is { } value ? ScalarPayload.OfBoolean(value) : default;
        return isTrue is not null;
    }

    // §5.6.9.8.1: the bits of the operand inverted, in its type - the 8 bits of a Byte, a Boolean's -1 or 0 giving 0 or -1.
    private static bool TryNot(ScalarPayload operand, out ScalarPayload result)
    {
        result = default;
        return operand.IsIntegral && ScalarPayload.TryIntegral(operand.Type, Invert(operand.Type, operand.Integral), out result);
    }

    private static long Invert(VBType type, long value) => type is VBByteType ? ~value & byte.MaxValue : ~value;

    private static bool TryNegate(ScalarPayload operand, out ScalarPayload result)
    {
        result = default;
        if (!operand.IsIntegral)
        {
            return ScalarPayload.TryFloating(operand.Type, -operand.Floating, out result);
        }

        // the negation of the least value of an integer type is not in its range (MS-VBA: Overflow), which the boxed evaluation raises.
        if (operand.Integral == long.MinValue)
        {
            return false;
        }

        return operand.Type is VBCurrencyType
            ? Currency(-operand.Integral, out result)
            : ScalarPayload.TryIntegral(operand.Type, -operand.Integral, out result);
    }

    private static bool Currency(long scaled, out ScalarPayload result)
    {
        result = ScalarPayload.OfCurrency(scaled);
        return true;
    }

    private static bool TryMultiplyCurrency(long l, long r, out ScalarPayload result)
    {
        result = default;
        var product = (Int128)l * r;
        if (product % ScalarPayload.CurrencyScale != 0)
        {
            return false;
        }

        var scaled = product / ScalarPayload.CurrencyScale;
        return scaled >= long.MinValue && scaled <= long.MaxValue && Currency((long)scaled, out result);
    }
}
