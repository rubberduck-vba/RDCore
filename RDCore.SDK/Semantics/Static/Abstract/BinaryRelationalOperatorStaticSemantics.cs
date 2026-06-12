using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Static.Abstract
{
    /// <summary>
    /// Uses pattern-matching rules to encapsulate binary relational operator static semantics as defined in <strong>MS-VBAL 5.6.9.5</strong>.
    /// </summary>
    public record class BinaryRelationalOperatorStaticSemantics : StaticSemantics, IStaticSemantics
    {
        public override VBType? DetermineDeclaredType(IVBExecutionContext context, params VBType[] operandDeclaredTypes)
            => DetermineOperatorStaticType(operandDeclaredTypes[0], operandDeclaredTypes[1]);

        /// <summary>
        /// MS-VBAL 5.6.9.5 Relational Operators (static semantics) 
        /// The operator has the declared type returned by this method, based on the declared type of its operands.
        /// </summary>
        /// <param name="lhs">The declared type of the LHS operand.</param>
        /// <param name="rhs">The declared type of the RHS operand.</param>
        /// <returns><c>null</c> if no type is statically valid.</returns>
        protected virtual VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
        {
            return lhs switch
            {
                not VBArrayType and not VBUserDefinedType and not VBVariantType
                    when rhs is not VBArrayType and not VBUserDefinedType and not VBVariantType => VBBooleanType.TypeInfo,

                not VBArrayType and not VBUserDefinedType when rhs is VBVariantType => VBVariantType.TypeInfo,
                VBVariantType when rhs is not VBArrayType and not VBUserDefinedType => VBVariantType.TypeInfo,

                _ => default
            };
        }
    }
}
