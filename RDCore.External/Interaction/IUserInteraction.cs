using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.External.Interaction;

/// <summary>
/// What a program shows the person running it, and asks of them: the dialogs of <c>MsgBox</c> and <c>InputBox</c>.
/// </summary>
/// <remarks>
/// The runtime does not know who that person is, nor where: its host says, by composing it with one of these. Windows shows its own dialogs, the interactive shell asks
/// at its console, and a platform with nobody to ask answers as though the dialog had been confirmed at once (<see cref="NoUserInteraction"/>).
/// <para>
/// A call blocks until the dialog is closed, as the program waits for it. The token is the run's: a program that is stopped while a dialog is open stops waiting for it.
/// </para>
/// </remarks>
public interface IUserInteraction
{
    /// <summary>
    /// Shows a message, and waits for one of its buttons to close it.
    /// </summary>
    /// <param name="request">The message, its title, and the buttons it has.</param>
    /// <param name="token">Stops waiting.</param>
    /// <returns>The button that closed the dialog.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="token"/> was cancelled before the dialog was closed.</exception>
    VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default);

    /// <summary>
    /// Asks for a line of text, and waits for it to be confirmed or the dialog cancelled.
    /// </summary>
    /// <param name="request">The message, its title, and the text the dialog starts with.</param>
    /// <param name="token">Stops waiting.</param>
    /// <returns>What was typed - a zero-length string when nothing was - or <see langword="null"/> when the dialog was cancelled.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="token"/> was cancelled before the dialog was closed.</exception>
    string? Prompt(InputBoxRequest request, CancellationToken token = default);
}

/// <summary>
/// A <c>MsgBox</c>: its message, its title, and the <c>VbMsgBoxStyle</c> the program asked for.
/// </summary>
/// <param name="Prompt">The message.</param>
/// <param name="Title">The title of the dialog.</param>
/// <param name="Style">The buttons, the icon, the default button and the modality, summed.</param>
public sealed record class MessageBoxRequest(string Prompt, string Title, VBMsgBoxStyle Style)
{
    private const int ButtonsMask = 0x7;
    private const int IconMask = 0x70;
    private const int DefaultButtonMask = 0x300;
    private const int DefaultButtonShift = 8;

    /// <summary>
    /// The set of buttons: <see cref="VBMsgBoxStyle.VBOKOnly"/> through <see cref="VBMsgBoxStyle.VBRetryCancel"/>. A value that names none of them is
    /// <see cref="VBMsgBoxStyle.VBOKOnly"/>.
    /// </summary>
    public VBMsgBoxStyle Buttons => ((int)Style & ButtonsMask) is var buttons and <= (int)VBMsgBoxStyle.VBRetryCancel
        ? (VBMsgBoxStyle)buttons
        : VBMsgBoxStyle.VBOKOnly;

    /// <summary>
    /// The icon: <see cref="VBMsgBoxStyle.VBCritical"/>, <see cref="VBMsgBoxStyle.VBQuestion"/>, <see cref="VBMsgBoxStyle.VBExclamation"/>,
    /// <see cref="VBMsgBoxStyle.VBInformation"/>, or <c>0</c> for none.
    /// </summary>
    public VBMsgBoxStyle Icon => (VBMsgBoxStyle)((int)Style & IconMask);

    /// <summary>
    /// The buttons of the dialog, in the order they are shown, as the answer each of them gives.
    /// </summary>
    public IReadOnlyList<VBMsgBoxResult> Choices => Buttons switch
    {
        VBMsgBoxStyle.VBOKCancel => [VBMsgBoxResult.VBOK, VBMsgBoxResult.VBCancel],
        VBMsgBoxStyle.VBAbortRetryIgnore => [VBMsgBoxResult.VBAbort, VBMsgBoxResult.VBRetry, VBMsgBoxResult.VBIgnore],
        VBMsgBoxStyle.VBYesNoCancel => [VBMsgBoxResult.VBYes, VBMsgBoxResult.VBNo, VBMsgBoxResult.VBCancel],
        VBMsgBoxStyle.VBYesNo => [VBMsgBoxResult.VBYes, VBMsgBoxResult.VBNo],
        VBMsgBoxStyle.VBRetryCancel => [VBMsgBoxResult.VBRetry, VBMsgBoxResult.VBCancel],
        _ => [VBMsgBoxResult.VBOK],
    };

    /// <summary>
    /// The index in <see cref="Choices"/> of the button that has the focus when the dialog opens: the one <c>vbDefaultButton1</c> through <c>vbDefaultButton4</c>
    /// names, or the first when the dialog has no such button - as Windows has it.
    /// </summary>
    public int DefaultChoice => (((int)Style & DefaultButtonMask) >> DefaultButtonShift) is var index && index < Choices.Count ? index : 0;

    /// <summary>
    /// The answer of a dialog closed at window level - <kbd>Esc</kbd>, or its close button - without choosing a button: <see cref="VBMsgBoxResult.VBCancel"/> when
    /// it has a <em>Cancel</em> button, <see cref="VBMsgBoxResult.VBOK"/> when <em>OK</em> is its only one, and <see langword="null"/> for a dialog that cannot be
    /// closed that way (<see cref="VBMsgBoxStyle.VBYesNo"/>, <see cref="VBMsgBoxStyle.VBAbortRetryIgnore"/>), as Windows has it.
    /// </summary>
    public VBMsgBoxResult? EscapeResult => Choices switch
    {
        [VBMsgBoxResult.VBOK] => VBMsgBoxResult.VBOK,
        var choices when choices.Contains(VBMsgBoxResult.VBCancel) => VBMsgBoxResult.VBCancel,
        _ => null,
    };
}

/// <summary>
/// An <c>InputBox</c>: its message, its title, the text it starts with, and where it is.
/// </summary>
/// <param name="Prompt">The message.</param>
/// <param name="Title">The title of the dialog.</param>
/// <param name="Default">The text the dialog starts with.</param>
/// <param name="XPos">Where the left edge of the dialog is, in twips from the left of the screen; <see langword="null"/> to center it.</param>
/// <param name="YPos">Where the top edge of the dialog is, in twips from the top of the screen; <see langword="null"/> to center it.</param>
public sealed record class InputBoxRequest(string Prompt, string Title, string Default = "", int? XPos = null, int? YPos = null);

/// <summary>
/// The dialogs of a platform that has nobody to ask - a build machine, a service: each is answered as though it had been confirmed the moment it opened.
/// </summary>
/// <remarks>
/// A <c>MsgBox</c> answers its default button and an <c>InputBox</c> the text it starts with, which is what pressing <kbd>Enter</kbd> at once would have done; nothing
/// is shown, and nothing waits.
/// </remarks>
public sealed class NoUserInteraction : IUserInteraction
{
    /// <summary>The one there is: it has nothing to hold.</summary>
    public static NoUserInteraction Instance { get; } = new();

    private NoUserInteraction() { }

    /// <inheritdoc/>
    public VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default) => request.Choices[request.DefaultChoice];

    /// <inheritdoc/>
    public string? Prompt(InputBoxRequest request, CancellationToken token = default) => request.Default;
}
