using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Static.Abstract
{
    /// <summary>
    /// Uses pattern-matching rules to encapsulate unary arithmetic operator static semantics as defined in <strong>MS-VBAL 5.6.9.3</strong>.
    /// </summary>
    /// <remarks>
    /// This is implicitly the specification for the unary '+' operator, which is omitted from MS-VBAL.
    /// </remarks>
    public record class UnaryArithmeticOperatorStaticSemantics : StaticSemantics, IStaticSemantics
    {
        public override VBType? DetermineDeclaredType(IVBExecutionContext context, params VBType[] operandDeclaredTypes)
            => DetermineOperatorStaticType(context, operandDeclaredTypes[0]);

        /// <summary>
        /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics) 
        /// The operator has the declared type returned by this method, based on the declared type of its operands.
        /// </summary>
        /// <param name="operand">The declared type of the operand.</param>
        /// <returns><c>null</c> if no type is statically valid.</returns>
        protected virtual VBType? DetermineOperatorStaticType(IVBExecutionContext context, VBType operand)
        {
            return operand switch
            {
                VBByteType => VBByteType.TypeInfo,
                VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
                VBLongType => VBLongType.TypeInfo,
                VBLongLongType => VBLongLongType.TypeInfo,
                VBSingleType => VBSingleType.TypeInfo,
                VBDoubleType or VBFixedStringType or VBStringType => VBDoubleType.TypeInfo,
                VBCurrencyType => VBCurrencyType.TypeInfo,
                VBDateType => VBDateType.TypeInfo,
                VBVariantType => VBVariantType.TypeInfo,
                _ => default
            };
        }
    }
}
