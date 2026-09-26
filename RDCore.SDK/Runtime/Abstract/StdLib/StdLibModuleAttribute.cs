namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// Marks an interface as the internal representation of one of the standard library's predefined
/// procedural modules (<strong>MS-VBAL §6.1.2</strong>).
/// </summary>
/// <remarks>
/// A predefined procedural module is a standard module, so its members are promoted to the project
/// scope and resolve unqualified: <c>IsNumeric(x)</c> without naming <c>Information</c> first.
/// <para>
/// The marker is what makes the module discoverable - every VBA project has the standard library
/// whether or not anything references it (<strong>RD-VBAL §6.1</strong>), so nothing declares the set
/// of modules a workspace gets, and the set is read off the SDK itself.
/// </para>
/// </remarks>
/// <param name="name">
/// The module's name in VBA source, when it differs from the conventional one.
/// </param>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class StdLibModuleAttribute(string? name = null) : Attribute
{
    /// <summary>
    /// The module's name in VBA source, or <c>null</c> for the conventional one: the declaring
    /// interface's name without its <c>IStd</c> prefix and <c>Module</c> suffix, so
    /// <c>IStdInformationModule</c> is <c>Information</c>.
    /// </summary>
    public string? Name { get; } = name;
}
