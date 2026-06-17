using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;

namespace RDCore.SDK.Model.Values;

/// <summary>
/// A utility factory for creating new <c>VBTypedValue</c> instances.
/// </summary>
public static class VBTypedValueFactory
{
    /// <summary>
    /// Creates a new <see cref="VBTypeDescValue"/>, which is a meta-<see cref="VBTypedValue"/> representing a <see cref="VBType"/>.
    /// </summary>
    /// <param name="type">The <see cref="VBType"/> to describe.</param>
    /// <param name="symbol">The <see cref="Symbol"/> to be associated with the value.</param>
    public static VBTypeDescValue DescribeType(VBType type, Symbol symbol) => new(type, symbol);

    /// <summary>
    /// Creates a new <c>VBNumericValue</c> of the specified type, with the specified value, for the specified symbol.
    /// </summary>
    /// <typeparam name="TNumericType">The target numeric <c>VBType</c> data type.</typeparam>
    /// <param name="type">The target numeric <c>VBType</c>.</param>
    /// <param name="symbol">The symbol to be associated with the new value.</param>
    /// <param name="numericValue">The underlying (managed) numeric value being wrapped.</param>
    public static VBNumericTypedValue CreateValue<TNumericType>(TNumericType type, Symbol symbol, double numericValue) where TNumericType : VBNumericType, INumericType
        => (VBNumericTypedValue)((INumericValue)CreateValue(type, symbol)!).WithValue(numericValue);

    /// <summary>
    /// Creates a new <c>VBDateValue</c> with the specified value for the specified symbol.
    /// </summary>
    /// <param name="symbol">The symbol to be associated with the new value.</param>
    /// <param name="dateTimeValue">The underying (managed) <c>DateTime</c> value being wrapped.</param>
    public static VBDateValue CreateValue(Symbol symbol, DateTime dateTimeValue)
        => new(symbol) { Value = dateTimeValue };

    /// <summary>
    /// Creates a new <c>VBBooleanValue</c> with the specified value for the specified symbol.
    /// </summary>
    /// <param name="symbol">The symbol to be associated with the new value.</param>
    /// <param name="boolValue">The underying (managed) <c>bool</c> value being wrapped.</param>
    public static VBBooleanValue CreateValue(Symbol symbol, bool boolValue)
        => new(symbol) { Value = boolValue };
    /// <summary>
    /// Creates a new <c>VBBooleanValue</c> with the specified value for the specified symbol.
    /// </summary>
    /// <param name="symbol">The symbol to be associated with the new value.</param>
    /// <param name="boolValue">The underying (managed) <c>bool</c> value being wrapped.</param>
    public static VBBooleanValue CreateBooleanValue(Symbol symbol, double numericValue)
        => new(symbol) { Value = numericValue != 0 };

    public static VBBooleanValue CreateBooleanValue(Symbol symbol, VBBooleanValue value)
        => new(symbol) { Value = value.Value == true };
    public static VBBooleanValue CreateBooleanValue(Symbol symbol, bool value)
        => new(symbol) { Value = value == true };



    /// <summary>
    /// Creates a new <c>VBNumericValue</c> of the specified type, with the specified value, for the specified symbol.
    /// </summary>
    /// <param name="type">The target numeric <c>VBType</c>.</param>
    /// <param name="symbol">The symbol to be associated with the new value.</param>
    /// <param name="numericValue">The underlying (managed) numeric value being wrapped.</param>
    public static VBTypedValue CreateValue(VBType type, Symbol symbol, double numericValue)
        => (VBNumericTypedValue)((INumericValue)CreateValue(type, symbol)!).WithValue(numericValue);

    /// <summary>
    /// Creates a new <see cref="VBNullValue"/> for the specified <see cref="Symbol"/>.
    /// </summary>
    /// <param name="symbol">The symbol to be associated with the new value.</param>
    public static VBNullValue CreateNullValue(Symbol symbol) => new(symbol);

    /// <summary>
    /// Creates a new <c>VBNumericValue</c> of the specified described type, with the specified value, for the specified symbol.
    /// </summary>
    /// <param name="typeDesc">A <c>VBTypeDescValue</c> describing the target numeric data type.</param>
    /// <param name="symbol">The symbol to be associated with the new value.</param>
    /// <param name="numericValue">The underlying (managed) numeric value being wrapped.</param>
    /// <remarks>
    /// 👉 Overloads taking a <see cref="VBTypeDescValue"/> <em>type descriptor value</em> parameter are 
    /// intended for let-coercion semantics and may eventually need to be moved.
    /// </remarks>
    public static VBTypedValue CreateValue(VBTypeDescValue typeDesc, Symbol symbol, double numericValue)
        => (VBNumericTypedValue)((INumericValue)CreateValue(typeDesc.Target, symbol)!).WithValue(numericValue);
    /// <summary>
    /// Creates a new <c>VBNumericValue</c> of the specified described type, with the specified value, for the specified symbol.
    /// </summary>
    /// <param name="typeDesc">A <c>VBTypeDescValue</c> describing the target numeric data type.</param>
    /// <param name="symbol">The symbol to be associated with the new value.</param>
    /// <param name="source">A <see cref="VBNumericTypedValue"/> source value.</param>
    /// <remarks>
    /// 👉 Overloads taking a <see cref="VBTypeDescValue"/> <em>type descriptor value</em> parameter are 
    /// intended for let-coercion semantics and may eventually need to be moved.
    /// </remarks>
    public static VBTypedValue CreateValue(VBTypeDescValue typeDesc, Symbol symbol, VBNumericTypedValue source)
        => CreateValue(typeDesc.Target, symbol, source.ManagedValue);
    /// <summary>
    /// Creates a new <c>VBNumericValue</c> of the specified described type, with the specified value, for the specified symbol.
    /// </summary>
    /// <param name="typeDesc">A <c>VBTypeDescValue</c> describing the target numeric data type.</param>
    /// <param name="symbol">The symbol to be associated with the new value.</param>
    /// <param name="source">A <see cref="VBDateValue"/> source value.</param>
    /// <remarks>
    /// 👉 Overloads taking a <see cref="VBTypeDescValue"/> <em>type descriptor value</em> parameter are 
    /// intended for let-coercion semantics and may eventually need to be moved.
    /// </remarks>
    public static VBTypedValue CreateValue(VBTypeDescValue typeDesc, Symbol symbol, VBDateValue source)
        => CreateValue(typeDesc.Target, symbol, source.SerialValue);

    public static VBTypedValue CreateValue(Symbol symbol, VBDateValue value)
        => CreateValue(VBDateType.TypeInfo, symbol, value.SerialValue);

    /// <summary>
    /// Creates a new <c>VBStringValue</c> with the specified value, for the specified symbol.
    /// </summary>
    /// <param name="symbol">The symbol to be associated with the new value.</param>
    /// <param name="stringValue">The underlying (managed) string value being wrapped.</param>
    public static VBStringValue CreateStringValue(Symbol symbol, string stringValue)
        => ((VBStringValue)CreateValue(VBStringType.TypeInfo, symbol)!).WithValue(stringValue);

    /// <summary>
    /// Creates a new <c>VBStringValue</c> with the specified value, for the specified symbol.
    /// </summary>
    /// <param name="symbol">The symbol to be associated with the new value.</param>
    /// <param name="stringValue">The underlying (managed) string value being wrapped.</param>
    public static VBTypedValue CreateStringValue(Symbol symbol, VBStringValue stringValue) => CreateStringValue(symbol, stringValue.Value);

    /// <summary>
    /// Creates a new <c>VBTypedValue</c> of the specified <c>VType</c> 
    /// </summary>
    /// <param name="type">The <c>VBType</c> of the value to create.</param>
    /// <param name="symbol">The <c>Symbol</c> to be associated with the new value.</param>
    public static VBTypedValue? CreateValue(VBType type, Symbol symbol) =>
        type switch
        {
            VBStringType => new VBStringValue(symbol),
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
            _ => null
        };

    /// <summary>
    /// Creates a new <c>Variant</c> value wrapping the specified <c>VBTypedValue</c>.
    /// </summary>
    /// <param name="wrapped">The <c>VBTypedValue</c> to be wrapped.into a <c>Variant</c>.</param>
    /// <param name="symbol">The symbol to be associated with the resulting value.</param>
    /// <returns>A <c>VBVariantValue</c> with a <c>SubType</c> matching the data type of the <c>wrapped</c> value.</returns>
    public static VBVariantValue CreateVariant(VBTypedValue wrapped, Symbol symbol) => new(wrapped, symbol);
}
