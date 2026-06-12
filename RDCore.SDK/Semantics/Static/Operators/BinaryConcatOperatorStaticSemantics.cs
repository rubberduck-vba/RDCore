using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators
{
    /// <summary>
    /// <strong>MS-VBAL 5.6.9.4</strong> Binary '&' Operator (static semantics)
    /// </summary>
    public record class BinaryConcatOperatorStaticSemantics : IStaticSemantics
    {
        public VBType? DetermineDeclaredType(IVBExecutionContext context, params VBType[] operandDeclaredTypes)
        {
            var lhs = operandDeclaredTypes[0];
            var rhs = operandDeclaredTypes[1];

            return lhs switch
            {
                VBNumericType or VBFixedStringType or VBStringType or VBDateType or VBNullType when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType => VBStringType.TypeInfo,
                VBNumericType or VBFixedStringType or VBStringType or VBDateType when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType or VBNullType => VBStringType.TypeInfo,

                VBType and not VBArrayType and not VBUserDefinedType when rhs is VBVariantType => VBVariantType.TypeInfo,
                VBVariantType when rhs is VBType and not VBArrayType and not VBUserDefinedType => VBVariantType.TypeInfo,

                _ => default
            };
        }
    }
}