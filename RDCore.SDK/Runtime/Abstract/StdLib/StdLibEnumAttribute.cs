namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// Marks an enumeration as the internal representation of one of the standard library's predefined
/// enums (<strong>MS-VBAL §6.1.1</strong>).
/// </summary>
/// <remarks>
/// A predefined enum is a global <c>Enum</c>: the type name binds in an <c>As</c> clause, and each of
/// its constants resolves unqualified, which is what makes <c>vbSunday</c> a name on its own.
/// <para>
/// The marker is what makes the enum discoverable - every VBA project has the standard library whether
/// or not anything references it (<strong>RD-VBAL §6.1</strong>), so nothing declares the set of enums
/// a workspace gets, and the set is read off the SDK itself.
/// </para>
/// </remarks>
/// <param name="name">
/// The enum's name in VBA source, when it differs from the conventional one.
/// </param>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class StdLibEnumAttribute(string? name = null) : Attribute
{
    /// <summary>
    /// The enum's name in VBA source, or <c>null</c> for the conventional one: the declaring
    /// enumeration's name with its <c>VB</c> prefix cased down to <c>Vb</c>, so <c>VBDayOfWeek</c> is
    /// <c>VbDayOfWeek</c>.
    /// </summary>
    /// <remarks>
    /// <c>FormShowConstants</c> (<strong>§6.1.1.1</strong>) is the one predefined enum with no prefix
    /// at all, and states its name here.
    /// </remarks>
    public string? Name { get; } = name;
}
