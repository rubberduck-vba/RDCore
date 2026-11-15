using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.Runtime.LanguageCore.Plans;
using System.Numerics;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.Semantics.Operators;

public abstract record class BinaryRelationalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IErrorDetailsRecorder ErrorDetails)
    : BinaryOperatorRuntimeSemantics<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags>(LetCoercionSemanticsProvider, ErrorDetails)
{
    /// <summary>
    /// Compares two operands already let-coerced to <c>String</c>, as <paramref name="rules"/> say: whole values rather than their
    /// characters, since a trailing byte is part of what binary mode compares (<see cref="StringComparisonRules.Compare"/>).
    /// </summary>
    protected abstract bool ComparisonOp(VBStringValue lhs, VBStringValue rhs, StringComparisonRules rules);

    /// <summary>
    /// Compares two operands already let-coerced to the same numeric effective type, in that type's
    /// own CLR representation — generic math (<see cref="INumber{TSelf}"/>), no widening or boxing.
    /// </summary>
    protected abstract bool ComparisonOp<T>(T lhs, T rhs) where T : INumber<T>;

    // MS-VBAL 5.6.9.5: a comparison yields a Boolean, or Null when its effective type is Null.
    protected override VBType ResultTypeOf(VBType effectiveType)
        => effectiveType is VBNullType ? effectiveType : VBBooleanType.TypeInfo;

    /// <summary>
    /// Whether a Variant holding a number compares below a Variant holding a <c>String</c>, whatever their values (<strong>MS-VBAL 5.6.9.5</strong>).
    /// </summary>
    protected virtual bool RanksVariantNumberBelowString => true;

    /// <summary>
    /// Whether the operator compares the values of its operands, as the six relational operators do - and a <c>Variant</c> number with a <c>String</c> as text
    /// (<see cref="ComparisonOperatorSemanticFlags.IsVariantComparedWithStringAsText"/>) - rather than a pattern (<c>Like</c>) or two objects (<c>Is</c>).
    /// </summary>
    protected virtual bool ComparesValues => true;

    protected override OperatorAnalysisContext<ComparisonOperatorSemanticFlags> CreateAnalysisContext(
        SyntaxNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        ComparisonOperatorSemanticFlags semanticFlags)
        => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected sealed override OperatorFact? CreateFact(
        string token, ExpressionNode expression, VBType? effectiveType, ComparisonOperatorSemanticFlags flags,
        StringComparisonRules comparison, bool isValueKnown, VBErrorInfo? error)
        => new ComparisonOperatorFact(expression.Identity, expression.Location, token, effectiveType, isValueKnown, error, flags, comparison);

    protected sealed override OperatorFact WithOperands(OperatorFact fact, IRuntimeSymbolResolver resolver, ExpressionNode expression, VBTypedValue[] operands)
        => fact is ComparisonOperatorFact comparison && StringOperandOf(resolver, expression, operands) is { } operand
            ? comparison with { StringOperand = operand }
            : fact;

    // MS-VBAL 5.6.9.5: a String compared with a number is Let-coerced to the effective type of the table, which MS-VBA does not always compare it in (it
    // compares a String with a Byte, an Integer or a Long as Doubles). What the String is either way is stated, with the error each Let-coercion raises.
    private StringOperandFact? StringOperandOf(IRuntimeSymbolResolver resolver, ExpressionNode expression, VBTypedValue[] operands)
    {
        if (!ComparesValues || operands is not [var left, var right]
            || !IsStringAgainstAnother(left, right, out var text, out var number, out var stringIsLeft)
            || HeldValueOf(number) is not { TypeInfo: VBNumericType } held
            || OperationPlanner.SpecifiedRelationalEffectiveType(left.GetTargetType(), right.GetTargetType()) is not { } specified)
        {
            return null;
        }

        var known = number.IsIndeterminate ? null : held;
        if (text.IsIndeterminate)
        {
            return new StringOperandFact(stringIsLeft, specified.Name, null, null, null, null, known);
        }

        var asDouble = CoerceUnstated(resolver, expression, text, VBDoubleType.TypeInfo);
        var asSpecified = CoerceUnstated(resolver, expression, text, specified);
        return new StringOperandFact(stringIsLeft, specified.Name,
            asDouble.ErrorInfo is null ? asDouble.Result : null, asDouble.ErrorInfo?.ErrorId,
            asSpecified.ErrorInfo is null ? asSpecified.Result : null, asSpecified.ErrorInfo?.ErrorId,
            known);
    }

    private static VBTypedValue HeldValueOf(VBTypedValue value) => value is VBVariantValue { TypedValue: var held } ? held : value;

    protected override ISemanticContextContributor<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags> Analyze(
        IRuntimeSymbolResolver resolver, 
        ConversionOperationSemanticContext coercionContext, 
        ISemanticContextContributor<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags> builder, 
        ExpressionNode expression, 
        OperatorAnalysisContext<ComparisonOperatorSemanticFlags> analysisContext, params VBTypedValue[] operands)
    {
        // MS-VBAL 5.6.9.5: comparing two Error values is only defined when both hold a standard error code.
        if (analysisContext.EffectiveTypeResult.Result is VBErrorType
            && operands.OfType<VBErrorValue>().ToArray() is { Length: > 1 } errorOperands
            && errorOperands.All(error => error.Value >= VBErrorType.MinimumStdErrorValue && error.Value <= VBErrorType.MaximumStdErrorValue))
        {
            builder.AddFlags(ComparisonOperatorSemanticFlags.HasStandardErrorCodes);
        }

        if (operands.Any(operand => operand.TypeInfo is IFloatingPointNumericType
            && double.IsNaN(Convert.ToDouble(operand.RuntimeValue.BoxedValue))))
        {
            builder.AddFlags(ComparisonOperatorSemanticFlags.HasNaNOperand);
        }

        // only Variant operands have a subtype to look at. A VBVariantValue's own TypeInfo mirrors its
        // wrapped value's (see VBVariantValue's own remarks), never VBVariantType itself - read the
        // subtype off the actual wrapped value, not off TypeInfo.
        var variantSubTypes = operands.OfType<VBVariantValue>().Select(variant => variant.TypedValue.TypeInfo).ToArray();
        if (RanksVariantNumberBelowString
            && operands.Length == operands.OfType<VBVariantValue>().Count()
            && variantSubTypes.Any(subType => subType is VBStringType)
            && variantSubTypes.Any(IsRankedBelowAString))
        {
            builder.AddFlags(ComparisonOperatorSemanticFlags.IsVariantStringNumericException);
        }

        if (ComparesValues && operands is [var left, var right] && IsComparedAsText(left, right))
        {
            builder.AddFlags(ComparisonOperatorSemanticFlags.IsVariantComparedWithStringAsText);
        }

        if (operands is [var lhs, var rhs] && IsVariantStringThatIsNoValue(resolver, expression, lhs, rhs, out _))
        {
            builder.AddFlags(ComparisonOperatorSemanticFlags.IsVariantStringThatIsNoValueRanked);
        }

        return builder.AddFlags(analysisContext.EffectiveTypeResult.Result switch
        {
            VBBooleanType => ComparisonOperatorSemanticFlags.BooleanEffectiveType,
            VBByteType => ComparisonOperatorSemanticFlags.ByteEffectiveType | ComparisonOperatorSemanticFlags.IntegralNumericEffectiveType,
            VBIntegerType => ComparisonOperatorSemanticFlags.IntegerEffectiveType | ComparisonOperatorSemanticFlags.IntegralNumericEffectiveType,
            VBLongType => ComparisonOperatorSemanticFlags.LongEffectiveType | ComparisonOperatorSemanticFlags.IntegralNumericEffectiveType,
            VBLongLongType => ComparisonOperatorSemanticFlags.LongLongEffectiveType | ComparisonOperatorSemanticFlags.IntegralNumericEffectiveType,
            VBSingleType => ComparisonOperatorSemanticFlags.SingleEffectiveType | ComparisonOperatorSemanticFlags.FloatingPointNumericEffectiveType,
            VBDoubleType => ComparisonOperatorSemanticFlags.DoubleEffectiveType | ComparisonOperatorSemanticFlags.FloatingPointNumericEffectiveType,
            VBStringType => ComparisonOperatorSemanticFlags.StringEffectiveType
                | (analysisContext.Comparison.IgnoresCase
                    ? ComparisonOperatorSemanticFlags.StringComparisonText
                    : ComparisonOperatorSemanticFlags.StringComparisonBinary),
            VBCurrencyType => ComparisonOperatorSemanticFlags.CurrencyEffectiveType | ComparisonOperatorSemanticFlags.FixedPointNumericEffectiveType,
            VBDecimalType => ComparisonOperatorSemanticFlags.DecimalEffectiveType | ComparisonOperatorSemanticFlags.FixedPointNumericEffectiveType,
            VBNullType => ComparisonOperatorSemanticFlags.NullEffectiveType,
            VBErrorType => ComparisonOperatorSemanticFlags.ErrorEffectiveType,
            _ => 0
        });
    }

    // MS-VBAL 5.6.9.5's own exception to the effective-type table below: when BOTH operands have a
    // declared type of Variant, one originally holding a String value and the other a numeric value,
    // the numeric operand is considered less than (and never equal to) the String operand, regardless
    // of their actual values - the String need not even look like a number, so this must be detected
    // and handled BEFORE normal coercion ever runs (coercing a non-numeric-looking String to a number
    // to compare it is exactly what this exception exists to avoid). Both DetermineBinaryOperatorEffectiveType
    // and ValidateOperand check this, short-circuiting to a synthetic Integer rank (0 = numeric side,
    // 1 = String side) that the existing VBIntegerType evaluation branch below then compares for real,
    // via the concrete operator's own ComparisonOp - no new evaluation code needed.
    // MS-VBA ranks a Variant Boolean and a Variant Date below a Variant String as it ranks a number, where MS-VBAL 5.6.9.5 says a numeric value type, which
    // neither is (MS-VBAL 5.5.1.2.1): with A holding "40", A < b is False for b a Variant False, and A2 < d is False for A2 holding "1/1/1990" and d a Variant Date
    // of 2000 (recorded in Excel).
    private static bool IsRankedBelowAString(VBType subType) => subType is VBNumericType or VBBooleanType or VBDateType;

    private bool IsVariantStringNumericException(OperatorEvaluationFrame frame, out bool leftIsNumeric)
    {
        leftIsNumeric = false;
        if (!RanksVariantNumberBelowString
            || frame[InputIndex.BinaryLeftOperand] is not VBVariantValue left || frame[InputIndex.BinaryRightOperand] is not VBVariantValue right)
        {
            return false;
        }

        var leftType = left.TypedValue.TypeInfo;
        var rightType = right.TypedValue.TypeInfo;

        if (leftType is VBStringType && IsRankedBelowAString(rightType))
        {
            return true;
        }

        if (IsRankedBelowAString(leftType) && rightType is VBStringType)
        {
            leftIsNumeric = true;
            return true;
        }

        return false;
    }

    // MS-VBA compares a Variant holding a number, a Date or a Boolean with a String that is no Variant - one declared As String, a literal - as text: the
    // Variant's value is let-coerced to String, as Microsoft Learn's Comparison operators documents it ("One expression is a String and the other is any Variant
    // except a Null: Perform a string comparison"; Excel-measured, oracle\s41 V01-V04, W01-W08, W10 - `10 > "9"` is False). MS-VBAL 5.6.9.5's table makes the
    // effective type the number's, which makes a String that is no number a type mismatch (13): RD-VBA does what MS-VBA does, and the analysis states it
    // (IsVariantComparedWithStringAsText) for RDCore.Diagnostics to report (RDC11013). A Variant holding an Error is left to the table, which no oracle measured.
    private static bool IsComparedAsText(VBTypedValue left, VBTypedValue right)
        => IsTextComparedVariant(left) && right is VBStringValue || IsTextComparedVariant(right) && left is VBStringValue;

    private static bool IsTextComparedVariant(VBTypedValue operand)
        => operand is VBVariantValue { TypedValue.TypeInfo: VBNumericType or VBDateType or VBBooleanType };

    // MS-VBA compares a Variant holding a String with a number, a Date or a Boolean that is no Variant as the table says when the String is a value of the
    // effective type - "3" < 5 is True, "1/2/1900" < a Date of 5 is True, "True" = a Boolean True is True - and, when it is not, ranks the String above it
    // as it does for two Variants (IsVariantStringNumericException), raising nothing: "abc" > 5 is True, "" < 5 is False, from either side (Excel-measured,
    // oracle\s48 H01-H05, I01-I04, K01-K09, L01-L04). MS-VBAL 5.6.9.5's table, and Microsoft Learn's Comparison operators, make the String's conversion a type
    // mismatch (13): RD-VBA does what MS-VBA does, and the analysis states it (IsVariantStringThatIsNoValueRanked) for RDCore.Diagnostics to report
    // (RDC11020). A String declared as one is no Variant, and raises 13 (L02). Only a type mismatch is a String that is no value: an overflow is raised.
    private bool IsVariantStringThatIsNoValue(IRuntimeSymbolResolver resolver, ExpressionNode expression, OperatorEvaluationFrame frame, out bool leftIsNumeric)
        => IsVariantStringThatIsNoValue(resolver, expression, frame[InputIndex.BinaryLeftOperand], frame[InputIndex.BinaryRightOperand], out leftIsNumeric);

    // the String is let-coerced to the type the plan of the operation makes the effective type, without stating it (it is the question, not the operation).
    private bool IsVariantStringThatIsNoValue(IRuntimeSymbolResolver resolver, ExpressionNode expression, VBTypedValue left, VBTypedValue right, out bool leftIsNumeric)
    {
        leftIsNumeric = false;
        if (!RanksVariantNumberBelowString || !ComparesValues
            || !IsVariantStringAgainstAScalar(left, right, out var text, out leftIsNumeric)
            || text.IsIndeterminate
            || OperationPlanner.ForValues(Operator, left.GetTargetType(), right.GetTargetType()).EffectiveType is not { } effective
            || effective is VBStringType)
        {
            return false;
        }

        var coercion = new LetCoercionStackFrame(expression.Identity, InputIndex.CoercionSourceValue, text, new VBTypeDescValue(effective), ConversionSite.OperatorOperand);
        return LetCoercionSemanticsProvider.EvaluateUnstated(resolver, expression, coercion).ErrorInfo?.ErrorId == (int)VBRuntimeErrorId.TypeMismatch;
    }

    private static bool IsVariantStringAgainstAScalar(VBTypedValue left, VBTypedValue right, out VBStringValue text, out bool leftIsNumeric)
    {
        (text, leftIsNumeric) = (left, right) switch
        {
            (VBVariantValue { TypedValue: VBStringValue held }, _) when IsDeclaredScalar(right) => (held, false),
            (_, VBVariantValue { TypedValue: VBStringValue held }) when IsDeclaredScalar(left) => (held, true),
            _ => (null!, false),
        };

        return text is not null;
    }

    private static bool IsDeclaredScalar(VBTypedValue operand) => operand is not VBVariantValue && operand.TypeInfo is VBNumericType or VBDateType or VBBooleanType;

    protected override LetCoercionResult ValidateOperand(
        IRuntimeSymbolResolver resolver, ExpressionNode expression, OperatorEvaluationFrame frame, InputIndex index)
    {
        if (IsVariantStringNumericException(frame, out var leftIsNumeric) || IsVariantStringThatIsNoValue(resolver, expression, frame, out leftIsNumeric))
        {
            var isNumericSide = index == InputIndex.BinaryLeftOperand ? leftIsNumeric : !leftIsNumeric;
            return LetCoercionResult.Success(new VBIntegerValue(isNumericSide ? (short)0 : (short)1));
        }

        // MS-VBAL 5.6.9.5: two Errors are compared as the Errors they are, by their codes: CVErr(5) < CVErr(6) is True (recorded in Excel: oracle\s57
        // E02, E04). An Error is let-coerced to no type but Variant (5.5.1.2.9), and is not let-coerced to the effective type it already is.
        if (frame.EffectiveType is VBErrorType && frame[index] is VBErrorValue or VBVariantValue { TypedValue: VBErrorValue })
        {
            return LetCoercionResult.Success(frame[index] is VBVariantValue { TypedValue: VBErrorValue held } ? held : frame[index]);
        }

        if (IsTextOfADate(frame, frame[index]))
        {
            var date = LetCoercionSemanticsProvider.EvaluateLetCoercionSemantics(resolver, expression, new()
            {
                NodeId = expression.Identity,
                OperandIndex = index,
                SourceValue = frame[index],
                DestinationTypeDesc = new VBTypeDescValue(VBDateType.TypeInfo),
                Site = frame.Site,
            });

            return date.Result is VBDateValue value ? date with { Result = new VBDoubleValue(value.SerialValue) } : date;
        }

        return base.ValidateOperand(resolver, expression, frame, index);
    }

    protected override LetCoercionAnalysisContext AnalyzeValidateOperand(
        IRuntimeSymbolResolver resolver, ILetCoercionSemanticContextBuilder builder, ExpressionNode expression, OperatorEvaluationFrame frame, InputIndex operandIndex)
        => IsTextOfADate(frame, frame[operandIndex])
            ? AnalyzeOperandCoercion(resolver, builder, expression, frame[operandIndex], operandIndex, VBDateType.TypeInfo)
            : base.AnalyzeValidateOperand(resolver, builder, expression, frame, operandIndex);

    // MS-VBAL 5.6.9.5: a String compared with a Date has the effective type Date, and is let-coerced to it - the text of a date is read as one (MS-VBA:
    // a Variant "1/2/1900" < a Date of 5 is True, Excel-measured, oracle\s48 K04). The operands of a Date effective type are otherwise let-coerced to
    // Double, the serial they are compared by, which the text of a date is not: it is let-coerced to Date, and compared by its serial.
    private static bool IsTextOfADate(OperatorEvaluationFrame frame, VBTypedValue operand)
        => frame.EffectiveType is VBDateType && operand is VBStringValue or VBVariantValue { TypedValue: VBStringValue };

    /// <summary>
    /// The operator: one of the <c>Tokens</c> relational operators, which is what its <see cref="OperationPlan"/> is made for.
    /// </summary>
    protected abstract string Operator { get; }

    /// <remarks>
    /// The effective type is the operation's plan (<see cref="OperationPlanner"/>), made once for each pair of value types and kept - but for the comparison of
    /// two Variants that ranks a number below a <c>String</c>, and of a Variant <c>String</c> that is no value of the plan's type with a number, which depend on
    /// more than their types (<see cref="RanksVariantNumberBelowString"/>).
    /// </remarks>
    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        IRuntimeSymbolResolver resolver,
        BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags> context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
        => IsVariantStringNumericException(frame, out _) || IsVariantStringThatIsNoValue(resolver, expression, frame, out _)
            ? DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo)
            : ComparesValues && IsComparedAsText(frame[InputIndex.BinaryLeftOperand], frame[InputIndex.BinaryRightOperand])
                ? DetermineOperatorEffectiveTypeResult.Success(VBStringType.TypeInfo)
                : ComparesValues && IsVariantStringAgainstASingleOrALongLong(frame[InputIndex.BinaryLeftOperand], frame[InputIndex.BinaryRightOperand])
                    ? DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo)
                    : PlannedEffectiveType(expression, frame);

    // MS-VBA compares a Variant holding a String with a Single or a LongLong as Doubles, as it compares any String with a Byte, an Integer or a Long
    // (OperationPlanner): with v holding "0.1", v = a Single of 0.1 is False, and with v holding "9007199254740993", v = a LongLong of 9007199254740992 is
    // True (recorded in Excel: oracle\s56 H02-H04; RD-VBAL 5.6.9.5). A String declared as one is compared with a Single as MS-VBAL's table says (H02).
    private static bool IsVariantStringAgainstASingleOrALongLong(VBTypedValue left, VBTypedValue right)
        => IsVariantStringAgainstAScalar(left, right, out _, out var leftIsNumeric)
            && (leftIsNumeric ? left : right).TypeInfo is VBSingleType or VBLongLongType;

    private static bool IsStringAgainstAnother(VBTypedValue left, VBTypedValue right, out VBStringValue text, out VBTypedValue number, out bool stringIsLeft)
    {
        (text, number, stringIsLeft) = (HeldString(left), HeldString(right)) switch
        {
            ({ } held, null) => (held, right, true),
            (null, { } held) => (held, left, false),
            _ => (null!, null!, false),
        };

        return text is not null;
    }

    private static VBStringValue? HeldString(VBTypedValue operand) => operand switch
    {
        VBStringValue text => text,
        VBVariantValue { TypedValue: VBStringValue text } => text,
        _ => null,
    };

    private LetCoercionResult CoerceUnstated(IRuntimeSymbolResolver resolver, ExpressionNode expression, VBStringValue text, VBType destination)
        => LetCoercionSemanticsProvider.EvaluateUnstated(resolver, expression,
            new LetCoercionStackFrame(expression.Identity, InputIndex.CoercionSourceValue, text, new VBTypeDescValue(destination), ConversionSite.OperatorOperand));

    /// <summary>
    /// The effective type the plan of the operation says, for the value types of its operands.
    /// </summary>
    protected DetermineOperatorEffectiveTypeResult PlannedEffectiveType(ExpressionNode expression, OperatorEvaluationFrame frame)
        => EffectiveTypeOf(OperationPlanner.ForValues(
            Operator, frame[InputIndex.BinaryLeftOperand].GetTargetType(), frame[InputIndex.BinaryRightOperand].GetTargetType()), expression);

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        IRuntimeSymbolResolver resolver,
        BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags> context,
        ExpressionNode expression, 
        OperatorEvaluationFrame frame)
    {
        var lhs = frame.Operands[(int)InputIndex.BinaryLeftOperand];
        var rhs = frame.Operands[(int)InputIndex.BinaryRightOperand];

        // operands have been let-coerced to the effective type by the pipeline; each numeric
        // effective type compares in its own CLR representation via generic math (INumber<T>) —
        // no widening, no boxing through BoxedValue.
        if (frame.EffectiveType is VBByteType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBByteValue)lhs).Value, ((VBByteValue)rhs).Value)));
        }
        else if (frame.EffectiveType is VBIntegerType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBIntegerValue)lhs).Value, ((VBIntegerValue)rhs).Value)));
        }
        else if (frame.EffectiveType is VBLongType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBLongValue)lhs).Value, ((VBLongValue)rhs).Value)));
        }
        else if (frame.EffectiveType is VBLongLongType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBLongLongValue)lhs).Value, ((VBLongLongValue)rhs).Value)));
        }
        else if (frame.EffectiveType is VBBooleanType)
        {
            // Boolean compares over its -1/0 representation (RD-VBAL §5.0.2.1, same convention the
            // logical operators use); bool is not itself an INumber<T>, so it widens to long.
            var result = ComparisonOp(
                Convert.ToInt64(lhs.RuntimeValue.BoxedValue),
                Convert.ToInt64(rhs.RuntimeValue.BoxedValue));
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(result));
        }
        else if (frame.EffectiveType is VBStringType)
        {
            // MS-VBAL 5.6.9.5: in a module compared in text mode the strings are compared regardless of case, according to the
            // regional settings of the environment; otherwise, by "each byte of the implementation-specific representation of the
            // string data" - a trailing byte included, which is why the values are compared and not their characters.
            var result = ComparisonOp((VBStringValue)lhs, (VBStringValue)rhs, frame.Comparison);
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(result));
        }
        else if (frame.EffectiveType is VBCurrencyType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBCurrencyValue)lhs).Value.Value, ((VBCurrencyValue)rhs).Value.Value)));
        }
        else if (frame.EffectiveType is VBDecimalType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBDecimalValue)lhs).Value, ((VBDecimalValue)rhs).Value)));
        }
        else if (frame.EffectiveType is VBSingleType)
        {
            var lhsValue = ((VBSingleValue)lhs).Value;
            var rhsValue = ((VBSingleValue)rhs).Value;
            if (float.IsNaN(lhsValue) || float.IsNaN(rhsValue))
            {
                return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.Overflow, expression,
                    Exceptions.LetCoercionRuntimeErrorExceptionOverflow_Verbose));
            }
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(lhsValue, rhsValue)));
        }
        else if (frame.EffectiveType is VBDoubleType or VBDateType)
        {
            // a Date effective type compares the operands as they were let-coerced: to Double.
            var lhsValue = ((VBDoubleValue)lhs).Value;
            var rhsValue = ((VBDoubleValue)rhs).Value;
            if (double.IsNaN(lhsValue) || double.IsNaN(rhsValue))
            {
                return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.Overflow, expression,
                    Exceptions.LetCoercionRuntimeErrorExceptionOverflow_Verbose));
            }
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(lhsValue, rhsValue)));
        }
        else if (frame.EffectiveType is VBNullType)
        {
            return RuntimeSemanticsEvaluationResult.Success(VBNullValue.Null);
        }
        else if (frame.EffectiveType is VBErrorType)
        {
            // MS-VBAL 5.6.9.5: two standard error codes compare by their numeric value.
            return RuntimeSemanticsEvaluationResult.Success(
                new VBBooleanValue(ComparisonOp(((VBErrorValue)lhs).Value, ((VBErrorValue)rhs).Value)));
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }

    /// <summary>
    /// 💥 Creates and returns a new <see cref="RuntimeSemanticsEvaluationResult"/> with a <see cref="VBRuntimeErrorId.ObjectRequired"/> error.
    /// </summary>
    /// <param name="expression">The <em>binary arithmetic operator expression</em> whose <c>ResultSymbol</c> the error result will be attached to.</param>
    /// <param name="verbose">A detailed <c>Verbose</c> message about the error.</param>
    protected static RuntimeSemanticsEvaluationResult OnObjectRequired(ExpressionNode expression, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.ObjectRequired, expression.SourceLocation, verbose));
}