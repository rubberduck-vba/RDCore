namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.1.8 VbFileAttribute</strong>
/// </summary>
/// <remarks>
/// This <c>enum</c> is used to encode the return value of the <see cref="IStdInteractionModule.StdInteraction__GetAttr"/> function.<br/>
/// 👉 The values of this enum are powers of 2, suggesting they are intended to be combined and used with bitwise logic.
/// </remarks>
[Flags]
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

