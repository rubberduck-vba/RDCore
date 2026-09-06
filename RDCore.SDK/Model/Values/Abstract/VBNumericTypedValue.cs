using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// Represents any data type that is specified as a <em>numeric type</em>, mapping directly to "Any numeric type" specifications.
/// </summary>
/// <param name="TypeInfo">The <c>VBType</c> of the numeric value.</param>
public abstract record class VBNumericTypedValue(VBType TypeInfo) : VBTypedValue(TypeInfo), INumericValue
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
    public override int GetHashCode() => RuntimeValue.GetHashCode();
}
