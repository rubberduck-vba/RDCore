using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;

namespace RDCore.SDK.Semantics.Runtime.Operators;

public abstract record class UnaryOperatorRuntimeSemantics : RuntimeSemantics
{
    public sealed override VBType? DetermineEffectiveType(IVBExecutionContext context, params VBType[] operandDeclaredTypes)
        => DetermineOperatorEffectiveType(operandDeclaredTypes[0]);

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (runtime semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="operand">The declared type of the operand.</param>
    /// <returns><c>null</c> if no type is statically valid.</returns>
    protected virtual VBType? DetermineOperatorEffectiveType(VBType operand)
    {
        return operand switch
        {
            VBByteType => VBByteType.TypeInfo,
            VBBooleanType or VBIntegerType or VBEmptyType => VBIntegerType.TypeInfo,
            VBLongType => VBLongType.TypeInfo,
            VBLongLongType => VBLongLongType.TypeInfo,
            VBSingleType => VBSingleType.TypeInfo,
            VBDoubleType or VBStringType => VBDoubleType.TypeInfo,
            VBCurrencyType => VBCurrencyType.TypeInfo,
            VBDateType => VBDateType.TypeInfo,
            VBDecimalType => VBDecimalType.TypeInfo,
            VBNullType => VBNullType.TypeInfo,
            _ => default
        };
    }
}

