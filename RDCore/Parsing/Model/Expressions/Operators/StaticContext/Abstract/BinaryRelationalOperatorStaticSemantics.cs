using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticContext.Abstract;

/// <summary>
/// MS-VBAL 5.6.9.7 Binary 'Is' Operator (static semantics)
/// </summary>
internal record class IsRefEqOperatorStaticSemantics : StaticSemantics
{
    public override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes)
    {
        // MS-VBAL: each **expression** MUST be classified as a value and
        // the **declared type** of each expression MUST be a specific class, Object, or Variant.

        // here we deal with the declared type - the caller would know about the expressions.
        if (operandDeclaredTypes.All(operand => operand is VBClassType or VBObjectType or VBVariantType))
        {
            return VBBooleanType.TypeInfo;
        }

        return default;
    }
}

internal record class BinaryRelationalOperatorStaticSemantics : StaticSemantics
{
    public sealed override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes)
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

            not VBArrayType and not VBUserDefinedType 
                when rhs is VBVariantType => VBVariantType.TypeInfo,
            VBVariantType 
                when rhs is not VBArrayType and not VBUserDefinedType => VBVariantType.TypeInfo,

            _ => default
        };
    }
}
