using RDCore.Parsing;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;
using RDCore.Server;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

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
                        if (operand is not VBNumericTypedValue)
                        {
                            context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(expression.Location.Range, operand.TypeInfo, validOperand.TypeInfo));
                        }
                        else if (operand.Size < validOperand.Size)
                        {
                            context.AddDiagnostic(RDCoreDiagnostic.ImplicitWideningConversion(expression.Location.Range));
                        }
                        else if (operand.Size > validOperand.Size)
                        {
                            context.AddDiagnostic(RDCoreDiagnostic.ImplicitNarrowingConversion(expression.Location.Range));
                        }
                    }
                    else if (expression is VBBinaryOperatorExpression op)
                    {
                        if (validOperands.Count == 0)
                        {
                            if (operand.TypeInfo is VBDateType)
                            {
                                context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(op.Left.Location.Range));
                            }
                            else if (operand is not VBNumericTypedValue)
                            {
                                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(op.Left.Location.Range, operand.TypeInfo, validOperand.TypeInfo));
                            }
                            else if (operand.Size < validOperand.Size)
                            {
                                context.AddDiagnostic(RDCoreDiagnostic.ImplicitWideningConversion(op.Left.Location.Range));
                            }
                            else if (operand.Size > validOperand.Size)
                            {
                                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNarrowingConversion(op.Left.Location.Range));
                            }
                        }
                        else if (validOperands.Count == 1)
                        {
                            if (operand.TypeInfo is VBDateType)
                            {
                                context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(op.Right.Location.Range));
                            }
                            else if (operand is not VBNumericTypedValue)
                            {
                                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(op.Right.Location.Range, operand.TypeInfo, validOperand.TypeInfo));
                            }
                            else if (operand.Size < validOperand.Size)
                            {
                                context.AddDiagnostic(RDCoreDiagnostic.ImplicitWideningConversion(op.Right.Location.Range));
                            }
                            else if (operand.Size > validOperand.Size)
                            {
                                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNarrowingConversion(op.Right.Location.Range));
                            }
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
        VBTypedValue? letCoercedOperand = effectiveType.Equals(operand.TypeInfo) ? operand : null;
        if (letCoercedOperand is not null)
        {
            return letCoercedOperand;
        }
        
        if (effectiveType is VBStringType && operand is IStringCoercion coercibleString)
        {
            var depth = 0;
            letCoercedOperand = coercibleString.AsCoercedString(ref depth);
        }
        else if (effectiveType is VBBooleanType && operand is IBooleanCoercion coercibleBoolean)
        {
            var depth = 0;
            letCoercedOperand = coercibleBoolean.AsCoercedBoolean(ref depth);
        }
        else if (effectiveType is VBDateType && operand is IDateCoercion coercibleDate)
        {
            var depth = 0;
            letCoercedOperand = coercibleDate.AsCoercedDate(ref depth)
                .AsCoercedDouble(ref depth); // date operands are let-coerced to Double before operation evaluation
        }
        else if (effectiveType.Equals(operand.TypeInfo))
        {
            letCoercedOperand = operand;
        }
        else if (effectiveType is INumericType && operand is INumericCoercion coercibleNumeric)
        {
            var depth = 0;
            letCoercedOperand = coercibleNumeric.AsCoercedDouble(ref depth);
        }

        return letCoercedOperand ?? operand;
    }
}
