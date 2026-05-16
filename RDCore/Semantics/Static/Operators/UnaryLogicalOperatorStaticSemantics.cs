using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Semantics.Static.Abstract;

namespace RDCore.Semantics.Static.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3 Unary Operator static semantics
/// </summary>
/// <remarks>
/// This is implicitly the spec for the unary '+' operator, which is omitted from MS-VBAL.
/// </remarks>
internal record class UnaryLogicalOperatorStaticSemantics : StaticSemantics
{
    public override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes)
    {
        var operand = operandDeclaredTypes[0];
        return operand switch
        {
            VBByteType => VBByteType.TypeInfo,
            VBBooleanType => VBBooleanType.TypeInfo,
            VBIntegerType => VBIntegerType.TypeInfo,
            IFloatingPointNumericType or IFixedPointNumericType or VBLongType or VBStringType or VBFixedStringType or VBDateType => VBLongType.TypeInfo,
            VBLongLongType => VBLongLongType.TypeInfo,
            VBVariantType => VBVariantType.TypeInfo,
            _ => default
        };
    }
}
