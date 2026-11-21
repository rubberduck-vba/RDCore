using RDCore.External.Interaction;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.External.Protocol;

/// <summary>
/// A request for a dialog the external host shows beside its forms: the number the environment host gave it, which
/// <see cref="ExternalProtocol.FormCloseDialog"/> closes it by.
/// </summary>
public abstract record class DialogRequest : AutomationRequest
{
    /// <summary>
    /// The dialog's number, unique among the dialogs the environment host has open.
    /// </summary>
    public long Dialog { get; init; }
}

/// <summary>
/// Request for <see cref="ExternalProtocol.FormMessageBox"/>: <see cref="IUserInteraction.ShowMessage"/>.
/// </summary>
public sealed record class FormMessageBoxParams : DialogRequest
{
    /// <summary>
    /// The message.
    /// </summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>
    /// The title of the dialog.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// The <c>VbMsgBoxStyle</c> the program asked for.
    /// </summary>
    public int Style { get; init; }

    /// <summary>
    /// The help file the dialog's help opens; <see langword="null"/> when the program names none.
    /// </summary>
    public string? HelpFile { get; init; }

    /// <summary>
    /// The topic of <see cref="HelpFile"/> the dialog's help opens; <see langword="null"/> when the program names none.
    /// </summary>
    public int? Context { get; init; }

    /// <summary>
    /// The request for a message box.
    /// </summary>
    /// <param name="request">The message box.</param>
    /// <param name="dialog">The dialog's number.</param>
    /// <param name="handlingEvent">The event whose handler shows it; <c>0</c> when none does.</param>
    public static FormMessageBoxParams Of(MessageBoxRequest request, long dialog, long handlingEvent) => new()
    {
        Dialog = dialog,
        Prompt = request.Prompt,
        Title = request.Title,
        Style = (int)request.Style,
        HelpFile = request.HelpFile,
        Context = request.Context,
        HandlingEvent = handlingEvent,
    };

    /// <summary>
    /// The message box this request is for.
    /// </summary>
    public MessageBoxRequest ToRequest() => new(Prompt, Title, (VBMsgBoxStyle)Style, HelpFile, Context);
}

/// <summary>
/// The answer to <see cref="ExternalProtocol.FormMessageBox"/>.
/// </summary>
public sealed record class FormMessageBoxResult : ExternalResult
{
    /// <summary>
    /// The <c>VbMsgBoxResult</c> of the button that closed the dialog.
    /// </summary>
    public int Result { get; init; }
}

/// <summary>
/// Request for <see cref="ExternalProtocol.FormInputBox"/>: <see cref="IUserInteraction.Prompt"/>.
/// </summary>
public sealed record class FormInputBoxParams : DialogRequest
{
    /// <summary>
    /// The message.
    /// </summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>
    /// The title of the dialog.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// The text the dialog starts with.
    /// </summary>
    public string Default { get; init; } = string.Empty;

    /// <summary>
    /// Where the left edge of the dialog is, in twips from the left of the screen; <see langword="null"/> to center it.
    /// </summary>
    public int? XPos { get; init; }

    /// <summary>
    /// Where the top edge of the dialog is, in twips from the top of the screen; <see langword="null"/> to center it.
    /// </summary>
    public int? YPos { get; init; }

    /// <summary>
    /// The help file the dialog's help opens; <see langword="null"/> when the program names none.
    /// </summary>
    public string? HelpFile { get; init; }

    /// <summary>
    /// The topic of <see cref="HelpFile"/> the dialog's help opens; <see langword="null"/> when the program names none.
    /// </summary>
    public int? Context { get; init; }

    /// <summary>
    /// The request for an input box.
    /// </summary>
    /// <param name="request">The input box.</param>
    /// <param name="dialog">The dialog's number.</param>
    /// <param name="handlingEvent">The event whose handler shows it; <c>0</c> when none does.</param>
    public static FormInputBoxParams Of(InputBoxRequest request, long dialog, long handlingEvent) => new()
    {
        Dialog = dialog,
        Prompt = request.Prompt,
        Title = request.Title,
        Default = request.Default,
        XPos = request.XPos,
        YPos = request.YPos,
        HelpFile = request.HelpFile,
        Context = request.Context,
        HandlingEvent = handlingEvent,
    };

    /// <summary>
    /// The input box this request is for.
    /// </summary>
    public InputBoxRequest ToRequest() => new(Prompt, Title, Default, XPos, YPos, HelpFile, Context);
}

/// <summary>
/// The answer to <see cref="ExternalProtocol.FormInputBox"/>.
/// </summary>
public sealed record class FormInputBoxResult : ExternalResult
{
    /// <summary>
    /// What was typed, a zero-length string when nothing was; <see langword="null"/> when the dialog was cancelled.
    /// </summary>
    public string? Value { get; init; }
}

/// <summary>
/// Request for <see cref="ExternalProtocol.FormCloseDialog"/>.
/// </summary>
public sealed record class FormCloseDialogParams : DialogRequest;
