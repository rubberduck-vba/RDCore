using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Semantics;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Plans;

namespace RDCore.Runtime.Semantics;

// MS-VBAL §5.6.9.3: an expression of Single, Double and Date operations of operands of declared types is computed in the x87's extended format, and
// rounded where its value is stored (ExtendedIntermediates; recorded in Excel, grid oracle\s42). An operation's plan, made of the declared types of its
// operands, says whether it is one: a plan that is not deferred, of such an effective type. Its value is a WideValue until something takes it - stored as
// the type of the expression by default, converted to its own type by what let-coerces it to a numeric one, compared at its precision by a comparison.
public sealed partial class RuntimeExpressionEvaluator
{
    // what the extended evaluation of a node gives: its value, or the error evaluating an operand raised.
    private readonly record struct WideResult(WideValue Value, RuntimeSemanticsEvaluationResult? Failed);

    /// <summary>
    /// Whether <paramref name="node"/> is an extended intermediate: an arithmetic operation of a <c>Single</c>, <c>Double</c> or <c>Date</c> effective type
    /// whose operands have declared types, a negation of one, parentheses around one, or <c>Abs</c>, <c>Int</c>, <c>Fix</c>, <c>CSng</c> or <c>CDbl</c> of
    /// one.
    /// </summary>
    /// <remarks>
    /// An analysis - an observation of the operations, or an evaluation that assumes the values of variables - evaluates each operation on its own, as
    /// the operator does (one at a time, each rounded twice): what it states is of each, and what it assumes is no value to compute with.
    /// </remarks>
    private bool IsWide(BoundNode node) => Observation is null && !AssumesVariables && WideTypeOf(node) is not null;

    // the type of an extended intermediate's expression; null for a node that is not one. CSng of one is a Single and CDbl a Double, as they are declared
    // (MS-VBAL §6.1.2.3.1.11, §6.1.2.3.1.5); Abs, Int and Fix, declared to return a Variant (§6.1.2.10.1.1, §6.1.2.3.1.18, §6.1.2.3.1.16), are of the type
    // of the number they are given in MS-VBA, which computes them on it (s42 A10-A13).
    private static VBType? WideTypeOf(BoundNode node) => node switch
    {
        BoundBinary binary => PlanOf(binary) is { IsDeferred: false } plan && ExtendedIntermediates.IsArithmetic(plan.Operator)
            && ExtendedIntermediates.IsFloating(plan.EffectiveType) ? plan.EffectiveType : null,
        BoundUnary { Plan: { Operator: Tokens.NegationOp } } unary => PlanOf(unary) is { IsDeferred: false } plan
            && ExtendedIntermediates.IsFloating(plan.EffectiveType) ? plan.EffectiveType : null,
        BoundUnary { Plan: null, Operation.Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp, Operand: var enclosed }
            => WideTypeOf(enclosed),
        BoundCall call when WideFunctionOf(call) is { } function && ExtendedIntermediates.Applies(function.Key) && WideTypeOf(function.Argument) is { } type
            => function.Key switch
            {
                "IStdConversionModule.CSng/1" => VBSingleType.TypeInfo,
                "IStdConversionModule.CDbl/1" => VBDoubleType.TypeInfo,
                _ => type,
            },
        _ => null,
    };

    // the plan of an operation, made again of the types of its extended operands where they are what deferred it: an operand that is Abs(x) of a Double is
    // a Double, which the plan made of the declared types took for the Variant Abs is declared to return.
    private static OperationPlan? PlanOf(BoundBinary binary)
    {
        if (binary.Plan is not { IsDeferred: true, Operands: [var declaredLeft, var declaredRight] } plan)
        {
            return binary.Plan;
        }

        var left = WideTypeOf(binary.Left) ?? declaredLeft;
        var right = WideTypeOf(binary.Right) ?? declaredRight;
        return ReferenceEquals(left, declaredLeft) && ReferenceEquals(right, declaredRight) || IsUntyped(left) || IsUntyped(right)
            ? plan
            : OperationPlanner.ForValues(plan.Operator, left, right);
    }

    private static OperationPlan? PlanOf(BoundUnary unary)
        => unary.Plan is { IsDeferred: true } plan && WideTypeOf(unary.Operand) is { } operand ? OperationPlanner.ForValue(plan.Operator, operand) : unary.Plan;

    // a declared type that says nothing of the value: what an operand of it holds decides the plan, when it is evaluated.
    private static bool IsUntyped(VBType type) => type is VBVariantType or VBObjectType;

    // a call of a member of the standard library of one argument, by the key its implementation is found by, and its argument.
    private static (string Key, BoundNode Argument)? WideFunctionOf(BoundCall call)
        => call is { BoundArguments: [{ Value: { } argument, Elements.IsDefaultOrEmpty: true }] }
            && call.Procedure.GetProperty(SymbolProperties.ExternalTarget) is { } key && StdLibSymbolProvider.IsLibraryMember(call.Procedure)
            ? (key, argument)
            : null;

    private WideResult EvaluateWide(IRuntimeSession session, BoundNode node, RuntimeEvaluationContext context)
    {
        switch (node)
        {
            case BoundBinary binary when PlanOf(binary) is { } plan:
            {
                var left = EvaluateWideOperand(session, binary.Left, plan, context);
                if (left.Failed is not null)
                {
                    return left;
                }

                var right = EvaluateWideOperand(session, binary.Right, plan, context);
                return right.Failed is not null
                    ? right
                    : new WideResult(ExtendedIntermediates.Operate(plan.Operator, left.Value, right.Value, plan.EffectiveType!), null);
            }
            case BoundUnary { Plan: not null } unary when PlanOf(unary) is { } plan:
            {
                var operand = EvaluateWideOperand(session, unary.Operand, plan, context);
                return operand.Failed is not null ? operand : new WideResult(ExtendedIntermediates.Negate(operand.Value, plan.EffectiveType!), null);
            }
            case BoundUnary unary:
                return EvaluateWide(session, unary.Operand, context);
            case BoundCall call when WideFunctionOf(call) is { } function:
            {
                var argument = EvaluateWide(session, function.Argument, context);
                return argument.Failed is not null ? argument : new WideResult(ExtendedIntermediates.Apply(function.Key, argument.Value)!.Value, null);
            }
            default:
                return new WideResult(default, RuntimeSemanticsEvaluationResult.InternalError());
        }
    }

    // an operand of an extended operation: an extended intermediate itself, or a value of a declared type, let-coerced to the type the operation's operands
    // are - a String is a number (MS-VBAL §5.5.1.2.6), a Currency its ten-thousandths, exactly.
    private WideResult EvaluateWideOperand(IRuntimeSession session, BoundNode operand, OperationPlan plan, RuntimeEvaluationContext context)
    {
        if (IsWide(operand))
        {
            return EvaluateWide(session, operand, context);
        }

        var evaluated = EvaluateBoundNode(session, operand, context);
        if (!evaluated.IsSuccess)
        {
            return new WideResult(default, Unvalued(evaluated));
        }

        var value = evaluated.Result!;
        if (!ScalarPayload.TryOf(value.TypeInfo, value.RuntimeValue, out var payload))
        {
            var coerced = CoercedOperand(session, operand, value, plan.OperandDestination!);
            if (!coerced.IsSuccess || !ScalarPayload.TryOf(coerced.Result!.TypeInfo, coerced.Result.RuntimeValue, out payload))
            {
                return new WideResult(default, coerced.IsSuccess ? RuntimeSemanticsEvaluationResult.InternalError() : Unvalued(coerced));
            }
        }

        return new WideResult(ExtendedIntermediates.Widened(payload, plan.EffectiveType!), null);
    }

    private RuntimeSemanticsEvaluationResult CoercedOperand(IRuntimeSession session, BoundNode operand, VBTypedValue value, VBType destination)
    {
        if (TryConvertUnobserved(value, destination, out var planned))
        {
            return RuntimeSemanticsEvaluationResult.Success(planned);
        }

        var frame = new LetCoercionStackFrame(operand.Expression.Identity, InputIndex.CoercionSourceValue, value, new VBTypeDescValue(destination));
        var coerced = LetCoercionProvider!.EvaluateLetCoercionSemantics(session.Symbols.Resolver, operand.Expression, frame);
        return coerced.IsApplicable
            ? (coerced.IsSuccess ? RuntimeSemanticsEvaluationResult.Success(coerced.Result!) : RuntimeSemanticsEvaluationResult.Error(coerced.ErrorInfo!))
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    // an error raised on the way to a value is raised with no value: only the expression a variable is assigned carries what IEEE arithmetic made of it.
    private static RuntimeSemanticsEvaluationResult Unvalued(RuntimeSemanticsEvaluationResult failed)
        => failed is { IsError: true, Result: not null } ? RuntimeSemanticsEvaluationResult.Error(failed.ErrorInfo!) : failed;

    // a payload a wide value converted to, and the error converting it raised - which carries the payload, for a Single or a Double.
    private static RuntimeSemanticsEvaluationResult ResultOf(ScalarPayload? payload, VBRuntimeErrorId? error, BoundNode node)
    {
        var typed = payload is { } converted ? converted.Type.CreateValue(new ValueBindingHandle(converted.Box())) : null;
        if (error is not { } id)
        {
            return RuntimeSemanticsEvaluationResult.Success(typed!);
        }

        var verbose = id is VBRuntimeErrorId.DivisionByZero ? Exceptions.VBDivisionOp_DivisionByZero : Exceptions.VBRuntimeError_ArithmeticOverflow;
        return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(id, node.Expression.SourceLocation, verbose), typed);
    }

    // an extended intermediate taken by what does not let-coerce it to a numeric type: stored as the type of its expression.
    private RuntimeSemanticsEvaluationResult EvaluateStored(IRuntimeSession session, BoundNode node, RuntimeEvaluationContext context)
    {
        var wide = EvaluateWide(session, node, context);
        return wide.Failed ?? ResultOf(ExtendedIntermediates.Store(wide.Value, out var error), error, node);
    }

    // an extended intermediate let-coerced to a numeric type - by an assignment, a parameter, an operator - converted to it from the extended value. One
    // that is let-coerced to any other is stored as the type of its expression, and coerced from that.
    private RuntimeSemanticsEvaluationResult EvaluateConverted(IRuntimeSession session, BoundNode node, VBType destination, RuntimeEvaluationContext context)
    {
        if (!ExtendedIntermediates.ConvertsDirectly(destination))
        {
            return EvaluateStored(session, node, context);
        }

        var wide = EvaluateWide(session, node, context);
        return wide.Failed ?? ResultOf(ExtendedIntermediates.Convert(wide.Value, destination, out var error), error, node);
    }

    /// <summary>
    /// Evaluates <paramref name="expression"/>, the value of a Let-assignment to <paramref name="target"/>: an extended intermediate is converted to the
    /// declared type of the target, from its 80 bits (<see cref="ExtendedIntermediates"/>) - and what raises an error in a <c>Single</c> or <c>Double</c>
    /// carries the value it rounds to, which the variable is assigned before the error is raised.
    /// </summary>
    internal RuntimeSemanticsEvaluationResult EvaluateAssigned(IRuntimeSession session, ExpressionNode target, ExpressionNode expression, RuntimeEvaluationContext context)
    {
        var node = BoundTreeOf(session, expression, context);
        return IsWide(node) && DeclaredTypeOf(session, target, context) is { } declared
            ? EvaluateConverted(session, node, declared, context)
            : EvaluateBoundNode(session, node, context);
    }

    /// <summary>
    /// Evaluates <paramref name="expression"/>, let-coerced to <paramref name="destination"/> where it is taken - an argument of a parameter of that type,
    /// the bound of a <c>For</c> loop: an extended intermediate is converted to it from its 80 bits, and an error raised is raised with no value.
    /// </summary>
    internal RuntimeSemanticsEvaluationResult EvaluateLetCoercedTo(IRuntimeSession session, ExpressionNode expression, VBType destination, RuntimeEvaluationContext context)
        => EvaluateLetCoercedTo(session, BoundTreeOf(session, expression, context), destination, context);

    private RuntimeSemanticsEvaluationResult EvaluateLetCoercedTo(IRuntimeSession session, BoundNode node, VBType destination, RuntimeEvaluationContext context)
        => IsWide(node) ? Unvalued(EvaluateConverted(session, node, destination, context)) : EvaluateBoundNode(session, node, context);

    // an operation of another effective type, or a comparison, an operand of which is an extended intermediate: a comparison of a floating-point effective
    // type compares the extended values at its precision; any other operation is given each operand let-coerced to the type it coerces them to.
    private RuntimeSemanticsEvaluationResult EvaluateWithWideOperand(IRuntimeSession session, BoundBinary binary, OperationPlan plan, RuntimeEvaluationContext context)
    {
        if (ExtendedIntermediates.IsComparison(plan.Operator) && ExtendedIntermediates.IsFloating(plan.EffectiveType))
        {
            var left = EvaluateWideOperand(session, binary.Left, plan, context);
            if (left.Failed is { } leftFailed)
            {
                return leftFailed;
            }

            var right = EvaluateWideOperand(session, binary.Right, plan, context);
            if (right.Failed is { } rightFailed)
            {
                return rightFailed;
            }

            var compared = ExtendedIntermediates.Compare(plan.Operator, left.Value, right.Value, plan.EffectiveType!, out var error);
            return ResultOf(compared is { } isTrue ? ScalarPayload.OfBoolean(isTrue) : null, error, binary);
        }

        var leftResult = EvaluateOperandOf(session, binary.Left, plan.OperandDestination, context);
        if (!leftResult.IsSuccess)
        {
            return leftResult;
        }

        var rightResult = EvaluateOperandOf(session, binary.Right, plan.OperandDestination, context);
        return rightResult.IsSuccess
            ? OperatorProvider.EvaluateBinaryOperator(session, binary.Operation, leftResult.Result!, rightResult.Result!)
            : rightResult;
    }

    // an operand of an operation that is not itself an extended one: an extended intermediate is converted to the type the operation coerces its operands
    // to, when that is a numeric one (MS-VBA: (2.5 + 2^-60) \ 1 is 3, (s + 1) ^ 1 is 16777217 - s42 D06, D15), and stored as its own type otherwise.
    private RuntimeSemanticsEvaluationResult EvaluateOperandOf(IRuntimeSession session, BoundNode operand, VBType? destination, RuntimeEvaluationContext context)
        => !IsWide(operand) ? EvaluateBoundNode(session, operand, context)
            : destination is not null ? Unvalued(EvaluateConverted(session, operand, destination, context))
            : Unvalued(EvaluateStored(session, operand, context));

    // a call of CByte, CInt, CLng, CLngLng, CCur or CBool of an extended intermediate, which converts it from its 80 bits (s42 C15, D12), or of Sgn, which
    // gives its sign; null for any other call.
    private RuntimeSemanticsEvaluationResult? EvaluateWideConversion(IRuntimeSession session, BoundCall call, RuntimeEvaluationContext context)
    {
        if (WideFunctionOf(call) is not { } function || !IsWide(function.Argument))
        {
            return null;
        }

        if (ExtendedIntermediates.ConversionOf(function.Key) is { } type)
        {
            return Unvalued(EvaluateConverted(session, function.Argument, type, context));
        }

        if (function.Key == "IStdMathModule.Sgn/1")
        {
            var argument = EvaluateWide(session, function.Argument, context);
            return argument.Failed ?? ResultOf(ExtendedIntermediates.SignOf(argument.Value, out var error), error, call);
        }

        return null;
    }
}
