using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.7 Binary 'Is' Operator
/// </summary>
public record class IsRefEqRelationalOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs) => VBBooleanType.TypeInfo;

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        // just to read like MS-VBAL: VBNothingValue is a VBObjectValue (similar w/ string & fixedString)
        if (lhs is not VBObjectValue and not VBNothingValue)
        {
            throw VBRuntimeErrorException.ObjectRequired(expression.Left.Location.Range);
        }
        if (rhs is not VBObjectValue and not VBNothingValue)
        {
            throw VBRuntimeErrorException.ObjectRequired(expression.Right.Location.Range);
        }

        if (lhs.Symbol != null && lhs is VBObjectValue or VBVariantValue && 
            rhs.Symbol != null && rhs is VBObjectValue or VBVariantValue)
        {
            // TODO revisit this once we actually allocate things
            var result = lhs.RawAddress == rhs.RawAddress;
            return new VBBooleanValue(expression.Symbol).WithValue(result);
        }
        return default;
    }
}
