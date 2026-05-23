using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3.6 Binary '\' Operator and 'Mod' Operator (runtime semantics)
/// </summary>
public sealed record class BinaryModuloOperatorRuntimeSemantics : BinaryIntegerDivisionOperatorRuntimeSemantics
{
    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (effectiveType is VBByteType or VBIntegerType or VBLongType or VBLongLongType)
        {
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
                return VBTypedValueFactory.CreateValue((VBNumericType)effectiveType, expression.Symbol, integerValue);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
