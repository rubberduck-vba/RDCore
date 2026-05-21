using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Static.Abstract;

/// <summary>
/// Uses pattern-matching rules to encapsulate binary logical operator static semantics as defined in <strong>MS-VBAL 5.6.9.8</strong>.
/// </summary>
public record class BinaryLogicalOperatorStaticSemantics : StaticSemantics
{
    public sealed override VBType? DetermineDeclaredType(IVBExecutionContext context, params VBType[] operandDeclaredTypes)
        => DetermineOperatorStaticType(operandDeclaredTypes[0], operandDeclaredTypes[1]);

    /// <summary>
    /// MS-VBAL 5.6.9.8 Logical Operators (static semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="lhs">The declared type of the LHS operand.</param>
    /// <param name="rhs">The declared type of the RHS operand.</param>
    /// <returns><c>null</c> if no type is statically valid.</returns>
    protected virtual VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBByteType when rhs is VBByteType => VBByteType.TypeInfo,
            VBBooleanType when rhs is VBBooleanType => VBBooleanType.TypeInfo,
            VBByteType or VBIntegerType when rhs is VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
            VBBooleanType or VBIntegerType when rhs is VBByteType or VBIntegerType => VBIntegerType.TypeInfo,

            IFloatingPointNumericType or IFixedPointNumericType or VBLongType or VBFixedStringType or VBStringType or VBDateType 
                when rhs is (VBNumericType and not VBLongLongType) or VBFixedStringType or VBStringType or VBDateType => VBLongType.TypeInfo,
            (VBNumericType and not VBLongLongType) or VBFixedStringType or VBStringType or VBDateType
                when rhs is IFloatingPointNumericType or IFixedPointNumericType or VBLongType or VBFixedStringType or VBStringType or VBDateType => VBLongType.TypeInfo,
            
            VBLongLongType when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType => VBLongLongType.TypeInfo,
            VBNumericType or VBFixedStringType or VBStringType or VBDateType when rhs is VBLongLongType => VBLongLongType.TypeInfo,

            not VBArrayType and not VBUserDefinedType when rhs is VBVariantType => VBVariantType.TypeInfo,

            _ => default
        };
    }
}