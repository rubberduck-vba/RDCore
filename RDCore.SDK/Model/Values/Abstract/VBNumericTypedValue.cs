using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// Represents any data type that is specified as a <em>numeric type</em>, mapping directly to "Any numeric type" specifications.
/// </summary>
/// <param name="TypeInfo">The <c>VBType</c> of the numeric value.</param>
/// <param name="Symbol">The symbol associated with this value.</param>
public abstract record class VBNumericTypedValue(VBType TypeInfo, Symbol Symbol) : VBTypedValue(TypeInfo, Symbol),
    IComparable<INumericValue>, IEquatable<VBNumericTypedValue>, INumericValue
{
    /// <summary>
    /// The maximum possible number of significant digits retained in a String representation of a value of this type.
    /// </summary>
    public const int SignificantIntegerDigits = VBDoubleType.SignificantIntegerDigits;

    public abstract double ManagedValue { get; init; }
    public int CompareTo(INumericValue? other) => other is null ? 1 : ManagedValue.CompareTo(other.ManagedValue);

    /// <summary>
    /// Gets a copy of this value, with the specified underlying value.
    /// </summary>
    /// <remarks>
    /// 💥<see cref="VBRuntimeErrorId.Overflow"/> may be raised as specified in the appropraite <em>run-time semantics</em> if the specified value is outside the bounds representable by the <see cref="VBType"/>.
    /// </remarks>
    /// <param name="value">The underlying value of the numeric value to be produced.</param>
    public INumericValue WithValue(double value) => 
        this switch
        {
            PrecompilerConstantValue constValue => constValue.WithValue(value),
            VBByteValue byteValue => byteValue.WithValue(value),
            VBIntegerValue integerValue => integerValue.WithValue(value),
            VBLongValue longValue => longValue.WithValue(value),
            VBLongLongValue longLongValue => longLongValue.WithValue(value),
            VBSingleValue singleValue => singleValue.WithValue(value),
            VBDoubleValue doubleValue => doubleValue.WithValue(value),
            VBCurrencyValue currencyValue => currencyValue.WithValue(value),
            VBDecimalValue decimalValue => decimalValue.WithValue(value),
            _ => this with { ManagedValue = value },
        };

    public override int GetHashCode() => ManagedValue.GetHashCode();
    public virtual bool Equals(VBNumericTypedValue? other) => other != null && other.ManagedValue == ManagedValue;
}
