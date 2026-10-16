namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// States the default value of an optional standard-library parameter, which its C# signature cannot carry.
/// </summary>
/// <remarks>
/// A parameter is declared as a <see cref="Model.Values.Abstract.VBTypedValue"/>, and C# gives a parameter of a class type no default
/// but <c>null</c> (<c>= default</c>), which states no <c>&lt;default-value&gt;</c> clause at all: an omitted argument then takes the
/// declared type's own default value. A member whose specification declares one, such as <c>Optional Limit As Long = -1</c>
/// (<strong>MS-VBAL §6.1.2.11.1.35</strong>), states it here, and the parameter's symbol carries it as the constant expression it is, so
/// that an omitted argument takes it - and the member is told <c>-1</c>, which it cannot tell apart from an explicit <c>-1</c>, rather
/// than <c>0</c>, which it could not tell apart from an explicit <c>0</c>.
/// <para>
/// 👉 An enumeration's default needs none: an enumeration constant is a C# default, and is stated as one.
/// </para>
/// </remarks>
/// <param name="value">
/// The default value, as the C# value of the declared type: an <see langword="int"/> for a <c>Long</c> or an <c>Integer</c>, a
/// <see langword="bool"/> for a <c>Boolean</c>, a <see langword="string"/> for a <c>String</c>.
/// </param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StdLibDefaultAttribute(object value) : Attribute
{
    /// <summary>
    /// The default value, as the C# value of the parameter's declared type.
    /// </summary>
    public object Value { get; } = value;
}
