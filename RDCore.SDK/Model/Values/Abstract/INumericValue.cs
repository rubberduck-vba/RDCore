using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// Represents any numeric value with a non-generic interface.
/// </summary>
public interface INumericValue
{
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
