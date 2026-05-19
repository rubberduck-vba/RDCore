using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.4</strong> Binary '*' Operator (static semantics)
/// </summary>
public sealed record class BinaryMultiplicationOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    protected override VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBCurrencyType when rhs is VBSingleType or VBDoubleType or VBFixedStringType or VBStringType => VBDoubleType.TypeInfo,
            VBSingleType or VBDoubleType or VBFixedStringType or VBStringType when rhs is VBCurrencyType => VBDoubleType.TypeInfo,
            VBDateType when rhs is INumericType or VBFixedStringType or VBStringType or VBDateType => VBDoubleType.TypeInfo,
            INumericType or VBFixedStringType or VBStringType or VBDateType when rhs is VBDateType => VBDoubleType.TypeInfo,
            _ => base.DetermineOperatorStaticType(lhs, rhs)
        };
    }
}
