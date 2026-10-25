using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.1 ColorConstants Module</strong>
/// </summary>
/// <remarks>
/// The eight colours of the basic palette, each a <c>Long</c> red-green-blue value (<c>&amp;HBBGGRR</c>), as <c>RGB</c> would make it.
/// <para>
/// 👉 Each field is one of the module's <c>Const</c> members, declared as the value it is: <see cref="RDCore.SDK.Runtime.StdLib.StdLibSymbolReader"/> reads
/// it as a <c>Public Const</c> of that value's type. As in MS-VBA, a constant is reached by its own name (<c>vbRed</c>), or qualified by the module or the
/// library (<c>ColorConstants.vbRed</c>, <c>VBA.vbRed</c>), and assigning to one is a compile error. Nothing implements this interface.
/// </para>
/// </remarks>
[StdLibModule]
public interface IStdColorConstantsModule
{
    /// <summary><c>vbBlack</c>: black, <c>0</c>.</summary>
    public static readonly VBLongValue VBBlack = new(0);

    /// <summary><c>vbBlue</c>: blue, <c>&amp;HFF0000</c>.</summary>
    public static readonly VBLongValue VBBlue = new(16711680);

    /// <summary><c>vbCyan</c>: cyan, <c>&amp;HFFFF00</c>.</summary>
    public static readonly VBLongValue VBCyan = new(16776960);

    /// <summary><c>vbGreen</c>: green, <c>&amp;HFF00</c>.</summary>
    public static readonly VBLongValue VBGreen = new(65280);

    /// <summary><c>vbMagenta</c>: magenta, <c>&amp;HFF00FF</c>.</summary>
    public static readonly VBLongValue VBMagenta = new(16711935);

    /// <summary><c>vbRed</c>: red, <c>&amp;HFF</c>.</summary>
    public static readonly VBLongValue VBRed = new(255);

    /// <summary><c>vbWhite</c>: white, <c>&amp;HFFFFFF</c>.</summary>
    public static readonly VBLongValue VBWhite = new(16777215);

    /// <summary><c>vbYellow</c>: yellow, <c>&amp;HFFFF</c>.</summary>
    public static readonly VBLongValue VBYellow = new(65535);
}
