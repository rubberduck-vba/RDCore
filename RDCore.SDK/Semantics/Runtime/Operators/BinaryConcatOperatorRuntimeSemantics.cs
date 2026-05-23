using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Operators;

public record class BinaryConcatOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        return lhs switch 
        {
            VBNumericType or VBStringType or VBDateType or VBNullType or VBEmptyType
                when rhs is VBNumericType or VBStringType or VBDateType or VBEmptyType => VBStringType.TypeInfo,

            VBNumericType or VBStringType or VBDateType or VBEmptyType
                when rhs is VBNumericType or VBStringType or VBDateType or VBNullType or VBEmptyType => VBStringType.TypeInfo,

            VBResizableByteArrayType when rhs is VBResizableByteArrayType => VBStringType.TypeInfo,

            VBNullType when rhs is VBNullType => VBNullType.TypeInfo,
            _ => default
        };
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (effectiveType is VBStringType)
        {
            // NOTE: this behavior does not appear to be specified anywhere in MS-VBAL.
            // without this implicit let-coercion from null, we throw error 94 InvalidUseOfNull here.
            // treating VBNullValue as VBNullString for the sake of this operator's implementation
            // seems to be exactly what MS-VBA does, based on observation and experimentation.
            // No diagnostics should be issued in this case.

            if (lhs is VBNullValue)
            {
                lhs = VBStringValue.VBNullString;
            }
            if (rhs is VBNullValue)
            {
                rhs = VBStringValue.VBNullString;
            }

            // ------

            // let-evaluation let-coercion should already have done everything.
            if (CoerceAndUnwrapStringValue(lhs) is string lhsValue &&
                CoerceAndUnwrapStringValue(rhs) is string rhsValue)
            {
                var result = $"{lhsValue}{rhsValue}";
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
