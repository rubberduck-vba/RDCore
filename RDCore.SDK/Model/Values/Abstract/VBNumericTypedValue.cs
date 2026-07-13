using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Interop;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// Represents any data type that is specified as a <em>numeric type</em>, mapping directly to "Any numeric type" specifications.
/// </summary>
/// <param name="TypeInfo">The <c>VBType</c> of the numeric value.</param>
/// <param name="Symbol">The symbol associated with this value.</param>
public abstract record class VBNumericTypedValue(VBType TypeInfo, Symbol Symbol) : VBTypedValue(TypeInfo, Symbol),
    INumericValue
{
    /// <summary>
    /// The maximum possible number of significant digits retained in a String representation of a value of this type.
    /// </summary>
    public const int SignificantIntegerDigits = VBDoubleType.SignificantIntegerDigits;

    /// <summary>
    /// Gets a copy of this value, with the specified underlying value.
    /// </summary>
    /// <remarks>
    /// 💥<see cref="VBRuntimeErrorId.Overflow"/> may be raised as specified in the appropraite <em>run-time semantics</em> if the specified value is outside the bounds representable by the <see cref="VBType"/>.
    /// </remarks>
    /// <param name="value">The underlying value of the numeric value to be produced.</param>
    public INumericValue WithValue<T>(T value) where T : struct
    {
        return this switch
        {
            PrecompilerConstantValue constValue => constValue with { ManagedValue = new(new ManagedInteropValue(Convert.ToInt16(value))) },
            VBByteValue byteValue => byteValue with { ManagedValue = new(new ManagedInteropValue(Convert.ToByte(value))) },
            VBIntegerValue integerValue => integerValue with { ManagedValue = new(new ManagedInteropValue(Convert.ToInt16(value))) },
            VBLongValue longValue => longValue with { ManagedValue = new(new ManagedInteropValue(Convert.ToInt32(value))) },
            VBLongLongValue longLongValue => longLongValue with { ManagedValue = new(new ManagedInteropValue(Convert.ToInt64(value))) },
            VBSingleValue singleValue => singleValue with { ManagedValue = new(new ManagedInteropValue(Convert.ToSingle(value))) },
            VBDoubleValue doubleValue => doubleValue with { ManagedValue = new(new ManagedInteropValue(Convert.ToDouble(value))) },
            VBCurrencyValue currencyValue => currencyValue with { ManagedValue = new(new ManagedInteropValue(new ManagedCurrencyInteropValue(Convert.ToDecimal(value)))) },
            VBDecimalValue decimalValue => decimalValue with { ManagedValue = new(new ManagedInteropValue(new ManagedDecimalInteropValue(Convert.ToDecimal(value)))) },

            _ => throw new NotSupportedException(),
        };
    }

    public override int GetHashCode() => ManagedValue.GetHashCode();
}
