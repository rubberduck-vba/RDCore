namespace RDCore.Runtime.LanguageCore.StdLib.Abstract;

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
    /// <c>true</c> by default - <c>New Collection</c> and <c>New RegExp</c> are both valid. It is what the
    /// library's type library says of the class (<c>TYPEFLAG_FCANCREATE</c>): a class it does not say
    /// it of, the way <c>Match</c> is the object a <c>RegExp</c> finds, sets this to <c>false</c>, and
    /// <c>New</c> of it is a compile error (<strong>MS-VBAL §5.2.3.1.4</strong>).
    /// </remarks>
    public bool IsCreatable { get; init; } = true;

    /// <summary>
    /// Whether no server is registered for this class, though <see cref="IsCreatable"/> says <c>New</c>
    /// of it is valid: creating an object of it fails.
    /// </summary>
    /// <remarks>
    /// MS-VBA's type library says the <c>ErrObject</c> class can be created, so <c>New ErrObject</c>
    /// compiles; no server creates one, and the program raises run-time error -2147221164 <em>Class not
    /// registered</em> where it is created (recorded in Excel). The one object of the class is the one the
    /// <c>Err</c> function returns.
    /// </remarks>
    public bool Unregistered { get; init; }

    /// <summary>
    /// The programmatic identifier the class answers to in <c>CreateObject</c> (<c>VBScript.RegExp</c>), if it has one: the name of a COM class the platform
    /// implements itself, found before the machine's own servers are asked - which is what makes a program that creates one by name portable to a machine that
    /// has no such server.
    /// </summary>
    /// <remarks>The part after the last dot is the name of the class.</remarks>
    public string? ProgId { get; init; }

    /// <summary>
    /// What <c>TypeName</c> says of an object of the class, when it is not the class's name: the VBA library's <c>UserForms</c> collection is an object whose class
    /// MS-VBA names nothing, and <c>TypeName(UserForms)</c> is <c>Object</c> (Excel-measured, <c>oracle\s41</c> U01).
    /// </summary>
    public string? TypeName { get; init; }
}
