namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// Declares the VBA name of one constant of a standard-library enumeration, where it differs from the
/// conventional one.
/// </summary>
/// <remarks>
/// The conventional name is the field's own with its <c>VB</c> prefix cased down to <c>vb</c>, so
/// <c>VBSunday</c> is <c>vbSunday</c>. This is for the ones that convention does not reach.
/// </remarks>
/// <param name="name">The constant's name in VBA source.</param>
[AttributeUsage(AttributeTargets.Field)]
public sealed class StdLibConstantAttribute(string name) : Attribute
{
    /// <summary>
    /// The constant's name in VBA source.
    /// </summary>
    public string Name { get; } = name;
}
