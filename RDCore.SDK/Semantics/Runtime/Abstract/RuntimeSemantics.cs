using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Abstract;

public enum SemanticOperation
{
    SyntaxError,
    CompileError,
    RuntimeError,
    TypeConversion,

}

[Flags]
public enum SemanticFlags
{
    ConversionImplicit = 1,
    ConversionWidening = 2,
    ConversionNarrowing = 4,
    ConversionNumeric = 8,
    ConversionDateSerial = 16,
}

public abstract record class RuntimeSemantics
{
    public abstract VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes);

    public VBTypedValue? Evaluate(IVBExecutionContext context, ValuedExpression expression, params VBTypedValue[] operands)
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
        }
        else
        {
            CheckUdtOrArrayTypeMismatch(expression, operands[0]);
        }

        foreach (var operand in operands) 
        {
            if (operand is not VBNullValue && LetCoerceNonNullOperand(context, effectiveType, operand) is VBTypedValue validOperand)
            {
                if (!validOperand.TypeInfo.Equals(operand.TypeInfo))
                {
                    if (operands.Length == 1)
                    {
                        //if (operand is not VBNumericTypedValue)
                        //{
                        //    context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(expression.Location.Range, operand.TypeInfo, validOperand.TypeInfo));
                        //}
                        //else if (operand.Size < validOperand.Size)
                        //{
                        //    context.AddDiagnostic(RDCoreDiagnostic.ImplicitWideningConversion(expression.Location.Range));
                        //}
                        //else if (operand.Size > validOperand.Size)
                        //{
                        //    context.AddDiagnostic(RDCoreDiagnostic.ImplicitNarrowingConversion(expression.Location.Range));
                        //}
                    }
                    else if (expression is VBBinaryOperatorExpression op)
                    {
                        if (validOperands.Count == 0)
                        {
                        //    if (operand.TypeInfo is VBDateType)
                        //    {
                        //        context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(op.Left.Location.Range));
                        //    }
                        //    else if (operand is not VBNumericTypedValue)
                        //    {
                        //        context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(op.Left.Location.Range, operand.TypeInfo, validOperand.TypeInfo));
                        //    }
                        //    else if (operand.Size < validOperand.Size)
                        //    {
                        //        context.AddDiagnostic(RDCoreDiagnostic.ImplicitWideningConversion(op.Left.Location.Range));
                        //    }
                        //    else if (operand.Size > validOperand.Size)
                        //    {
                        //        context.AddDiagnostic(RDCoreDiagnostic.ImplicitNarrowingConversion(op.Left.Location.Range));
                        //    }
                        }
                        else if (validOperands.Count == 1)
                        {
                            //if (operand.TypeInfo is VBDateType)
                            //{
                            //    context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(op.Right.Location.Range));
                            //}
                            //else if (operand is not VBNumericTypedValue)
                            //{
                            //    context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(op.Right.Location.Range, operand.TypeInfo, validOperand.TypeInfo));
                            //}
                            //else if (operand.Size < validOperand.Size)
                            //{
                            //    context.AddDiagnostic(RDCoreDiagnostic.ImplicitWideningConversion(op.Right.Location.Range));
                            //}
                            //else if (operand.Size > validOperand.Size)
                            //{
                            //    context.AddDiagnostic(RDCoreDiagnostic.ImplicitNarrowingConversion(op.Right.Location.Range));
                            //}
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

        return EvaluateExpressionResult(context, expression, effectiveType, [.. validOperands]);
    }

    protected abstract VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands);

    protected virtual void CheckUdtOrArrayTypeMismatch(ValuedExpression expression, VBTypedValue operand)
    {
        // NOTE: MS-VBAL does not mention *why* these types are special-cased, but coincidentally these types *must* be passed by reference.
        // NOTE: virtual because Byte() coercion to and from String needs to override this.
        if (operand is VBArrayValue or VBErrorValue or VBUserDefinedTypeValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }
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

    protected virtual VBTypedValue? LetCoerceNonNullOperand(IVBExecutionContext context, VBType effectiveType, VBTypedValue operand)
    {
        VBTypedValue? letCoercedOperand = effectiveType.Equals(operand.TypeInfo) ? operand : null;
        if (letCoercedOperand is not null)
        {
            return letCoercedOperand;
        }

        if (LetCoercionRuntimeSemantics.GetSemantics(effectiveType) is LetCoercionRuntimeSemantics semantics)
        {
            return semantics.EvaluateExpressionResult(context, null!, effectiveType, [operand]);
        }

        return operand;
    }
}
