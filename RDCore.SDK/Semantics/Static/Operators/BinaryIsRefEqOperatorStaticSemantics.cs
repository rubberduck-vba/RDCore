using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators
{
    /// <summary>
    /// <strong>MS-VBAL 5.6.9.7</strong> Binary 'Is' Operator (static semantics)
    /// </summary>
    public sealed record class BinaryIsRefEqOperatorStaticSemantics : IStaticSemantics
    {
        public VBType? DetermineDeclaredType(IVBExecutionContext context, params VBType[] operandDeclaredTypes)
        {
            // MS-VBAL: each expression MUST be classified as a value and
            // the declared type of each expression MUST be a specific class, Object, or Variant.

            // here we deal with the declared type - the caller would know about the expressions.
            if (operandDeclaredTypes.All(operand => operand is VBClassType or VBObjectType or VBVariantType))
            {
                return VBBooleanType.TypeInfo;
            }

            return default;
        }
    }
}
