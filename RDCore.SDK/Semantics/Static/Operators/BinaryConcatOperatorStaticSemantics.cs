using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.4</strong> Binary '&' Operator (static semantics)
/// </summary>
public record class BinaryConcatOperatorStaticSemantics : StaticSemantics
{
    public override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes)
    {
        var lhs = operandDeclaredTypes[0];
        var rhs = operandDeclaredTypes[1];

        return lhs switch
        {
            INumericType or VBFixedStringType or VBStringType or VBDateType or VBNullType
                when rhs is INumericType or VBFixedStringType or VBStringType or VBDateType => VBStringType.TypeInfo,
            INumericType or VBFixedStringType or VBStringType or VBDateType
                when rhs is INumericType or VBFixedStringType or VBStringType or VBDateType or VBNullType => VBStringType.TypeInfo,

            VBType and not VBArrayType and not VBUserDefinedType
                when rhs is VBVariantType => VBVariantType.TypeInfo,
            VBVariantType 
                when rhs is VBType and not VBArrayType and not VBUserDefinedType => VBVariantType.TypeInfo,

            _ => default
        };
    }
}