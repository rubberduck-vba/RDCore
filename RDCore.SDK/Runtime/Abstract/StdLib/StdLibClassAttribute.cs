namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// Marks an interface as the internal representation of one of the standard library's predefined class
/// modules (<strong>MS-VBAL §6.1.3</strong>).
/// </summary>
/// <remarks>
/// A predefined class module is a class module: it is a nameable type an <c>As</c> clause can bind,
/// and its members resolve only through an instance of it.
/// <para>
/// The marker is what makes the class discoverable - every VBA project has the standard library
/// whether or not anything references it (<strong>RD-VBAL §6.1</strong>), so nothing declares the set
/// of classes a workspace gets, and the set is read off the SDK itself.
/// </para>
/// </remarks>
/// <param name="name">
/// The class's name in VBA source, when it differs from the conventional one.
/// </param>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class StdLibClassAttribute(string? name = null) : Attribute
{
    /// <summary>
    /// The class's name in VBA source, or <c>null</c> for the conventional one: the declaring
    /// interface's name without its <c>IStd</c> prefix and <c>Class</c> suffix, so
    /// <c>IStdCollectionClass</c> is <c>Collection</c>.
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>
    /// Whether VBA source can create an instance of this class with <c>New</c>.
    /// </summary>
    /// <remarks>
    /// <c>true</c> by default - <c>New Collection</c> and <c>New RegExp</c> are both valid. A class
    /// whose instances only ever come from the environment, the way an <c>ErrObject</c> comes from the
    /// <c>Err</c> function, sets this to <c>false</c> so that <c>New</c> on it is reported rather than
    /// resolved.
    /// </remarks>
    public bool IsCreatable { get; init; } = true;
}
