using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.12 SystemColorConstants</strong>
/// </summary>
/// <remarks>
/// The colours the system draws its elements with, each a <c>Long</c> with its high bit set (<c>&amp;H80000000</c> and up, so negative), which is what tells a
/// system colour from a red-green-blue value: what colour it is, is the system's to say (<strong>MS-VBAL §6.1.2.12</strong>: "implementation-dependent").
/// <c>vb3DFace</c> and <c>vb3DShadow</c> are <c>vbButtonFace</c> and <c>vbButtonShadow</c> by other names.
/// <para>
/// 👉 Each field is one of the module's <c>Const</c> members, declared as the value it is: <see cref="RDCore.SDK.Runtime.StdLib.StdLibSymbolReader"/> reads
/// it as a <c>Public Const</c> of that value's type. A constant is reached by its own name (<c>vbButtonFace</c>), or qualified by the module or the library
/// (<c>SystemColorConstants.vbButtonFace</c>, <c>VBA.vbButtonFace</c>). Nothing implements this interface.
/// </para>
/// </remarks>
[StdLibModule]
public interface IStdSystemColorConstantsModule
{
    /// <summary><c>vbScrollBars</c>: the colour of a scroll bar, <c>&amp;H80000000</c>.</summary>
    public static readonly VBLongValue VBScrollBars = new(unchecked((int)0x80000000));

    /// <summary><c>vbDesktop</c>: the colour of the desktop, <c>&amp;H80000001</c>.</summary>
    public static readonly VBLongValue VBDesktop = new(unchecked((int)0x80000001));

    /// <summary><c>vbActiveTitleBar</c>: the colour of the title bar of the active window, <c>&amp;H80000002</c>.</summary>
    public static readonly VBLongValue VBActiveTitleBar = new(unchecked((int)0x80000002));

    /// <summary><c>vbInactiveTitleBar</c>: the colour of the title bar of an inactive window, <c>&amp;H80000003</c>.</summary>
    public static readonly VBLongValue VBInactiveTitleBar = new(unchecked((int)0x80000003));

    /// <summary><c>vbMenuBar</c>: the background colour of a menu, <c>&amp;H80000004</c>.</summary>
    public static readonly VBLongValue VBMenuBar = new(unchecked((int)0x80000004));

    /// <summary><c>vbWindowBackground</c>: the background colour of a window, <c>&amp;H80000005</c>.</summary>
    public static readonly VBLongValue VBWindowBackground = new(unchecked((int)0x80000005));

    /// <summary><c>vbWindowFrame</c>: the colour of the frame of a window, <c>&amp;H80000006</c>.</summary>
    public static readonly VBLongValue VBWindowFrame = new(unchecked((int)0x80000006));

    /// <summary><c>vbMenuText</c>: the colour of the text of a menu, <c>&amp;H80000007</c>.</summary>
    public static readonly VBLongValue VBMenuText = new(unchecked((int)0x80000007));

    /// <summary><c>vbWindowText</c>: the colour of the text of a window, <c>&amp;H80000008</c>.</summary>
    public static readonly VBLongValue VBWindowText = new(unchecked((int)0x80000008));

    /// <summary><c>vbTitleBarText</c>: the colour of the text of a caption, size box and scroll arrow, <c>&amp;H80000009</c>.</summary>
    public static readonly VBLongValue VBTitleBarText = new(unchecked((int)0x80000009));

    /// <summary><c>vbActiveBorder</c>: the colour of the border of the active window, <c>&amp;H8000000A</c>.</summary>
    public static readonly VBLongValue VBActiveBorder = new(unchecked((int)0x8000000A));

    /// <summary><c>vbInactiveBorder</c>: the colour of the border of an inactive window, <c>&amp;H8000000B</c>.</summary>
    public static readonly VBLongValue VBInactiveBorder = new(unchecked((int)0x8000000B));

    /// <summary><c>vbApplicationWorkspace</c>: the background colour of a multiple-document interface, <c>&amp;H8000000C</c>.</summary>
    public static readonly VBLongValue VBApplicationWorkspace = new(unchecked((int)0x8000000C));

    /// <summary><c>vbHighlight</c>: the background colour of what is selected in a control, <c>&amp;H8000000D</c>.</summary>
    public static readonly VBLongValue VBHighlight = new(unchecked((int)0x8000000D));

    /// <summary><c>vbHighlightText</c>: the colour of the text of what is selected in a control, <c>&amp;H8000000E</c>.</summary>
    public static readonly VBLongValue VBHighlightText = new(unchecked((int)0x8000000E));

    /// <summary><c>vbButtonFace</c>: the colour of the face of a command button, <c>&amp;H8000000F</c>.</summary>
    public static readonly VBLongValue VBButtonFace = new(unchecked((int)0x8000000F));

    /// <summary><c>vbButtonShadow</c>: the colour of the edge of a command button, <c>&amp;H80000010</c>.</summary>
    public static readonly VBLongValue VBButtonShadow = new(unchecked((int)0x80000010));

    /// <summary><c>vbGrayText</c>: the colour of grayed (disabled) text, <c>&amp;H80000011</c>.</summary>
    public static readonly VBLongValue VBGrayText = new(unchecked((int)0x80000011));

    /// <summary><c>vbButtonText</c>: the colour of the text of a push button, <c>&amp;H80000012</c>.</summary>
    public static readonly VBLongValue VBButtonText = new(unchecked((int)0x80000012));

    /// <summary><c>vbInactiveCaptionText</c>: the colour of the text of the caption of an inactive window, <c>&amp;H80000013</c>.</summary>
    public static readonly VBLongValue VBInactiveCaptionText = new(unchecked((int)0x80000013));

    /// <summary><c>vb3DHighlight</c>: the highlight colour of a 3D element, <c>&amp;H80000014</c>.</summary>
    public static readonly VBLongValue VB3DHighlight = new(unchecked((int)0x80000014));

    /// <summary><c>vb3DDKShadow</c>: the darkest shadow colour of a 3D element, <c>&amp;H80000015</c>.</summary>
    public static readonly VBLongValue VB3DDKShadow = new(unchecked((int)0x80000015));

    /// <summary><c>vb3DLight</c>: the second lightest colour of a 3D element, after <c>vb3DHighlight</c>, <c>&amp;H80000016</c>.</summary>
    public static readonly VBLongValue VB3DLight = new(unchecked((int)0x80000016));

    /// <summary><c>vb3DFace</c>: the colour of the face of a 3D element, <c>&amp;H8000000F</c> - <c>vbButtonFace</c>.</summary>
    public static readonly VBLongValue VB3DFace = new(unchecked((int)0x8000000F));

    /// <summary><c>vb3DShadow</c>: the shadow colour of a 3D element, <c>&amp;H80000010</c> - <c>vbButtonShadow</c>.</summary>
    public static readonly VBLongValue VB3DShadow = new(unchecked((int)0x80000010));

    /// <summary><c>vbInfoText</c>: the colour of the text of a tooltip, <c>&amp;H80000017</c>.</summary>
    public static readonly VBLongValue VBInfoText = new(unchecked((int)0x80000017));

    /// <summary><c>vbInfoBackground</c>: the background colour of a tooltip, <c>&amp;H80000018</c>.</summary>
    public static readonly VBLongValue VBInfoBackground = new(unchecked((int)0x80000018));
}
