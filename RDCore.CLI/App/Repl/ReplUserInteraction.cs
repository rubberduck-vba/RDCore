using RDCore.CLI.App.Console;
using RDCore.External.Interaction;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The dialogs of a program run from the shell, asked at the shell's console: each is a box with a border (<see cref="ConsoleDialogBox"/>), centred on the
/// console, with its title in the top edge and its message inside. A <c>MsgBox</c> ends with a row of buttons (<see cref="ConsoleMenu"/>), and an
/// <c>InputBox</c> with a line to type on (<see cref="ConsoleLineEditor"/>).
/// </summary>
/// <remarks>
/// <para>
/// A <c>MsgBox</c> starts with its default button selected, and the button chosen is the answer. <kbd>Esc</kbd> closes it the way its close button would - the answer
/// is <c>vbCancel</c>, or <c>vbOK</c> for a message with only that button - and does nothing for one that has no way to be closed but to choose
/// (<c>vbYesNo</c>, <c>vbAbortRetryIgnore</c>).
/// </para>
/// <para>
/// An <c>InputBox</c> starts with its default text, selected; <kbd>Enter</kbd> confirms what is typed, and <kbd>Esc</kbd> cancels, which the program is told with
/// <c>vbNullString</c> and not with the zero-length string of a line confirmed with nothing on it.
/// </para>
/// <para>
/// A shell whose input is a script reads a line instead of keys: for a <c>MsgBox</c>, the name or the key of a button (an empty line is the default button, and the end of
/// the input closes it as <kbd>Esc</kbd> would), and for an <c>InputBox</c> the text (the end of the input cancels it).
/// </para>
/// </remarks>
/// <param name="keys">Where the keys are read.</param>
/// <param name="output">Where the dialogs are drawn.</param>
/// <param name="ansi">
/// Whether <paramref name="output"/> understands ANSI escape sequences, which show the selected default text of an input box, and draw the whole box before it is
/// answered.
/// </param>
/// <param name="consoleWidth">The cells of a line of the console, which a box is centred in; 80 when it is not said.</param>
public sealed class ReplUserInteraction(IConsoleKeys keys, TextWriter output, bool ansi = false, Func<int>? consoleWidth = null) : IUserInteraction
{
    private const int DefaultConsoleWidth = 80;
    private const string InputPrompt = "> ";

    // the cells the line of an input box is given, at the least: room enough for an answer that is a sentence.
    private const int InputWidth = 40;

    // what a button is called and which key chooses it, as a Windows dialog has it.
    private static string LabelOf(VBMsgBoxResult choice) => choice switch
    {
        VBMsgBoxResult.VBOk => "&OK",
        VBMsgBoxResult.VBCancel => "&Cancel",
        VBMsgBoxResult.VBAbort => "&Abort",
        VBMsgBoxResult.VBRetry => "&Retry",
        VBMsgBoxResult.VBIgnore => "&Ignore",
        VBMsgBoxResult.VBYes => "&Yes",
        VBMsgBoxResult.VBNo => "&No",
        _ => choice.ToString(),
    };

    private static string GlyphOf(VBMsgBoxStyle icon) => icon switch
    {
        VBMsgBoxStyle.VBCritical => "⛔",
        VBMsgBoxStyle.VBQuestion => "❓",
        VBMsgBoxStyle.VBExclamation => "⚠️",
        VBMsgBoxStyle.VBInformation => "ℹ️",
        _ => string.Empty,
    };

    private int ConsoleWidth => consoleWidth?.Invoke() is > 0 and var width ? width : DefaultConsoleWidth;

    /// <inheritdoc/>
    public VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default)
    {
        var labels = request.Choices.Select(LabelOf).ToArray();
        var menu = new ConsoleMenu(labels, request.DefaultChoice, canDismiss: request.EscapeResult is not null);
        var buttons = menu.Render();
        var box = new ConsoleDialogBox(request.Title, GlyphOf(request.Icon), request.Prompt, ConsoleText.WidthOf(buttons), ConsoleWidth, ConsoleBorder.For(output), separated: true, ansi);
        var (before, after) = box.Centring(buttons);

        if (!keys.IsInteractive)
        {
            box.Show(output, box.RowPrefix + before + buttons + after + box.RowSuffix);
            return AnswerFromLine(request, menu);
        }

        var chosen = box.Show(output, ansi, () => menu.Run(keys, output, box.RowPrefix + before, token, after + box.RowSuffix));
        return chosen is { } index ? request.Choices[index] : request.EscapeResult!.Value;
    }

    // a line of a script names a button by its text or its key; anything else is asked again, until the script runs out.
    private VBMsgBoxResult AnswerFromLine(MessageBoxRequest request, ConsoleMenu menu)
    {
        while (true)
        {
            var line = keys.ReadLine(CancellationToken.None)?.Trim();
            if (line is null)
            {
                return request.EscapeResult ?? request.Choices[request.DefaultChoice];
            }

            if (line.Length == 0)
            {
                return request.Choices[request.DefaultChoice];
            }

            var index = Array.FindIndex(menu.Items.ToArray(), item =>
                string.Equals(ConsoleMenu.TextOf(item), line, StringComparison.OrdinalIgnoreCase)
                || line.Length == 1 && ConsoleMenu.KeyOf(item) == char.ToUpperInvariant(line[0]));
            if (index >= 0)
            {
                return request.Choices[index];
            }
        }
    }

    /// <inheritdoc/>
    public string? Prompt(InputBoxRequest request, CancellationToken token = default)
    {
        var box = new ConsoleDialogBox(request.Title, string.Empty, request.Prompt, InputWidth, ConsoleWidth, ConsoleBorder.For(output), separated: false, ansi);
        var field = box.Width - InputPrompt.Length;
        if (!keys.IsInteractive)
        {
            box.Show(output, box.Row(InputPrompt + ConsoleText.Truncate(request.Default, field)));
            return keys.ReadLine(token);
        }

        return box.Show(output, ansi, () => new ConsoleLineEditor(request.Default).Run(keys, output, box.RowPrefix + InputPrompt, ansi, token, field, box.RowSuffix));
    }
}
