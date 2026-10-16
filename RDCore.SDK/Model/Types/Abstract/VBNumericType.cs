using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Types.Abstract;

/// <summary>
/// Represents any <strong>numeric</strong> data type mentioned in the <strong>MS-VBAL</strong> language specifications.
/// </summary>
/// <typeparam name="T">The managed type (internal representation) associated with this data type.</typeparam>
/// <param name="Name">The name (token) of the data type.</param>
public abstract record class VBNumericType<T>(string Name) : VBNumericType(Name, typeof(T)), INumericType { }

/// <summary>
/// Represents any <strong>numeric</strong> data type mentioned in the <strong>MS-VBAL</strong> language specifications.
/// </summary>
/// <typeparam name="T">The managed type (internal representation) associated with this data type.</typeparam>
/// <param name="Name">The name (token) of the data type.</param>
public abstract record class VBNumericType(string Name, Type ManagedType) : VBIntrinsicType(Name, ManagedType), INumericType
{
    /// <summary>
    /// Creates a value of this numeric type holding <paramref name="value"/>, converted to this
    /// type's managed representation (banker's rounding for integral targets, as per <c>Convert</c>).
    /// </summary>
    public abstract VBNumericTypedValue CreateValue(double value);

    /// <summary>
    /// Gets the minimum representable managed (.net) value for this data type.
    /// </summary>
    public abstract double ManagedMinValue { get; }
    /// <summary>
    /// Gets the maximum representable managed (.net) value for this data type.
    /// </summary>
    public abstract double ManagedMaxValue { get; }

    /// <summary>
    /// Implements <strong>MS-VBAL 5.5.1.2.1.1</strong> Banker's Rounding.
    /// A midpoint rounding scheme also known as "round-to-even" rounds to the nearest rounded value such
    /// that the least-significant digit is even.
    /// </summary>
    /// <param name="value">The floating-point numeric value to be rounded.</param>
    /// <returns>The nearest whole number, as a <see cref="double"/>: a whole number past the range of an <see cref="int"/> is still the one it rounds to.</returns>
    public static double BankersRounding(double value) => Math.Round(value, MidpointRounding.ToEven);
    /// <summary>
    /// Implements <strong>MS-VBAL 5.5.1.2.1.1</strong> Banker's Rounding.
    /// A midpoint rounding scheme also known as "round-to-even" rounds to the nearest rounded value such
    /// that the least-significant digit is even.
    /// </summary>
    /// <param name="value">The numeric value to be rounded.</param>
    /// <returns>The nearest whole number, as a <see cref="double"/>.</returns>
    public static double BankersRounding(VBNumericTypedValue value) => BankersRounding(value.AsDouble);

    /// <summary>
    /// A helper function to test if a given source numeric value is within the range of a destination data type.
    /// </summary>
    /// <param name="source">The managed (.net) numeric source value to validate.</param>
    /// <param name="destination">The data type to check conversion eligibility against.</param>
    /// <returns><c>true</c> if the value can legally be used with the destination type, <c>false otherwise</c>.</returns>
    /// <remarks>If this function returns <c>false</c>, the caller should throw a <c>VBRuntimeErrorOverflowException.</c></remarks>
    public static bool IsWithinRange(double source, VBNumericType destination) => destination.Holds(source);

    /// <summary>
    /// Whether a <see cref="double"/> is within the range of this type (<strong>MS-VBAL 5.5.1.2.1</strong>): one a value of this type can be made of
    /// (<see cref="CreateValue(double)"/>) without leaving the range.
    /// </summary>
    /// <param name="value">The value. <c>NaN</c> is within no range.</param>
    /// <remarks>
    /// 👉 The base is within <see cref="ManagedMinValue"/> and <see cref="ManagedMaxValue"/>, both included, which is right for a type whose bounds are
    /// <see cref="double"/> values. A type whose bound is not one has, as its <see cref="ManagedMaxValue"/> or <see cref="ManagedMinValue"/>, the
    /// <see cref="double"/> nearest it, which can be past it: that type overrides this, and leaves out such a bound.
    /// </remarks>
    public virtual bool Holds(double value) => value >= ManagedMinValue && value <= ManagedMaxValue;
}
