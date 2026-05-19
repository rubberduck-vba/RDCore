using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
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
            INumericType or VBStringType or VBDateType or VBNullType or VBEmptyType
                when rhs is INumericType or VBStringType or VBDateType or VBEmptyType => VBStringType.TypeInfo,

            INumericType or VBStringType or VBDateType or VBEmptyType
                when rhs is INumericType or VBStringType or VBDateType or VBNullType or VBEmptyType => VBStringType.TypeInfo,

            // NOTE: on its own switch branch for clarity
            VBArrayType lhsArray when lhsArray.DeclaredValue.ItemType is VBByteType
                && rhs is VBArrayType rhsArray && rhsArray.DeclaredValue.ItemType is VBByteType => VBStringType.TypeInfo,

            VBNullType when rhs is VBNullType => VBNullType.TypeInfo,

            _ => default
        };
    }

    protected override void CheckUdtOrArrayTypeMismatch(VBBinaryOperatorExpression expression, VBTypedValue lhs, VBTypedValue rhs)
    {
        // here we must override the TypeMismatch rule, which runs before the result is evaluated.

        // base implementation throws a type mismatch given any array type here.

        if (lhs is VBErrorValue or VBUserDefinedTypeValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Left.Location.Range);
        }
        if (rhs is VBErrorValue or VBUserDefinedTypeValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Right.Location.Range);
        }

        // ...but we want to allow array values, only if they're resizable byte arrays:

        if (lhs is VBArrayValue && !(lhs is VBResizableArrayValue lhsArray && lhsArray.ItemType is VBByteType))
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Left.Location.Range);
        }
        if (rhs is VBArrayValue && !(rhs is VBResizableArrayValue rhsArray && rhsArray.ItemType is VBByteType))
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Right.Location.Range);
        }
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
