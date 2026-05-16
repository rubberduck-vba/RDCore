using RDCore.Parsing;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;

namespace RDCore.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.7 Binary 'Is' Operator
/// </summary>
internal record class IsRefEqRelationalOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs) => VBBooleanType.TypeInfo;

    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
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
