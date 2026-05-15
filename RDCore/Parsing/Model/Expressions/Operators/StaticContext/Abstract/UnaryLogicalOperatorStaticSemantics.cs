using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticContext.Abstract;

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
