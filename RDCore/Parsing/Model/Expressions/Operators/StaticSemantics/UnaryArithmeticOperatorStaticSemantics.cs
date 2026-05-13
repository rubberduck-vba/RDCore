using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticSemantics.StaticSemantics;

/// <summary>
/// TODO derive static semantics for each operator; they only need to specify their respective exceptions, reading plainly like MS-VBAL.
/// </summary>
internal abstract record class UnaryArithmeticOperatorStaticSemantics : ArithmeticOperatorStaticSemantics
{
    public sealed override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes)
        => DetermineOperatorStaticType(operandDeclaredTypes[0]);

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="operand">The declared type of the operand.</param>
    /// <returns><c>null</c> if no type is statically valid.</returns>
    protected virtual VBType? DetermineOperatorStaticType(VBType operand)
    {
        return operand switch
        {
            VBByteType => VBByteType.TypeInfo,
            VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
            VBLongType => VBLongType.TypeInfo,
            VBLongLongType => VBLongLongType.TypeInfo,
            VBSingleType => VBSingleType.TypeInfo,
            VBDoubleType or VBStringType or VBFixedStringType => VBDoubleType.TypeInfo, // note: fixed string inherits string
            VBCurrencyType => VBCurrencyType.TypeInfo,
            VBDateType => VBDateType.TypeInfo,
            VBVariantType => VBVariantType.TypeInfo,
            _ => default
        };
    }
}
