using RDCore.SDK.Extensibility.Diagnostics;
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
    /// <exception cref="DialogNotAnsweredException">The dialog cannot be answered, and the run fails with the error it says.</exception>
    VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default);

    /// <summary>
    /// Asks for a line of text, and waits for it to be confirmed or the dialog cancelled.
    /// </summary>
    /// <param name="request">The message, its title, and the text the dialog starts with.</param>
    /// <param name="token">Stops waiting.</param>
    /// <returns>What was typed - a zero-length string when nothing was - or <see langword="null"/> when the dialog was cancelled.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="token"/> was cancelled before the dialog was closed.</exception>
    /// <exception cref="DialogNotAnsweredException">The dialog cannot be answered, and the run fails with the error it says.</exception>
    string? Prompt(InputBoxRequest request, CancellationToken token = default);
}

/// <summary>
/// A dialog that cannot be answered: the run fails with an untrappable error of RD-VBA's own (VBR50000-50999), rather than wait on a dialog nobody sees, or go
/// on with an answer nobody gave.
/// </summary>
/// <param name="Code">The diagnostic code of the error the run fails with.</param>
/// <param name="message">Why, as the error says it: who was asked, and what they answered.</param>
public sealed class DialogNotAnsweredException(RDCoreDiagnosticCode Code, string message) : Exception(message)
{
    /// <summary>
    /// The diagnostic code of the error the run fails with.
    /// </summary>
    public RDCoreDiagnosticCode Code { get; } = Code;
}

/// <summary>
/// A <c>MsgBox</c>: its message, its title, the <c>VbMsgBoxStyle</c> the program asked for, and the help topic it names (MS-VBAL §6.1.2.8.1.13). What the
/// style says of the buttons, the icon, the default button and the answer of <kbd>Esc</kbd> is <see cref="VBMsgBoxButtons"/>'s to read.
/// </summary>
/// <param name="Prompt">The message.</param>
/// <param name="Title">The title of the dialog.</param>
/// <param name="Style">The buttons, the icon, the default button and the modality, summed.</param>
/// <param name="HelpFile">The help file the dialog's help opens; <see langword="null"/> when the program names none.</param>
/// <param name="Context">The topic of <paramref name="HelpFile"/> the dialog's help opens; <see langword="null"/> when the program names none.</param>
public sealed record class MessageBoxRequest(string Prompt, string Title, VBMsgBoxStyle Style, string? HelpFile = null, int? Context = null);

/// <summary>
/// An <c>InputBox</c>: its message, its title, the text it starts with, where it is, and the help topic it names (MS-VBAL §6.1.2.8.1.12).
/// </summary>
/// <param name="Prompt">The message.</param>
/// <param name="Title">The title of the dialog.</param>
/// <param name="Default">The text the dialog starts with.</param>
/// <param name="XPos">Where the left edge of the dialog is, in twips from the left of the screen; <see langword="null"/> to center it.</param>
/// <param name="YPos">Where the top edge of the dialog is, in twips from the top of the screen; <see langword="null"/> to center it.</param>
/// <param name="HelpFile">The help file the dialog's help opens; <see langword="null"/> when the program names none.</param>
/// <param name="Context">The topic of <paramref name="HelpFile"/> the dialog's help opens; <see langword="null"/> when the program names none.</param>
public sealed record class InputBoxRequest(
    string Prompt, string Title, string Default = "", int? XPos = null, int? YPos = null, string? HelpFile = null, int? Context = null)
{
    /// <summary>
    /// Whether the dialog has help, which <kbd>F1</kbd> and a <em>Help</em> button open: an <c>InputBox</c> has it when the program names both its help file
    /// and its topic (MS-VBAL §6.1.2.8.1.12).
    /// </summary>
    public bool HasHelp => HelpFile is not null && Context is not null;
}

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
    public VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default)
        => VBMsgBoxButtons.ChoicesOf(request.Style)[VBMsgBoxButtons.DefaultChoiceOf(request.Style)];

    /// <inheritdoc/>
    public string? Prompt(InputBoxRequest request, CancellationToken token = default) => request.Default;
}
