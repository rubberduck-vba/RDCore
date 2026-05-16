using RDCore.Parsing;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using RDCore.Semantics.Diagnostics;

namespace RDCore.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3.6 Binary '\' Operator and 'Mod' Operator (runtime semantics)
/// </summary>
internal sealed record class BinaryModuloOperatorRuntimeSemantics : BinaryIntegerDivisionOperatorRuntimeSemantics
{
    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (effectiveType is VBByteType or VBIntegerType or VBLongType or VBLongLongType)
        {
            if (lhs is not VBNumericTypedValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(expression.Left.Location.Range, lhs.TypeInfo, VBDoubleType.TypeInfo));
            }
            if (rhs is not VBNumericTypedValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(expression.Right.Location.Range, rhs.TypeInfo, VBDoubleType.TypeInfo));
            }

            if (CoerceAndUnwrapNumericValue(lhs) is double lhsValue &&
                CoerceAndUnwrapNumericValue(rhs) is double rhsValue)
            {
                if ((int)rhsValue == 0)
                {
                    throw VBRuntimeErrorException.DivisionByZero(expression.Right.Location.Range);
                }

                // MS-VBAL:
                // if the quotient is an integer, the result is 0.
                // if the quotient is not an integer, the truncated quotient is the integer nearest to the quotient that is closer to zero than the quotient.
                // the result is the absolute value of the difference between the left operand and the product of the truncated quotient and the right operand.

                // NOTE: we have a whole framework, let's use it. the spec is overstepping its own scope trying to be specific here.
                var integerValue = Math.DivRem((int)lhsValue, (int)rhsValue).Remainder;
                return (VBTypedValue)effectiveType.CreateNumericValue(expression.Symbol).WithValue(integerValue);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
