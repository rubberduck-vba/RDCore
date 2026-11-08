namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// The buttons a <c>MsgBox</c> has, by its style (<strong>MS-VBAL 6.1.1.12</strong> <c>VbMsgBoxStyle</c>, <strong>6.1.1.11</strong> <c>VbMsgBoxResult</c>): what
/// whoever shows the dialog may answer it with.
/// </summary>
public static class VBMsgBoxButtons
{
    private const int ButtonsMask = 0x7;

    /// <summary>
    /// The set of buttons of a style: <see cref="VBMsgBoxStyle.VBOKOnly"/> through <see cref="VBMsgBoxStyle.VBRetryCancel"/>. A value that names none of them
    /// is <see cref="VBMsgBoxStyle.VBOKOnly"/>.
    /// </summary>
    /// <param name="style">The style a program asked for: the buttons, the icon, the default button and the modality, summed.</param>
    public static VBMsgBoxStyle Of(VBMsgBoxStyle style) => ((int)style & ButtonsMask) is var buttons and <= (int)VBMsgBoxStyle.VBRetryCancel
        ? (VBMsgBoxStyle)buttons
        : VBMsgBoxStyle.VBOKOnly;

    /// <summary>
    /// The buttons of a dialog of the style, in the order they are shown, as the answer each of them gives.
    /// </summary>
    /// <param name="style">The style a program asked for.</param>
    public static IReadOnlyList<VBMsgBoxResult> ChoicesOf(VBMsgBoxStyle style) => Of(style) switch
    {
        VBMsgBoxStyle.VBOKCancel => [VBMsgBoxResult.VBOK, VBMsgBoxResult.VBCancel],
        VBMsgBoxStyle.VBAbortRetryIgnore => [VBMsgBoxResult.VBAbort, VBMsgBoxResult.VBRetry, VBMsgBoxResult.VBIgnore],
        VBMsgBoxStyle.VBYesNoCancel => [VBMsgBoxResult.VBYes, VBMsgBoxResult.VBNo, VBMsgBoxResult.VBCancel],
        VBMsgBoxStyle.VBYesNo => [VBMsgBoxResult.VBYes, VBMsgBoxResult.VBNo],
        VBMsgBoxStyle.VBRetryCancel => [VBMsgBoxResult.VBRetry, VBMsgBoxResult.VBCancel],
        _ => [VBMsgBoxResult.VBOK],
    };

    /// <summary>
    /// The name a program writes the set of buttons of the style with: <c>vbOKOnly</c>, <c>vbYesNo</c>, ...
    /// </summary>
    /// <param name="style">The style a program asked for.</param>
    public static string NameOf(VBMsgBoxStyle style) => Of(style) switch
    {
        VBMsgBoxStyle.VBOKCancel => "vbOKCancel",
        VBMsgBoxStyle.VBAbortRetryIgnore => "vbAbortRetryIgnore",
        VBMsgBoxStyle.VBYesNoCancel => "vbYesNoCancel",
        VBMsgBoxStyle.VBYesNo => "vbYesNo",
        VBMsgBoxStyle.VBRetryCancel => "vbRetryCancel",
        _ => "vbOKOnly",
    };
}
