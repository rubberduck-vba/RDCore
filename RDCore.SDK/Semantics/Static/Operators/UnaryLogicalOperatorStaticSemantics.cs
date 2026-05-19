using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3</strong> Unary Operator static semantics
/// </summary>
public record class UnaryLogicalOperatorStaticSemantics : StaticSemantics
{
    public override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes)
    {
        var operand = operandDeclaredTypes[0];
        return operand switch
        {
            VBByteType => VBByteType.TypeInfo,
            VBBooleanType => VBBooleanType.TypeInfo,
            VBIntegerType => VBIntegerType.TypeInfo,
            IFloatingPointNumericType or IFixedPointNumericType or VBLongType or VBFixedStringType or VBStringType or VBDateType => VBLongType.TypeInfo,
            VBLongLongType => VBLongLongType.TypeInfo,
            VBVariantType => VBVariantType.TypeInfo,
            _ => default
        };
    }
}
