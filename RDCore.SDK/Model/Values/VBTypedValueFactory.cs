using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Values;

/// <summary>
/// A utility factory for creating new <c>VBTypedValue</c> instances.
/// </summary>
public static class VBTypedValueFactory
{
    /// <summary>
    /// Creates a new <c>VBTypedValue</c> of the specified <c>VType</c> 
    /// </summary>
    /// <param name="type">The <c>VBType</c> of the value to create.</param>
    /// <param name="symbol">The <c>Symbol</c> to be associated with the new value.</param>
    public static VBTypedValue? CreateValue(VBType type, Symbol symbol) =>
        type switch
        {
            VBBooleanType => new VBBooleanValue(symbol),
            VBByteType => new VBByteValue(symbol),
            VBIntegerType => new VBIntegerValue(symbol),
            VBLongType => new VBLongValue(symbol),
            VBLongLongType => new VBLongLongValue(symbol),
            VBSingleType => new VBSingleValue(symbol),
            VBDoubleType => new VBDoubleValue(symbol),
            VBCurrencyType => new VBCurrencyValue(symbol),
            VBDecimalType => new VBDecimalValue(symbol),
            VBDateType => new VBDateValue(symbol),
            VBNullType => new VBNullValue(symbol),
            VBEmptyType => new VBEmptyValue(symbol),
            VBObjectType => new VBObjectValue(symbol),
            
            VBVariantType => CreateVariant(type.DefaultValue, symbol),
            _ => throw new VBRuntimeErrorInternalErrorException()
        };

    /// <summary>
    /// Creates a new <c>Variant</c> value wrapping the specified <c>VBTypedValue</c>.
    /// </summary>
    /// <param name="wrapped">The <c>VBTypedValue</c> to be wrapped.into a <c>Variant</c>.</param>
    /// <param name="symbol">The symbol to be associated with the resulting value.</param>
    /// <returns>A <c>VBVariantValue</c> with a <c>SubType</c> matching the data type of the <c>wrapped</c> value.</returns>
    public static VBVariantValue CreateVariant(VBTypedValue wrapped, Symbol symbol) => new(wrapped, symbol);
}
