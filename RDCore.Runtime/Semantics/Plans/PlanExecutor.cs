using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Plans;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Semantics.Plans;

/// <summary>
/// Executes the plans of operations and let-coercions (IR lowering §3.2, the execution half): the operands are let-coerced by the rule their
/// <see cref="ConversionPlan"/> names, straight through the <see cref="ValueConversions"/> primitive the rule's strategy evaluates, and the operation is
/// evaluated by its operator's own semantics, in the effective type its <see cref="OperationPlan"/> says.
/// </summary>
/// <remarks>
/// <para>
/// This is the path of the values a program computes with most - numbers, Booleans and dates - when nothing fails and nothing observes the evaluation. It is
/// the evaluation pipeline of <see cref="OperatorRuntimeSemantics{TContext, TFlags}"/> without what that path has no use for: deciding the effective type
/// again, validating each operand as an operation of its own, and the frames and results of each let-coercion, which are there to say why an operand was not
/// coerced, and to describe the coercion to an observer.
/// </para>
/// <para>
/// Anything else declines: an operand of another type (a <c>String</c>, <c>Empty</c>, <c>Null</c>, an object, an array), a value that is not known, a plan
/// that is a type mismatch or converts to anything but a number, a Boolean or a date, and a conversion that fails. The caller then evaluates the operation
/// the way it always has, and that evaluation raises the error, observes the coercions, and applies the rules that depend on more than the types of the
/// operands. The conversions are pure functions of the value, so a conversion evaluated once here, and again by the caller, is not seen twice.
/// </para>
/// </remarks>
internal static class PlanExecutor
{
    /// <summary>
    /// Evaluates an operation by its plan, if the operation is one the plan can be executed for.
    /// </summary>
    /// <param name="semantics">The semantics of the operator, which evaluate the operation in its effective type.</param>
    /// <param name="session">The session the operation is evaluated in.</param>
    /// <param name="context">The semantic context the operation is evaluated in.</param>
    /// <param name="token">The operator, which is what the plan is made for.</param>
    /// <param name="expression">The expression the operation is evaluated for.</param>
    /// <param name="site">The construct that asks for the operation.</param>
    /// <param name="operands">The operands, one or two, as the operation is given them.</param>
    /// <returns>The result of the operation - its value, or the run-time error the operator's semantics raise - or <see langword="null"/> when the plan
    /// cannot be executed and the operation is to be evaluated the way it always is.</returns>
    public static RuntimeSemanticsEvaluationResult? TryEvaluate<TContext, TFlags>(
        OperatorRuntimeSemantics<TContext, TFlags> semantics,
        IRuntimeSession session,
        TContext context,
        string token,
        ExpressionNode expression,
        ConversionSite site,
        VBTypedValue[] operands)
        where TContext : SemanticContext<TFlags>, new()
        where TFlags : struct, Enum
    {
        var values = new VBTypedValue[operands.Length];
        var hasVariantOperand = false;
        for (var index = 0; index < operands.Length; index++)
        {
            if (!TryGetPlainValue(operands[index], out var value))
            {
                return null;
            }

            values[index] = value;
            hasVariantOperand |= operands[index] is VBVariantValue;
        }

        var plan = values.Length == 2
            ? OperationPlanner.ForValues(token, values[0].TypeInfo, values[1].TypeInfo)
            : OperationPlanner.ForValue(token, values[0].TypeInfo);
        if (plan.EffectiveType is null || plan.OperandDestination is not { } destination)
        {
            return null;
        }

        for (var index = 0; index < values.Length; index++)
        {
            // an operand that already is of the type is the operand, unchanged: no coercion occurs.
            if (!values[index].TypeInfo.Equals(destination))
            {
                if (!TryConvertPlainValue(values[index], destination, out var converted))
                {
                    return null;
                }

                values[index] = converted;
            }
        }

        var result = semantics.EvaluatePlanned(session, context, expression, site, plan, values, hasVariantOperand);

        // an internal error is the pipeline's to report, with what it knows of the operation.
        return result.IsInternalError ? null : result;
    }

    /// <summary>
    /// Let-coerces a value to a declared type by the rule of its plan, if the value and the rule are ones the plan can be executed for.
    /// </summary>
    /// <param name="source">The value, as it is given: a Variant holding the value is unwrapped.</param>
    /// <param name="destination">The declared type the value is let-coerced to.</param>
    /// <param name="converted">The let-coerced value: what the rule's conversion makes of it, as when the coercion is evaluated by its strategy.</param>
    /// <returns><see langword="false"/> when the coercion is to be evaluated the way it always is - which is also how it raises its error.</returns>
    /// <remarks>
    /// A <c>String</c> let-coerced to a <c>String</c> is executed too - a copy of it, which the regional settings the String rule writes other values in have
    /// nothing to do with; any other value let-coerced to a <c>String</c> is not.
    /// </remarks>
    public static bool TryConvert(VBTypedValue source, VBType destination, [NotNullWhen(true)] out VBTypedValue? converted)
    {
        converted = null;
        return TryGetPlainValue(source, out var value, allowsString: true) && TryConvertPlainValue(value, destination, out converted);
    }

    /// <summary>
    /// Whether a plan converts to the declared type - a number, a Boolean, a date, an enumeration, a <c>String</c> or a <c>Variant</c> - which is whether a
    /// value let-coerced to it can be executed by its plan at all (<see cref="TryConvert"/>).
    /// </summary>
    /// <param name="destination">The declared type.</param>
    public static bool IsPlannedDestination(VBType destination)
        => destination is VBNumericType or VBBooleanType or VBDateType or VBEnumType or VBLongPtrType_x64 or VBLongPtrType_x86 or VBVariantType
            || destination is VBStringType and not VBFixedStringType;

    private static bool TryConvertPlainValue(VBTypedValue value, VBType destination, [NotNullWhen(true)] out VBTypedValue? converted)
    {
        var plan = ConversionPlanner.ForValue(value.TypeInfo, destination);
        var conversion = plan.Rule switch
        {
            ConversionRule.Numeric when plan.Destination is VBNumericType numeric => ValueConversions.ToNumeric(value, numeric),
            ConversionRule.Boolean => ValueConversions.ToBoolean(value),
            ConversionRule.Date => ValueConversions.ToDate(value),
            ConversionRule.String when value is VBStringValue => ValueConversions.ToText(value),
            // MS-VBAL 5.5.1.2.12: "a copy of the source value" - as it is now, and not a view of the variable it was read from.
            ConversionRule.Variant => ValueConversionResult.Success(new VBVariantValue(DataValues.Copy(value))),
            _ => ValueConversionResult.NotApplicable,
        };

        converted = conversion.IsSuccess ? conversion.Value : null;
        return converted is not null;
    }

    // a number, a Boolean or a date - or a String, where it is allowed - known, unwrapped from the Variant that holds it. A LongPtr is the value of what it
    // is an alias of, which its coercion makes of it first: it is left to the coercion.
    private static bool TryGetPlainValue(VBTypedValue operand, [NotNullWhen(true)] out VBTypedValue? value, bool allowsString = false)
    {
        value = operand;
        while (value is VBVariantValue variant)
        {
            if (variant.IsIndeterminate)
            {
                value = null;
                return false;
            }

            value = variant.TypedValue;
        }

        if (value.IsIndeterminate || value is VBLongPtrValue
            || value is not (VBNumericTypedValue or VBBooleanValue or VBDateValue) && !(allowsString && value is VBStringValue))
        {
            value = null;
            return false;
        }

        return true;
    }
}
