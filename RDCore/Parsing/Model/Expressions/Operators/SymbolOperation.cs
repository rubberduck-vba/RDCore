using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;
using RDCore.Parsing.Model.Values;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using RDCore.Server;
using System.Diagnostics;
using System.Text;

namespace RDCore.Parsing.Model.Expressions.Operators;

/// <summary>
/// A type hierarchy to compose the semantic layer and cleanly separate static and runtime semantics, to clean up <c>SymbolOperation</c>.
/// </summary>
internal abstract record class StaticSemantics
{
    public abstract VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes);
}

internal abstract record class ArithmeticOperatorStaticSemantics : StaticSemantics
{
}

/// <summary>
/// TODO derive static semantics for each operator; they only need to specify their respective exceptions, reading plainly like MS-VBAL.
/// </summary>
internal abstract record class UnaryArithmeticOperatorStaticSemantics : ArithmeticOperatorStaticSemantics
{
    public sealed override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes)
        => DetermineOperatorStaticType(operandDeclaredTypes[0]);

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="operand">The declared type of the operand.</param>
    /// <returns><c>null</c> if no type is statically valid.</returns>
    protected virtual VBType? DetermineOperatorStaticType(VBType operand)
    {
        return operand switch
        {
            VBByteType => VBByteType.TypeInfo,
            VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
            VBLongType => VBLongType.TypeInfo,
            VBLongLongType => VBLongLongType.TypeInfo,
            VBSingleType => VBSingleType.TypeInfo,
            VBDoubleType or VBStringType or VBFixedStringType => VBDoubleType.TypeInfo, // note: fixed string inherits string
            VBCurrencyType => VBCurrencyType.TypeInfo,
            VBDateType => VBDateType.TypeInfo,
            VBVariantType => VBVariantType.TypeInfo,
            _ => default
        };
    }
}

/// <summary>
/// MS-VBAL 5.6.9.3.1 Unary '-' Operator (static semantics)
/// </summary>
internal record class UnaryMinusOperatorStaticSemantics : UnaryArithmeticOperatorStaticSemantics
{
    protected override VBType? DetermineOperatorStaticType(VBType operand)
    {
        if (operand is VBByteType)
        {
            return VBIntegerType.TypeInfo;
        }

        return base.DetermineOperatorStaticType(operand);
    }
}

/// <summary>
/// MS-VBAL 5.6.9.3.1 Unary '-' Operator (runtime semantics)
/// </summary>
internal record class UnaryMinusOperatorRuntimeSemantics : UnaryOperatorRuntimeSemantics
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

/// <summary>
/// TODO derive static semantics for each operator; they only need to specify their respective exceptions, reading plainly like MS-VBAL.
/// </summary>
internal abstract record class BinaryArithmeticOperatorStaticSemantics : ArithmeticOperatorStaticSemantics
{
    public sealed override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes)
        => DetermineOperatorStaticType(operandDeclaredTypes[0], operandDeclaredTypes[1]);

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="lhs">The declared type of the LHS operand.</param>
    /// <param name="rhs">The declared type of the RHS operand.</param>
    /// <returns><c>null</c> if no type is statically valid.</returns>
    protected virtual VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBByteType when rhs is VBByteType => VBByteType.TypeInfo,

            VBBooleanType or VBIntegerType when rhs is VBByteType or VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType when lhs is VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,

            VBLongType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType => VBLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType when rhs is VBLongType => VBLongType.TypeInfo,

            VBLongLongType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType => VBLongLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType when rhs is VBLongLongType => VBLongLongType.TypeInfo,

            VBSingleType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType => VBSingleType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType when rhs is VBSingleType => VBSingleType.TypeInfo,

            VBSingleType when rhs is VBLongType or VBLongLongType => VBDoubleType.TypeInfo,
            VBLongType or VBLongLongType when rhs is VBSingleType => VBDoubleType.TypeInfo,

            VBDoubleType or VBStringType or VBFixedStringType when rhs is INumericType or VBStringType or VBFixedStringType => VBDoubleType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType when rhs is VBDoubleType or VBStringType or VBFixedStringType => VBDoubleType.TypeInfo,

            VBCurrencyType when rhs is INumericType or VBStringType or VBFixedStringType => VBCurrencyType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType when lhs is (VBCurrencyType) => VBCurrencyType.TypeInfo,

            VBDateType when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType => VBDateType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType or VBDateType when lhs is (VBDateType) => VBDateType.TypeInfo,

            VBVariantType when rhs is not (VBArrayType or VBUserDefinedType) => VBVariantType.TypeInfo,
            not (VBArrayType or VBUserDefinedType) when rhs is VBVariantType => VBVariantType.TypeInfo,

            _ => default
        };
    }
}

internal record class AdditionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    protected override VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        return base.DetermineOperatorStaticType(lhs, rhs);
    }
}


internal abstract record class RuntimeSemantics
{
    public abstract VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes);
    public VBTypedValue? Evaluate(VBExecutionContext context, VBOperatorExpression expression, params VBTypedValue[] operands)
    {
        var effectiveType = DetermineEffectiveType([.. operands.Select(op => op.TypeInfo)]);
        if (effectiveType is null)
        {
            // the operation is invalid
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }

        var validOperands = new List<VBTypedValue>();
        if (expression is VBBinaryOperatorExpression binaryOp)
        {
            CheckTypeMismatch(binaryOp, operands[0], operands[1]);
        }
        else
        {
            CheckTypeMismatch(expression, operands[0]);
        }

        foreach (var operand in operands) 
        {
            if (operand is not VBNullValue && LetCoerceNonNullOperand(effectiveType, operand) is VBTypedValue validOperand)
            {
                if (!validOperand.TypeInfo.Equals(operand.TypeInfo))
                {
                    if (operands.Length == 1)
                    {
                        context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(expression.Location.Range, operand.TypeInfo, validOperand.TypeInfo));
                    }
                    else if (expression is VBBinaryOperatorExpression op)
                    {
                        if (validOperands.Count == 0)
                        {
                            context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(op.Left.Location.Range, operand.TypeInfo, validOperand.TypeInfo));
                        }
                        else if (validOperands.Count == 1)
                        {
                            context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(op.Right.Location.Range, operand.TypeInfo, validOperand.TypeInfo));
                        }
                    }
                }
                validOperands.Add(validOperand);
            }
            else
            {
                validOperands.Add(operand);
            }
        }

        return EvaluateOperationResult(context, expression, effectiveType, [.. validOperands]);
    }

    protected abstract VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBOperatorExpression expression, VBType effectiveType, VBTypedValue[] operands);

    protected virtual void CheckTypeMismatch(VBOperatorExpression expression, VBTypedValue operand)
    {
        if (operand is VBArrayValue or VBErrorValue or VBUserDefinedTypeValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }
    }

    protected virtual void CheckTypeMismatch(VBBinaryOperatorExpression expression, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (lhs is VBArrayValue or VBErrorValue or VBUserDefinedTypeValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Left.Location.Range);
        }
        if (rhs is VBArrayValue or VBErrorValue or VBUserDefinedTypeValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Right.Location.Range);
        }
    }

    protected virtual VBTypedValue? LetCoerceNonNullOperand(VBType effectiveType, VBTypedValue operand)
    {
        VBTypedValue? letCoercedOperand = null;
        if (effectiveType is VBStringType && operand is IStringCoercion coercibleString)
        {
            var depth = 0;
            letCoercedOperand = coercibleString.AsCoercedString(ref depth);
        }
        if (effectiveType is VBBooleanType && operand is IBooleanCoercion coercibleBoolean)
        {
            var depth = 0;
            letCoercedOperand = coercibleBoolean.AsCoercedBoolean(ref depth);
        }
        if (effectiveType is VBDateType && operand is IDateCoercion coercibleDate)
        {
            var depth = 0;
            letCoercedOperand = coercibleDate.AsCoercedDate(ref depth)
                .AsCoercedDouble(ref depth); // date operands are let-coerced to Double before operation evaluation
        }
        if (effectiveType is INumericType && operand is INumericCoercion coercibleNumeric)
        {
            var depth = 0;
            letCoercedOperand = coercibleNumeric.AsCoercedDouble(ref depth);
        }

        return letCoercedOperand ?? operand;
    }
}

internal abstract record class ArithmeticOperatorRuntimeSemantics : RuntimeSemantics { }
internal abstract record class UnaryOperatorRuntimeSemantics : ArithmeticOperatorRuntimeSemantics
{
    public sealed override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
        => DetermineOperatorEffectiveType(operandDeclaredTypes[0]);

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (runtime semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="operand">The declared type of the operand.</param>
    /// <returns><c>null</c> if no type is statically valid.</returns>
    protected virtual VBType? DetermineOperatorEffectiveType(VBType operand)
    {
        return operand switch
        {
            VBByteType => VBByteType.TypeInfo,
            VBBooleanType or VBIntegerType or VBEmptyType => VBIntegerType.TypeInfo,
            VBLongType => VBLongType.TypeInfo,
            VBLongLongType => VBLongLongType.TypeInfo,
            VBSingleType => VBSingleType.TypeInfo,
            VBDoubleType or VBStringType => VBDoubleType.TypeInfo, // note: fixed string inherits string
            VBCurrencyType => VBCurrencyType.TypeInfo,
            VBDateType => VBDateType.TypeInfo,
            VBDecimalType => VBDecimalType.TypeInfo,
            VBNullType => VBNullType.TypeInfo,
            _ => default
        };
    }
}

internal abstract record class BinaryOperatorRuntimeSemantics : ArithmeticOperatorRuntimeSemantics
{
    public sealed override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
        => DetermineOperatorEffectiveType(operandDeclaredTypes[0], operandDeclaredTypes[1]);

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (runtime semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="lhs">The declared type of the operand on the left side of the operator.</param>
    /// <param name="rhs">The declared type of the operand on the right side of the operator.</param>
    /// <returns><c>null</c> if no type is statically valid.</returns>
    protected virtual VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBByteType when rhs is VBByteType or VBEmptyType => VBByteType.TypeInfo,
            VBByteType or VBEmptyType when rhs is VBByteType => VBByteType.TypeInfo,

            VBBooleanType or VBIntegerType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBEmptyType => VBIntegerType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBEmptyType when rhs is VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
            VBEmptyType when rhs is VBEmptyType => VBIntegerType.TypeInfo,

            VBLongType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBEmptyType => VBLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBEmptyType when rhs is VBLongType => VBLongType.TypeInfo,

            VBLongLongType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType or VBEmptyType => VBLongLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType or VBEmptyType when rhs is VBLongLongType => VBLongLongType.TypeInfo,

            VBSingleType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBEmptyType => VBSingleType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBEmptyType when rhs is VBSingleType => VBSingleType.TypeInfo,

            VBSingleType when rhs is VBLongType or VBLongLongType => VBDoubleType.TypeInfo,
            VBLongType or VBLongLongType when rhs is VBSingleType => VBDoubleType.TypeInfo,
            VBDoubleType or VBStringType when rhs is INumericType or VBStringType or VBEmptyType => VBDoubleType.TypeInfo,
            INumericType or VBStringType or VBEmptyType when rhs is VBDoubleType or VBStringType => VBDoubleType.TypeInfo,

            VBCurrencyType when rhs is INumericType or VBCurrencyType or VBStringType or VBEmptyType => VBCurrencyType.TypeInfo,
            INumericType or VBStringType or VBEmptyType when rhs is VBCurrencyType => VBCurrencyType.TypeInfo,

            // date values are let-coerced to VBDoubleValue
            VBDateType when rhs is INumericType or VBStringType or VBDateType or VBEmptyType => VBDateType.TypeInfo,
            INumericType or VBStringType or VBDateType or VBEmptyType when rhs is VBDateType => VBDateType.TypeInfo,

            VBDecimalType when rhs is INumericType or VBCurrencyType or VBStringType or VBEmptyType => VBCurrencyType.TypeInfo,
            INumericType or VBStringType or VBEmptyType when rhs is VBCurrencyType => VBCurrencyType.TypeInfo,

            VBNullType when rhs is INumericType or VBStringType or VBDateType or VBEmptyType or VBNullType => VBNullType.TypeInfo,
            INumericType or VBStringType or VBDateType or VBEmptyType or VBNullType when rhs is VBNullType => VBNullType.TypeInfo,

            VBErrorType when rhs is INumericType or VBStringType or VBDateType or VBEmptyType or VBErrorType => VBErrorType.TypeInfo,
            INumericType or VBStringType or VBDateType or VBEmptyType or VBErrorType when rhs is VBErrorType => VBErrorType.TypeInfo,

            _ => default
        };
    }
}

internal record class AdditionOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        if (lhs is VBStringType && rhs is VBStringType)
        {
            return VBStringType.TypeInfo;
        }

        return base.DetermineOperatorEffectiveType(lhs, rhs);
    }

    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBOperatorExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        var lhs = operands[0];
        var rhs = operands[1];
        var binaryOp = (VBBinaryOperatorExpression)expression;

        if (effectiveType is INumericType)
        {
            var depth = 0;
            var numericLhs = lhs is VBNumericTypedValue lhsNum 
                ? lhsNum.NumericValue 
                : lhs is INumericCoercion lhsCoercion
                    ? lhsCoercion.AsCoercedDouble(ref depth)?.NumericValue
                    : null;

            depth = 0;
            var numericRhs = rhs is VBNumericTypedValue rhsNum
                ? rhsNum.NumericValue
                : rhs is INumericCoercion rhsCoercion
                    ? rhsCoercion.AsCoercedDouble(ref depth)?.NumericValue
                    : null;

            if (lhs is not VBNumericTypedValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(binaryOp.Left.Location.Range, lhs.TypeInfo, VBDoubleType.TypeInfo));
            }
            if (rhs is not VBNumericTypedValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(binaryOp.Right.Location.Range, rhs.TypeInfo, VBDoubleType.TypeInfo));
            }

            if (numericLhs.HasValue && numericRhs.HasValue)
            {
                var result = numericLhs.Value + numericRhs.Value;
                return (VBTypedValue)effectiveType.CreateNumericValue(expression.Symbol).WithValue(result);
            }
        }
        else if (effectiveType is VBDateType)
        {
            var lhsDepth = 0;
            var rhsDepth = 0;
            if (lhs is INumericCoercion lhsCoercibleNumeric && lhsCoercibleNumeric.AsCoercedDouble(ref lhsDepth) is VBDoubleValue lhsCoercedDouble &&
                rhs is INumericCoercion rhsCoercibleNumeric && rhsCoercibleNumeric.AsCoercedDouble(ref rhsDepth) is VBDoubleValue rhsCoercedDouble)
            {
                if (lhs is VBDateValue)
                {
                    context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(binaryOp.Left.Location.Range));
                }
                if (rhs is VBDateValue)
                {
                    context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(binaryOp.Right.Location.Range));
                }

                // the Double value is the sum of the operands.
                // the result is the Double value Let-coerced to Date.
                // if coercion to Date overflows and either operand is Variant (or both), the result is the Double value.
                var doubleValue = lhsCoercedDouble.Value + rhsCoercedDouble.Value;
                if ((doubleValue < VBDateValue.MinSerial || doubleValue > VBDateValue.MaxSerial) &&
                    lhs is VBVariantValue || rhs is VBVariantValue)
                {
                    return new VBDoubleValue(expression.Symbol).WithValue(doubleValue);
                }

                return new VBDateValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBStringType)
        {
            var lhsDepth = 0;
            var rhsDepth = 0;
            if (lhs is IStringCoercion lhsCoercibleString && lhsCoercibleString.AsCoercedString(ref lhsDepth) is VBStringValue lhsCoercedString &&
                rhs is IStringCoercion rhsCoercibleString && rhsCoercibleString.AsCoercedString(ref rhsDepth) is VBStringValue rhsCoercedString)
            {
                if (lhs is VBStringValue && rhs is VBStringValue)
                {
                    context.AddDiagnostic(RDCoreDiagnostic.AmbiguousConcatenation(expression.Location.Range));
                }

                var result = $"{lhsCoercedString.Value}{rhsCoercedString.Value}";
                return new VBStringValue(expression.Symbol).WithValue(result);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}

internal static class SymbolOperation
{
    internal delegate VBTypedValue UnaryOperation(
        VBExecutionContext context,
        VBUnaryOperatorExpression operation,
        VBTypedValue value);

    internal delegate VBTypedValue BinaryOperation(
        VBExecutionContext context,
        VBBinaryOperatorExpression operation,
        VBTypedValue lhsValue,
        VBTypedValue rhsValue);

    private static readonly Dictionary<Uri, BinaryOperation> _binaryInstructions =
        GlobalSymbols.Operators.OfType<BinaryOperatorSymbol>()
        .ToDictionary(symbol => symbol.Uri, symbol => symbol.ExecuteBinaryOp);

    private static readonly Dictionary<Uri, UnaryOperation> _unaryInstructions =
        GlobalSymbols.Operators.OfType<UnaryOperatorSymbol>()
        .ToDictionary(symbol => symbol.Uri, symbol => symbol.ExecuteUnaryOp);

    public static BinaryOperation GetBinaryInstruction(Uri uri) => _binaryInstructions[uri];
    public static UnaryOperation GetUnaryInstruction(Uri uri) => _unaryInstructions[uri];

    private static VBType GetPromotedType(VBType lhs, VBType rhs)
    {
        // Type Promotion Hierarchy
        // Double > Single > Long > Integer > Byte
        if (lhs is VBDoubleType || rhs is VBDoubleType)
        {
            return VBDoubleType.TypeInfo;
        }
        if (lhs is VBSingleType || rhs is VBSingleType)
        {
            return VBSingleType.TypeInfo;
        }
        if (lhs is VBLongType || rhs is VBLongType)
        {
            return VBLongType.TypeInfo;
        }
        if (lhs is VBIntegerType || rhs is VBIntegerType)
        {
            return VBIntegerType.TypeInfo;
        }

        if (lhs is VBBooleanType && rhs is VBBooleanType)
        {
            return VBBooleanType.TypeInfo;
        }

        return VBByteType.TypeInfo;
    }

    private static VBTypedValue EvaluateUnaryOp(VBExecutionContext context,
        VBUnaryOperatorExpression expression,
        VBTypedValue value,
        Func<VBTypedValue, VBTypedValue> op)
    {
        // Null Propagation
        if (value is VBNullValue)
        {
            return VBNullValue.Null;
        }

        // Let-Coercion
        var coercionDepth = 0;
        var effectiveValue = value is VBObjectValue obj ? obj.LetCoerce(ref coercionDepth) : value;

        // Empty Coercion
        if (effectiveValue is VBEmptyValue)
        {
            effectiveValue = VBIntegerValue.Zero;
        }

        if (effectiveValue is not VBNumericTypedValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Symbol?.SelectionRange!);
        }

        // Unary + and - on Empty results in 0 (Integer)
        // Note: Parentheses on Empty stays Empty until an operator touches it.
        return op(effectiveValue);
    }

    private static VBTypedValue EvaluateNumericBinaryOp(VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs,
        Func<double, double, double> op,
        out VBNumericTypedValue lhsNumeric,
        out VBNumericTypedValue rhsNumeric,
        out VBType targetType,
        VBType? targetTypeOverride = default
        )
    {
        lhsNumeric = default!;
        rhsNumeric = default!;
        targetType = targetTypeOverride!;

        // MS-VBAL 5.5.1.2.10 Let-coercion from Null
        // if either operand is Null and the other is a UDT or resizable array value, throw a type mismatch.
        // MS-VBAL 5.6.9.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.
        // --- so, Null is irrelevant then - 5.6.9.3 takes precedence in the context of a binary operation.
        if (lhs is VBNullValue && (rhs is VBResizableArrayValue || rhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(rhs.Symbol?.Range!);
        }
        if (rhs is VBNullValue && (lhs is VBResizableArrayValue || lhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(lhs.Symbol!.Range!);
        }

        // ...otherwise if either operand is Null, the result is Null.
        if (lhs is VBNullValue || rhs is VBNullValue)
        {
            targetType = VBNullType.TypeInfo;
            return VBNullValue.Null;
        }

        // MS-VBAL 5.5.1.2.11 Let-coercion from Empty
        // If both operands are Empty, the result is an Integer 0.
        if (lhs is VBEmptyValue && rhs is VBEmptyValue)
        {
            targetType = VBIntegerType.TypeInfo;
            return VBIntegerValue.Zero;
        }

        // MS-VBAL 5.6.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.

        if (lhs is VBErrorValue && rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }
        if (lhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Left.Location.Range);
        }
        if (rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Right.Location.Range);
        }

        // Numeric String Conversions
        // If one operand is a String and the other is a numeric, the String operand is converted to a Double.
        // RDC00109 is issued for each such implicit coercion, twice if both sides require numeric coercion.
        var lhsCoercionDepth = 0;
        lhsNumeric = lhs.TypeInfo is INumericType ? (VBNumericTypedValue)lhs : ((INumericCoercion)lhs).AsCoercedDouble(ref lhsCoercionDepth)!;

        var rhsCoercionDepth = 0;
        rhsNumeric = rhs.TypeInfo is INumericType ? (VBNumericTypedValue)rhs : ((INumericCoercion)rhs).AsCoercedDouble(ref rhsCoercionDepth)!;

        // determine the target type
        if (lhs is VBBooleanValue && rhs is VBBooleanValue)
        {
            targetType = VBBooleanType.TypeInfo;
        }
        else if (targetType == default)
        {
            targetType = GetPromotedType(lhsNumeric.TypeInfo, rhsNumeric.TypeInfo);
        }

        if (lhs is VBStringValue)
        {
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(expression.Left.Location.Range, lhs.TypeInfo, targetType));
        }

        if (rhs is VBStringValue)
        {
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(expression.Right.Location.Range, rhs.TypeInfo, targetType));
        }

        // calculate the numeric result
        var resultValue = op(lhsNumeric.NumericValue, rhsNumeric.NumericValue);

        // Overflow checks [VBR0006]
        if (targetType is INumericType)
        {
            return (VBTypedValue)((VBNumericTypedValue)targetType.CreateValue(expression.Symbol))
                .WithValue(resultValue);
        }
        else
        {
            return ((VBBooleanValue)targetType.CreateValue(expression.Symbol))
                .WithValue(resultValue);
        }
    }


    public static VBTypedValue EvaluateBinaryConcat(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        if (lhs is VBNullValue && rhs is VBNullValue)
        {
            return VBNullValue.Null;
        }
        else if (lhs is VBNullValue)
        {
            lhs = VBStringValue.VBNullString;
        }
        else if (rhs is VBNullValue)
        {
            rhs = VBStringValue.VBNullString;
        }

        VBStringValue lhsString = default!;
        if (lhs is VBResizableArrayValue lhsArray && lhsArray.ItemType == VBByteType.TypeInfo)
        {
            // MS-VBAL 5.5.1.2.6 Let-coercion to and from resizable Byte()
            // TODO verify about fixed-size Byte()
            var bytes = lhsArray.Dimensions[0].State
                .OfType<VBByteValue>()
                .Select(e => e.Value)
                .ToArray();

            // spec says it's implementation-defined, but also that
            // let-coercion from Byte() array should be through StrConv (stdlib).
            // so... TODO here.
            var value = Encoding.ASCII.GetString(bytes);
            lhsString = new VBStringValue(lhs.Symbol).WithValue(value);
        }

        if (lhs is VBStringValue lhsStringValue)
        {
            lhsString = lhsStringValue;
        }
        else if(lhs is IStringCoercion lhsCoercible)
        {
            var depth = 0;
            if (lhsCoercible.AsCoercedString(ref depth) is VBStringValue lhsCoerced)
            {
                // do we issue a diagnostic here?
                //context.AddDiagnostic(RDCoreDiagnostic.ImplicitStringCoercion(expression.Left.Location.Range, lhs.TypeInfo));
                lhsString = lhsCoerced;
            }
        }
        if (lhsString == default)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Left.Location.Range);
        }

        VBStringValue rhsString = default!;
        if (rhs is VBResizableArrayValue rhsArray && rhsArray.ItemType == VBByteType.TypeInfo)
        {
            // MS-VBAL 5.5.1.2.6 Let-coercion to and from resizable Byte()
            // TODO verify about fixed-size Byte()
            var bytes = rhsArray.Dimensions[0].State
                .OfType<VBByteValue>()
                .Select(e => e.Value)
                .ToArray();

            // spec says it's implementation-defined, but also that
            // let-coercion from Byte() array should be through StrConv (stdlib).
            // so... TODO here.
            var value = Encoding.ASCII.GetString(bytes);
            rhsString = new VBStringValue(rhs.Symbol).WithValue(value);
        }

        if (rhs is VBStringValue rhsStringValue)
        {
            rhsString = rhsStringValue;
        }
        else if (rhs is IStringCoercion rhsCoercible)
        {
            var depth = 0;
            if (rhsCoercible.AsCoercedString(ref depth) is VBStringValue rhsCoerced)
            {
                // do we issue a diagnostic here?
                //context.AddDiagnostic(RDCoreDiagnostic.ImplicitStringCoercion(expression.Right.Location.Range, rhs.TypeInfo));
                rhsString = rhsCoerced;
            }
        }
        if (rhsString == default)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Right.Location.Range);
        }

        if (lhsString != default && rhsString != default)
        {
            var result = $"{lhsString.Value}{rhsString.Value}";
            return new VBStringValue(expression.Symbol).WithValue(result);
        }

        throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
    }

    public static VBTypedValue EvaluateBinaryAddition(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        VBNumericTypedValue lhsNumeric;
        VBNumericTypedValue rhsNumeric;

        if (lhs is VBStringValue && rhs is VBStringValue)
        {
            // if both operands are strings, then this is a concatenation, not an addition.
            // a diagnostic is issued for the ambiguous concatenation operator usage.
            context.AddDiagnostic(RDCoreDiagnostic.AmbiguousConcatenation(expression.Symbol.Range!));
        }

        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => left + right,
            out lhsNumeric,
            out rhsNumeric,
            out var targetType);

        // Date Math Rule
        // "If one operand is a Date and the other is numeric, the result is a Date."
        if (resultValue is not VBNullValue && (lhs.TypeInfo is VBDateType || rhs.TypeInfo is VBDateType))
        {
            // Date math is effectively Double math re-wrapped
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Symbol?.Range!));
            return new VBDateValue(expression.Symbol).WithValue(((VBNumericTypedValue)resultValue).NumericValue);
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinarySubtraction(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => left - right,
            out var lhsNumeric,
            out var rhsNumeric,
            out var targetType);

        // Date Math Rule
        // "If one operand is a Date and the other is numeric, the result is a Date."
        if (resultValue is not VBNullValue && (lhs.TypeInfo is VBDateType || rhs.TypeInfo is VBDateType))
        {
            // Date math is effectively Double math re-wrapped
            var diff = lhsNumeric.NumericValue - rhsNumeric.NumericValue;
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Symbol?.Range!));

            // If both are dates, return Double. If only one is a date, return Date.
            return (lhs.TypeInfo is VBDateType && rhs.TypeInfo is VBDateType)
                ? new VBDoubleValue(expression.Symbol).WithValue(diff)
                : new VBDateValue(expression.Symbol).WithValue(diff);
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinaryMultiplication(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => left * right,
            out var lhsNumeric,
            out var rhsNumeric,
            out var targetType);

        // Date Math Rule
        // "If one operand is a Date and the other is numeric, the result is a Date."
        if (resultValue is not VBNullValue && (lhs.TypeInfo is VBDateType || rhs.TypeInfo is VBDateType))
        {
            // Date math is effectively Double math re-wrapped
            var diff = lhsNumeric.NumericValue - rhsNumeric.NumericValue;
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Symbol?.Range!));

            // If both are dates, return Double. If only one is a date, return Date.
            return new VBDoubleValue(expression.Symbol).WithValue(diff);
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinaryExponentiation(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        static double func(double left, double right) => Math.Pow(left, right);

        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
             func,
             out var lhsNumeric,
             out var rhsNumeric,
             out var targetType,
             VBDoubleType.TypeInfo);

        if (resultValue is not VBNullValue)
        {
            if (lhs.TypeInfo is VBDateType)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Left.Location.Range));
            }
            if (rhs.TypeInfo is VBDateType)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Right.Location.Range));
            }
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinaryDivision(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        // MS-VBAL 5.5.1.2.10 Let-coercion from Null
        // if either operand is Null and the other is a UDT or resizable array value, throw a type mismatch.
        // MS-VBAL 5.6.9.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.
        // --- so, Null is irrelevant then - 5.6.9.3 takes precedence in the context of a binary operation.
        if (lhs is VBNullValue && (rhs is VBResizableArrayValue || rhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(rhs.Symbol?.Range!);
        }
        if (rhs is VBNullValue && (lhs is VBResizableArrayValue || lhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(lhs.Symbol!.Range!);
        }

        // ...otherwise if either operand is Null, the result is Null.
        if (lhs is VBNullValue || rhs is VBNullValue)
        {
            return VBNullValue.Null;
        }

        // MS-VBAL 5.5.1.2.11 Let-coercion from Empty
        // If both operands are Empty, the result is an Integer 0.
        if (lhs is VBEmptyValue && rhs is VBEmptyValue)
        {
            return VBIntegerValue.Zero;
        }

        // MS-VBAL 5.6.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.

        if (lhs is VBErrorValue && rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }
        if (lhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Left.Location.Range);
        }
        if (rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Right.Location.Range);
        }

        var lhsCoercionDepth = 0;
        var lhsNumeric = lhs.TypeInfo is INumericType ? (VBNumericTypedValue)lhs : ((INumericCoercion)lhs).AsCoercedDouble(ref lhsCoercionDepth)!;

        var rhsCoercionDepth = 0;
        var rhsNumeric = rhs.TypeInfo is INumericType ? (VBNumericTypedValue)rhs : ((INumericCoercion)rhs).AsCoercedDouble(ref rhsCoercionDepth)!;

        if (rhsNumeric.NumericValue == 0)
        {
            throw VBRuntimeErrorException.DivisionByZero(expression.Location.Range);
        }

        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
             (left, right) => left / right,
             out lhsNumeric,
             out rhsNumeric,
             out var targetType,
             VBDoubleType.TypeInfo);

        if (resultValue is not VBNullValue)
        {
            if (lhs.TypeInfo is VBDateType)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Left.Location.Range));
            }
            if (rhs.TypeInfo is VBDateType)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Right.Location.Range));
            }
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinaryIntegerDivision(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        // MS-VBAL 5.5.1.2.10 Let-coercion from Null
        // if either operand is Null and the other is a UDT or resizable array value, throw a type mismatch.
        // MS-VBAL 5.6.9.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.
        // --- so, Null is irrelevant then - 5.6.9.3 takes precedence in the context of a binary operation.
        if (lhs is VBNullValue && (rhs is VBResizableArrayValue || rhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(rhs.Symbol?.Range!);
        }
        if (rhs is VBNullValue && (lhs is VBResizableArrayValue || lhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(lhs.Symbol!.Range!);
        }

        // ...otherwise if either operand is Null, the result is Null.
        if (lhs is VBNullValue || rhs is VBNullValue)
        {
            return VBNullValue.Null;
        }

        // MS-VBAL 5.5.1.2.11 Let-coercion from Empty
        // If both operands are Empty, the result is an Integer 0.
        if (lhs is VBEmptyValue && rhs is VBEmptyValue)
        {
            return VBIntegerValue.Zero;
        }

        // MS-VBAL 5.6.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.

        if (lhs is VBErrorValue && rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }
        if (lhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Left.Location.Range);
        }
        if (rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Right.Location.Range);
        }

        var lhsCoercionDepth = 0;
        var lhsNumeric = lhs.TypeInfo is INumericType ? (VBNumericTypedValue)lhs : ((INumericCoercion)lhs).AsCoercedDouble(ref lhsCoercionDepth)!;

        var rhsCoercionDepth = 0;
        var rhsNumeric = rhs.TypeInfo is INumericType ? (VBNumericTypedValue)rhs : ((INumericCoercion)rhs).AsCoercedDouble(ref rhsCoercionDepth)!;

        var actualRHS = (int)Math.Round(rhsNumeric?.NumericValue ?? 0, 0, MidpointRounding.ToEven);
        if (actualRHS == 0)
        {
            throw VBRuntimeErrorException.DivisionByZero(expression.Location.Range);
        }

        static int func(double left, double right) => (int)Math.Round(left, 0, MidpointRounding.ToEven) / (int)Math.Round(right, 0, MidpointRounding.ToEven);

        // integer division effective value type breaks all the rules!
        VBType? typeOverride = lhs switch
        {
            VBByteValue 
                when rhs is VBEmptyValue => VBIntegerType.TypeInfo,
            
            VBEmptyValue 
                when rhs is VBByteValue => VBIntegerType.TypeInfo,
            
            VBBooleanValue or VBIntegerValue 
                when rhs is VBNumericTypedValue and not VBLongLongValue => VBIntegerType.TypeInfo,
            
            VBIntegerValue or VBDateValue
                when rhs is VBDateValue => VBLongType.TypeInfo,

            VBStringValue 
            or VBDateValue 
            or VBSingleValue 
            or VBDoubleValue 
            or VBDecimalValue 
                when rhs is VBNumericTypedValue and not VBLongLongValue => VBLongType.TypeInfo,

            VBNumericTypedValue and not VBLongLongValue 
                when rhs is VBSingleValue 
                         or VBDoubleValue 
                         or VBDecimalValue 
                         or VBStringValue 
                         or VBDateValue => VBLongType.TypeInfo,

            VBLongLongValue 
                when rhs is VBNumericTypedValue 
                         or VBStringValue 
                         or VBDateValue 
                         or VBEmptyValue => VBLongLongType.TypeInfo,

            VBNumericTypedValue 
            or VBStringValue 
            or VBDateValue 
            or VBEmptyValue 
                when rhs is VBLongLongValue => VBLongLongType.TypeInfo,

            // otherwise we let the regular binary operator rules apply.. whatever is left of them.
            _ => default
        };

        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => func(left, right),
            out lhsNumeric,
            out rhsNumeric,
            out var targetType,
            typeOverride);

        // Date Math Rule
        // "If one operand is a Date and the other is numeric, the result is a Date."
        if (lhs.TypeInfo is VBDateType)
        {
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Left.Location.Range));
        }
        if (rhs.TypeInfo is VBDateType)
        {
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Right.Location.Range));
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinaryModulo(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        static double func(double left, double right) => Math.DivRem((int)left, (int)right).Remainder;
        // MS-VBAL 5.5.1.2.10 Let-coercion from Null
        // if either operand is Null and the other is a UDT or resizable array value, throw a type mismatch.
        // MS-VBAL 5.6.9.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.
        // --- so, Null is irrelevant then - 5.6.9.3 takes precedence in the context of a binary operation.
        if (lhs is VBNullValue && (rhs is VBResizableArrayValue || rhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(rhs.Symbol?.Range!);
        }
        if (rhs is VBNullValue && (lhs is VBResizableArrayValue || lhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(lhs.Symbol!.Range!);
        }

        // ...otherwise if either operand is Null, the result is Null.
        if (lhs is VBNullValue || rhs is VBNullValue)
        {
            return VBNullValue.Null;
        }

        // MS-VBAL 5.5.1.2.11 Let-coercion from Empty
        // If both operands are Empty, the result is an Integer 0.
        if (lhs is VBEmptyValue && rhs is VBEmptyValue)
        {
            return VBIntegerValue.Zero;
        }

        // MS-VBAL 5.6.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.

        if (lhs is VBErrorValue && rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }
        if (lhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Left.Location.Range);
        }
        if (rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Right.Location.Range);
        }

        var lhsCoercionDepth = 0;
        var lhsNumeric = lhs.TypeInfo is INumericType ? (VBNumericTypedValue)lhs : ((INumericCoercion)lhs).AsCoercedDouble(ref lhsCoercionDepth)!;

        var rhsCoercionDepth = 0;
        var rhsNumeric = rhs.TypeInfo is INumericType ? (VBNumericTypedValue)rhs : ((INumericCoercion)rhs).AsCoercedDouble(ref rhsCoercionDepth)!;

        var actualRHS = (int)Math.Round(rhsNumeric?.NumericValue ?? 0, 0, MidpointRounding.ToEven);
        if (actualRHS == 0)
        {
            throw VBRuntimeErrorException.DivisionByZero(expression.Location.Range);
        }

        // integer division effective value type breaks all the rules!
        VBType? typeOverride = lhs switch
        {
            VBByteValue
                when rhs is VBEmptyValue => VBIntegerType.TypeInfo,

            VBEmptyValue
                when rhs is VBByteValue => VBIntegerType.TypeInfo,

            VBBooleanValue or VBIntegerValue
                when rhs is VBNumericTypedValue and not VBLongLongValue => VBIntegerType.TypeInfo,

            VBIntegerValue or VBDateValue
                when rhs is VBDateValue => VBLongType.TypeInfo,

            VBStringValue
            or VBDateValue
            or VBSingleValue
            or VBDoubleValue
            or VBDecimalValue
                when rhs is VBNumericTypedValue and not VBLongLongValue => VBLongType.TypeInfo,

            VBNumericTypedValue and not VBLongLongValue
                when rhs is VBSingleValue
                         or VBDoubleValue
                         or VBDecimalValue
                         or VBStringValue
                         or VBDateValue => VBLongType.TypeInfo,

            VBLongLongValue
                when rhs is VBNumericTypedValue
                         or VBStringValue
                         or VBDateValue
                         or VBEmptyValue => VBLongLongType.TypeInfo,

            VBNumericTypedValue
            or VBStringValue
            or VBDateValue
            or VBEmptyValue
                when rhs is VBLongLongValue => VBLongLongType.TypeInfo,

            // otherwise we let the regular binary operator rules apply.. whatever is left of them.
            _ => default
        };

        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => func(left, right),
            out lhsNumeric,
            out rhsNumeric,
            out var targetType,
            typeOverride);

        // Date Math Rule
        // "If one operand is a Date and the other is numeric, the result is a Date."
        if (lhs.TypeInfo is VBDateType)
        {
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Left.Location.Range));
        }
        if (rhs.TypeInfo is VBDateType)
        {
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Right.Location.Range));
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinaryIsRefEquality(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        if (lhs is not VBObjectValue lObj || rhs is not VBObjectValue rObj)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }

        var result = lObj.RawAddress == rObj.RawAddress;
        return new VBBooleanValue(expression.Symbol).WithValue(result);
    }

    public static VBTypedValue EvaluateUnaryParentheses(
        VBExecutionContext context,
        VBUnaryOperatorExpression expression,
        VBTypedValue value) =>
    EvaluateUnaryOp(context, expression, value, value => value);

    // NOTE: no-op on the value, but forces null/empty propagation and let coercions.
    public static VBTypedValue EvaluateUnaryPlus(
        VBExecutionContext context,
        VBUnaryOperatorExpression expression,
        VBTypedValue value) => EvaluateUnaryOp(context, expression, value, value => value);

    public static VBTypedValue EvaluateUnaryMinus(
        VBExecutionContext context,
        VBUnaryOperatorExpression expression,
        VBTypedValue value)
    {
        var validValue = (VBNumericTypedValue)EvaluateUnaryOp(context, expression, value, value => value);
        return (VBTypedValue)validValue.WithValue(-validValue.NumericValue);
    }

    public static VBTypedValue EvaluateUnaryBitwiseNot(
        VBExecutionContext context,
        VBUnaryOperatorExpression expression,
        VBTypedValue value) =>
    EvaluateUnaryOp(context, expression, value, v =>
    {
        var depth = 0;
        var num = ((VBNumericTypedValue)v).AsCoercedDouble(ref depth);
        return num.WithValue(~(long)num.NumericValue);
    });

    public static VBTypedValue EvaluateBinaryBitwiseAnd(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs)
    {
        var result = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => (long)left & (long)right,
            out var lhsNumeric,
            out var rhsNumeric,
            out var targetType);

        var numericResult = result as VBNumericTypedValue;
        var depth = 0;
        var coercedResult = (result as INumericCoercion)?.AsCoercedDouble(ref depth);
        {
            if (numericResult is null && coercedResult != null)
            {
                var diagnostic = RDCoreDiagnostic.ImplicitNumericCoercion(expression.Symbol.SelectionRange!, result.TypeInfo, coercedResult.TypeInfo);
                context.AddDiagnostic(diagnostic);
            }
        }

        return targetType switch
        {
            VBBooleanType => new VBBooleanValue(expression.Symbol).WithValue(coercedResult!.AsBoolean().Value),
            INumericType numericType => (VBTypedValue)targetType.CreateNumericValue(expression.Symbol).WithValue(numericResult!.NumericValue),
            _ => throw new InvalidOperationException($"Unexpected bitwise result type: {targetType.Name}")
        };
    }

    public static VBTypedValue EvaluateBinaryBitwiseOr(
    VBExecutionContext context,
    VBBinaryOperatorExpression expression,
    VBTypedValue lhs,
    VBTypedValue rhs) => EvaluateNumericBinaryOp(context, expression, lhs, rhs,
        (left, right) => (long)left | (long)right,
        out var lhsNumeric,
        out var rhsNumeric,
        out var targetType);

    public static VBTypedValue EvaluateBinaryBitwiseXOr(
    VBExecutionContext context,
    VBBinaryOperatorExpression expression,
    VBTypedValue lhs,
    VBTypedValue rhs) => EvaluateNumericBinaryOp(context, expression, lhs, rhs,
        (left, right) => (long)left ^ (long)right,
        out var lhsNumeric,
        out var rhsNumeric,
        out var targetType);

    public static VBTypedValue EvaluateBinaryBitwiseImp(
    VBExecutionContext context,
    VBBinaryOperatorExpression expression,
    VBTypedValue lhs,
    VBTypedValue rhs) => EvaluateNumericBinaryOp(context, expression, lhs, rhs,
        (left, right) => ~(long)left | ~(long)right,
        out var lhsNumeric,
        out var rhsNumeric,
        out var targetType);

    public static VBTypedValue EvaluateBinaryBitwiseEqv(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs) => EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => ~((long)left ^ (long)right),
            out var lhsNumeric,
            out var rhsNumeric,
            out var targetType);

    public static VBTypedValue EvaluateBinaryMemberAccess(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs)
    {
        if (lhs.TypeInfo is VBVariantType)
        {
            context.AddDiagnostic(RDCoreDiagnostic.LateBoundMemberAccess(lhs.Symbol?.Range!));
        }

        if (lhs is IVBMemberOwnerType lhsOwner)
        {
            if (rhs.Symbol is not null)
            {
                var members = lhsOwner.Members.ToLookup(e => e.Name);
                var candidates = members[rhs.Symbol.Name];
                if (candidates.Any())
                {
                    // TODO make this statically deterministic, not based on where we found it in the source document.
                    var member = candidates.OrderBy(e => e.Uri).First();
                    var value = context.Memory.GetValue(member);

                    return value;
                }
                else
                {
                    // NOTE: LHS member owner could be a class, a stdmodule, an enum, or a UDT.
                    if (lhsOwner is not VBClassType lhsClassType)
                    {
                        throw VBCompileErrorException.MethodOrDataMemberNotFound(rhs.Symbol.SelectionRange!);
                    }

                    // if LHS is a class type, let's be nice and work with a deferred member instead:
                    return new VBDeferredMemberValue(expression.Symbol)
                           .WithContext(lhs)
                           .WithName(rhs.Symbol.Name)
                           .WithDiagnostic(RDCoreDiagnostic.UnresolvedLateBoundMemberAccess(rhs.Symbol.SelectionRange!));
                }
            }
            else
            {
                // user has typed the dot, but not the member name yet.
                // the members should be returned to the client to populate a completion list.
                // we're using VBVoidValue here to signal this:
                return VBVoidValue.Void;
            }
        }
        else
        {
            // Given a `NonExistingModule.NonExistingMember` member call where neither is defined:
            if (context.CurrentScope.ScopeSymbol is VBTypeMemberSymbol)
            {
                // VBA throws a compile error (variable not defined) if the code is inside the editor (scoped context)
                throw VBCompileErrorException.VariableNotDefined(lhs.Symbol?.SelectionRange!);
            }
            else
            {
                // VBA throws a runtime error (VBR00424 object required) if the same code is inside the immediate pane (default context)
                throw VBRuntimeErrorException.ObjectRequired(lhs.Symbol?.SelectionRange!);
            }
        }

        throw VBCompileErrorException.SyntaxError(expression.Location.Range, "An identifier is expected");
    }

    public static VBTypedValue EvaluateBinaryDictionaryAccess(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBStringValue rhs)
    {
        context.AddDiagnostic(RDCoreDiagnostic.LateBoundMemberAccess(expression.Symbol.SelectionRange!));

        if (lhs is not IVBMemberOwnerType lhsOwner ||
            lhsOwner.Members.FirstOrDefault(member => member.Get(SymbolProperties.UserMemId) == 0) is not VBTypeMemberSymbol defaultMember)
        {
            throw VBRuntimeErrorException.ObjectDoesntSupportPropertyOrMethod(lhs.Symbol?.SelectionRange!);
        }

        if (rhs.Symbol is null)
        {
            // user has typed the bang, but not the member name yet (NOTE: it may have been parsed as a type hint)
            // the members could be returned to the client to populate a completion list.
            // we're using VBVoidValue here to signal this:
            return VBVoidValue.Void;
        }

        return new VBDeferredMemberValue(expression.Symbol)
               .WithContext(lhs)
               .WithName(rhs.Symbol.Name)
               .WithDiagnostic(RDCoreDiagnostic.UnresolvedLateBoundMemberAccess(rhs.Symbol.SelectionRange!));
    }
}