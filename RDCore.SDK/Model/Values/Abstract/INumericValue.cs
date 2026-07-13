using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// Represents any numeric value with a non-generic interface.
/// </summary>
public interface INumericValue
{
    /// <summary>
    /// Gets a managed (.net) internal representation the numeric value. This is always a <c>double</c>.
    /// </summary>
    //double ManagedValue { get; }

    /// <summary>
    /// Gets a copy of this value, with the specified underlying value.
    /// </summary>
    /// <remarks>
    /// 💥<see cref="VBRuntimeErrorId.Overflow"/> may be raised as specified in the appropraite <em>run-time semantics</em> if the specified value is outside the bounds representable by the <see cref="VBType"/>.
    /// </remarks>
    /// <param name="value">The <em>managed value</em> of the numeric value to be produced.</param>
    INumericValue WithValue<T>(T value) where T : struct;
}

/// <summary>
/// Represents any numeric value with a generic interface mapping it to a specific <see cref="VBType"/>.
/// </summary>
/// <typeparam name="VBTValue"></typeparam>
public interface INumericValue<VBTValue> : INumericValue
    where VBTValue : VBTypedValue
{
    /// <summary>
    /// The numeric <see cref="VBType"/> of this value.
    /// </summary>
    VBType TypeInfo { get; }
}
