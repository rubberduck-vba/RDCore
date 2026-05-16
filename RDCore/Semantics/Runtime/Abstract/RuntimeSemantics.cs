using RDCore.Parsing;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using RDCore.Semantics.Diagnostics;

namespace RDCore.Semantics.Runtime.Abstract;

internal abstract record class RuntimeSemantics
{
    public abstract VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes);

    public VBTypedValue? Evaluate(VBExecutionContext context, VBOperatorExpression expression, params VBTypedValue[] operands)
    {
        var operandTypes = operands.Select(op => op.TypeInfo).ToArray();
        var effectiveType = DetermineEffectiveType(operandTypes);
        if (effectiveType is null)
        {
            // the operation is invalid
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }

        var validOperands = new List<VBTypedValue>();
        if (expression is VBBinaryOperatorExpression binaryOp)
        {
            CheckUdtOrArrayTypeMismatch(binaryOp, operands[0], operands[1]);
            DiagnosePreLetCoerceOperands(context, binaryOp, operands[0], operands[1]);
        }
        else
        {
            CheckUdtOrArrayTypeMismatch(expression, operands[0]);
            DiagnosePreLetCoerceOperands(context, expression, operands[0]);
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

    protected virtual void CheckUdtOrArrayTypeMismatch(VBOperatorExpression expression, VBTypedValue operand)
    {
        // NOTE: MS-VBAL does not mention *why* these types are special-cased, but coincidentally these types *must* be passed by reference.
        // NOTE: virtual because Byte() coercion to and from String needs to override this.
        if (operand is VBArrayValue or VBErrorValue or VBUserDefinedTypeValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }
    }

    protected virtual void DiagnosePreLetCoerceOperands(VBExecutionContext context, VBOperatorExpression expression, VBTypedValue operand)
    {
    }
    protected virtual void DiagnosePreLetCoerceOperands(VBExecutionContext context, VBBinaryOperatorExpression expression, VBTypedValue lhs, VBTypedValue rhs)
    {
    }

    protected virtual void CheckUdtOrArrayTypeMismatch(VBBinaryOperatorExpression expression, VBTypedValue lhs, VBTypedValue rhs)
    {
        // NOTE: MS-VBAL does not mention *why* these types are special-cased, but coincidentally these types *must* be passed by reference.
        // NOTE: virtual because Byte() coercion to and from String needs to override this.

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
