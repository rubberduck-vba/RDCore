namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.1.8 VbFileAttribute</strong>
/// </summary>
/// <remarks>
/// These values encode the return value of the <c>GetAttr</c> function. Each is a power of 2, so they combine with
/// bitwise operators.
/// </remarks>
[Flags]
[StdLibEnum]
public enum VBFileAttribute
{
    /// <summary>
    /// Specifies files with no attributes.
    /// </summary>
    VBNormal = 0,
    /// <summary>
    /// Specifies <em>read-only</em> files.
    /// </summary>
    VBReadOnly = 1,
    /// <summary>
    /// Specifies <em>hidden</em> files.
    /// </summary>
    VBHidden = 2,
    /// <summary>
    /// Specifies <em>system</em> files.
    /// </summary>
    VBSystem = 4,
    /// <summary>
    /// Specifies <em>volume label</em>; if any other attribute is specified, <c>VBVolume</c> is ignored.
    /// </summary>
    VBVolume = 8,
    /// <summary>
    /// Specifies <em>directories</em> (<em>folders</em>).
    /// </summary>
    VBDirectory = 16,
    /// <summary>
    /// Specifies <em>files that have changed since the last backup</em>.
    /// </summary>
    VBArchive = 32,
    /// <summary>
    /// Specifies <em>file aliases</em> on platforms that support them.
    /// </summary>
    VBAlias = 64,
}

