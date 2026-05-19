using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Globalization;

namespace RDCore.SDK.Model.Values.Abstract;

public static class VBTypedValueFactory
{
    private static readonly Lazy<Dictionary<Type, Func<object, Symbol, VBTypedValue>>> _factories =
        new(() => new()
        {
            [typeof(bool)] = static (value, symbol) => new VBBooleanValue(symbol).WithValue((bool)value),
            [typeof(byte)] = static (value, symbol) => new VBByteValue(symbol).WithValue((byte)value),
            [typeof(short)] = static (value, symbol) => new VBIntegerValue(symbol).WithValue((short)value),
            [typeof(int)] = static (value, symbol) => new VBLongValue(symbol).WithValue((int)value),
            [typeof(long)] = static (value, symbol) => new VBLongLongValue(symbol).WithValue((long)value),
            [typeof(float)] = static (value, symbol) => new VBSingleValue(symbol).WithValue((float)value),
            [typeof(double)] = static (value, symbol) => new VBDoubleValue(symbol).WithValue((double)value),
            [typeof(decimal)] = static (value, symbol) => new VBCurrencyValue(symbol).WithValue((decimal)value),
            [typeof(DateTime)] = static (value, symbol) => new VBDateValue(symbol).WithValue((DateTime)value),

        }, LazyThreadSafetyMode.PublicationOnly);

    public static VBTypedValue WrapManagedValue(object value, Symbol symbol)
    {
        return _factories.Value[value?.GetType()](value, symbol);
    }
}

/// <summary>
/// Represents any run-time typed value that can be represented with a managed (.net) value.
/// </summary>
/// <remarks>
/// Mandates an implementation of <c>IEquatable&lt;T&gt;</c> for the specified <c>VBTypedValue</c>
/// </remarks>
/// <typeparam name="VBTValue">The <c>VBType</c> type of the value.</typeparam>
/// <typeparam name="TValue">The underlying managed type of the value.</typeparam>
public interface IVBTypedValue<VBTValue, TValue> : IEquatable<IVBTypedValue<VBTValue, TValue>>
    where VBTValue : VBTypedValue
{
    /// <summary>
    /// Gets the underlying managed value corresponding to this typed value.
    /// </summary>
    TValue Value { get; }
}

/// <summary>
/// Represents any run-time typed value.
/// </summary>
/// <remarks>
/// This class is at the base of the type hierarchy for all typed values.
/// </remarks>
/// <param name="TypeInfo">The <c>VBType</c> of the value.</param>
/// <param name="Symbol">The <c>Symbol</c> associated with the value, if applicable.</param>
public abstract record class VBTypedValue(VBType TypeInfo, Symbol Symbol) : VBRuntimeEntity(TypeInfo, Symbol)
{
    private static readonly Lazy<CultureInfo> _cultureInfo = new(() => CultureInfo.GetCultureInfo("en-US"), LazyThreadSafetyMode.PublicationOnly);    
    /// <summary>
    /// A static and thread-safe reference to the "en-US" <c>CultureInfo</c> instance that derived values should use to ensure correct string/numeric conversions - this will almost certainly need to be revised to meet MS-VBAL.
    /// </summary>
    protected static CultureInfo CultureInfo => _cultureInfo.Value;

    /// <summary>
    /// <c>true</c> if this typed value is a <c>With</c> block variable.
    /// </summary>
    /// <remarks>
    /// A typed value serving as a <c>With</c> block variable should be a <c>VBObjectValue</c> or a <c>VBVariantValue</c> wrapping a <c>VBObjectValue</c>
    /// that refers to a non-null object pointer (<c>VBNothingValue</c>).
    /// </remarks>
    public bool IsWithBlockVariable { get; init; }

    /// <summary>
    /// Creates a new <c>VBVariantValue</c> wrapping this typed value.
    /// </summary>
    /// <remarks>
    /// The <c>SubType</c> of the created <c>VBVariant</c> is the <c>TypeInfo</c> of this typed value.
    /// </remarks>
    public VBVariantValue AsVariant() => new(this, Symbol);

    /// <summary>
    /// The raw memory address of this typed value.
    /// </summary>
    public long RawAddress { get; init; }

    /// <summary>
    /// The size (in bytes) of this typed value.
    /// </summary>
    public abstract int Size { get; }
}

