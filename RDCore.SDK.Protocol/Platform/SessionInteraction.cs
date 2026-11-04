namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// What a program shows in a <c>MsgBox</c>, as it travels to whoever shows it.
/// </summary>
/// <remarks>
/// A program runs in the environment host, and the person it asks is at a client: the request goes from the host to the language server
/// (<c>rdcore/host/interaction/msgBox</c>) and from the language server to the client (<see cref="SessionMessageBoxParams"/>), and the answer comes back
/// the same way. The program waits for it, as it waits for a dialog.
/// </remarks>
public abstract record class MessageBoxParams
{
    /// <summary>The message.</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>The title of the dialog.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The <c>VbMsgBoxStyle</c> the program asked for: the buttons, the icon, the default button and the modality, summed.</summary>
    public int Style { get; init; }

    /// <summary>
    /// The lines the program printed before it opened the dialog that have not been said yet, in order: shown before the dialog, and not again with the answer to the
    /// request that runs the program.
    /// </summary>
    public IReadOnlyList<string> Output { get; init; } = [];
}

/// <summary>
/// What a program asks for in an <c>InputBox</c>, as it travels to whoever asks.
/// </summary>
/// <remarks>See <see cref="MessageBoxParams"/>.</remarks>
public abstract record class InputBoxParams
{
    /// <summary>The message.</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>The title of the dialog.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The text the dialog starts with.</summary>
    public string Default { get; init; } = string.Empty;

    /// <summary>Where the left edge of the dialog is, in twips from the left of the screen; <see langword="null"/> to center it.</summary>
    public int? XPos { get; init; }

    /// <summary>Where the top edge of the dialog is, in twips from the top of the screen; <see langword="null"/> to center it.</summary>
    public int? YPos { get; init; }

    /// <inheritdoc cref="MessageBoxParams.Output"/>
    public IReadOnlyList<string> Output { get; init; } = [];
}

/// <summary>
/// Which button closed a <c>MsgBox</c>, or that nobody was asked.
/// </summary>
public sealed record class MessageBoxResult
{
    /// <summary>
    /// Whether someone was asked. <see langword="false"/> when the one asked has nobody to ask - a client that did not say it shows dialogs - and the host shows the
    /// dialog the platform it runs on has.
    /// </summary>
    public bool Handled { get; init; }

    /// <summary>The <c>VbMsgBoxResult</c> of the button that closed the dialog.</summary>
    public int Result { get; init; }

    /// <summary>
    /// Whether the one asked shows dialogs, and declined to answer this one: nobody else will, and the program's run fails (<c>VBR50001</c>). The language server
    /// says so of a client that said it shows dialogs and did not handle one; the host shows no dialog of its own then.
    /// </summary>
    public bool Declined { get; init; }

    /// <summary>
    /// Why the answer cannot be used, when it cannot: a <see cref="Result"/> that is none of the dialog's buttons. The program's run fails with it
    /// (<c>VBR50002</c>), naming the client, the buttons and the answer. <see langword="null"/> for an answer that can be used.
    /// </summary>
    public string? Failure { get; init; }

    /// <summary>The answer of one who has nobody to ask.</summary>
    public static MessageBoxResult NotHandled { get; } = new();
}

/// <summary>
/// What was typed in an <c>InputBox</c>, that it was cancelled, or that nobody was asked.
/// </summary>
public sealed record class InputBoxResult
{
    /// <inheritdoc cref="MessageBoxResult.Handled"/>
    public bool Handled { get; init; }

    /// <summary>
    /// Whether the dialog was cancelled - its <em>Cancel</em> button, or <kbd>Esc</kbd> - which the program is told with <c>vbNullString</c>, and not with
    /// the zero-length string of a dialog confirmed with nothing typed.
    /// </summary>
    public bool Cancelled { get; init; }

    /// <summary>What was typed; ignored when <see cref="Cancelled"/>.</summary>
    public string Value { get; init; } = string.Empty;

    /// <inheritdoc cref="MessageBoxResult.Declined"/>
    public bool Declined { get; init; }

    /// <inheritdoc cref="MessageBoxResult.NotHandled"/>
    public static InputBoxResult NotHandled { get; } = new();
}

/// <summary>
/// Request <c>rdcore/session/interaction/msgBox</c>, which a client handles: the language server asks a client that said it shows dialogs (the
/// <c>SessionInteraction</c> capability) to answer a program's <c>MsgBox</c>, and is answered a <see cref="MessageBoxResult"/>.
/// </summary>
public record class SessionMessageBoxParams : MessageBoxParams;

/// <summary>
/// Request <c>rdcore/session/interaction/inputBox</c>, which a client handles: the language server asks a client that said it shows dialogs (the
/// <c>SessionInteraction</c> capability) to answer a program's <c>InputBox</c>, and is answered an <see cref="InputBoxResult"/>.
/// </summary>
public record class SessionInputBoxParams : InputBoxParams;
