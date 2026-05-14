using RDCore.Parsing.Model.Expressions.Operators.StaticSemantics.Abstract;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticSemantics;

/// <summary>
/// MS-VBAL 5.6.9.4 Binary '&' Operator (static semantics)
/// </summary>
internal record class ConcatOperatorStaticSemantics : StaticSemanticRules
{
    public override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes)
    {
        var lhs = operandDeclaredTypes[0];
        var rhs = operandDeclaredTypes[1];

        return lhs switch
        {
            INumericType or VBStringType or VBFixedStringType or VBDateType or VBNullType
                when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType => VBStringType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType or VBDateType
                when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType or VBNullType => VBStringType.TypeInfo,

            VBType and not VBArrayType and not VBUserDefinedType
                when rhs is VBVariantType => VBVariantType.TypeInfo,
            VBVariantType 
                when rhs is VBType and not VBArrayType and not VBUserDefinedType => VBVariantType.TypeInfo,

            _ => default
        };
    }
}