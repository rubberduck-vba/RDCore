using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using System.Diagnostics;

namespace RDCore.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3.1 Unary '-' Operator (runtime semantics)
/// </summary>
internal record class UnaryNegationOperatorRuntimeSemantics : UnaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType operand)
    {
        if (operand is VBByteType)
        {
            return VBIntegerType.TypeInfo;
        }

        return base.DetermineOperatorEffectiveType(operand);
    }

    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBOperatorExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        var operand = operands[0];
        
        if (effectiveType is VBByteType)
        {
            var depth = 0;
            if (operand is INumericCoercion coercibleNumeric && coercibleNumeric.AsCoercedDouble(ref depth) is VBNumericTypedValue coercedNumeric)
            {
                var doubleValue = 0 - coercedNumeric.NumericValue;
                return new VBByteValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBIntegerType)
        {
            var depth = 0;
            if (operand is INumericCoercion coercibleNumeric && coercibleNumeric.AsCoercedDouble(ref depth) is VBNumericTypedValue coercedNumeric)
            {
                var doubleValue = 0 - coercedNumeric.NumericValue;
                return new VBIntegerValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBLongType)
        {
            var depth = 0;
            if (operand is INumericCoercion coercibleNumeric && coercibleNumeric.AsCoercedDouble(ref depth) is VBNumericTypedValue coercedNumeric)
            {
                var doubleValue = 0 - coercedNumeric.NumericValue;
                return new VBLongValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBLongLongType)
        {
            var depth = 0;
            if (operand is INumericCoercion coercibleNumeric && coercibleNumeric.AsCoercedDouble(ref depth) is VBNumericTypedValue coercedNumeric)
            {
                var doubleValue = 0 - coercedNumeric.NumericValue;
                return new VBLongLongValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBSingleType)
        {
            var depth = 0;
            if (operand is INumericCoercion coercibleNumeric && coercibleNumeric.AsCoercedDouble(ref depth) is VBNumericTypedValue coercedNumeric)
            {
                var doubleValue = 0 - coercedNumeric.NumericValue;
                return new VBSingleValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBDoubleType)
        {
            var depth = 0;
            if (operand is INumericCoercion coercibleNumeric && coercibleNumeric.AsCoercedDouble(ref depth) is VBNumericTypedValue coercedNumeric)
            {
                var doubleValue = 0 - coercedNumeric.NumericValue;
                return new VBDoubleValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBCurrencyType)
        {
            var depth = 0;
            if (operand is INumericCoercion coercibleNumeric && coercibleNumeric.AsCoercedDouble(ref depth) is VBNumericTypedValue coercedNumeric)
            {
                var doubleValue = 0 - coercedNumeric.NumericValue;
                return new VBCurrencyValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBDecimalType)
        {
            var depth = 0;
            if (operand is INumericCoercion coercibleNumeric && coercibleNumeric.AsCoercedDouble(ref depth) is VBNumericTypedValue coercedNumeric)
            {
                var doubleValue = 0 - coercedNumeric.NumericValue;
                return new VBDecimalValue(expression.Symbol).WithValue(doubleValue);
            }
        }

        else if (effectiveType is VBDateType)
        {
            var depth = 0;
            if (operand is INumericCoercion coercibleNumeric && coercibleNumeric.AsCoercedDouble(ref depth) is VBDoubleValue coercedDouble)
            {
                // the Double value is the operand subtracted from 0.
                // the result is the Double value Let-coerced to Date.
                // if coercion to Date overflows and the operand is Variant, the result is the Double value.
                var doubleValue = 0 - coercedDouble.Value;
                if (operand is VBVariantValue && (doubleValue < VBDateValue.MinSerial || doubleValue > VBDateValue.MaxSerial))
                {
                    return new VBDoubleValue(expression.Symbol).WithValue(doubleValue);
                }

                return new VBDateValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        Debug.Assert(false); // something is wrong, we shouldn't be here.
        throw new InvalidOperationException();
    }
}
