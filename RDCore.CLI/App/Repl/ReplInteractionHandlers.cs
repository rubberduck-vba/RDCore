using OmniSharp.Extensions.JsonRpc;
using RDCore.External.Interaction;
using RDCore.CLI.ConsoleIO.Model;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// Handles <c>rdcore/session/interaction/msgBox</c>: a program run from the shell opened a <c>MsgBox</c>, and the shell answers it at its console.
/// </summary>
/// <remarks>
/// What the program printed before it opened the dialog comes with it, and is written first: the answer to the request that runs the program, which carries the rest
/// of what it printed, comes when the program is over.
/// </remarks>
/// <param name="interaction">The shell's dialogs.</param>
/// <param name="console">The shell's console.</param>
internal sealed class ReplMessageBoxHandler(ConsoleUserInteraction interaction, IReplConsole console) : IJsonRpcRequestHandler<SessionMessageBoxRequest, MessageBoxResult>
{
    /// <inheritdoc/>
    public Task<MessageBoxResult> Handle(SessionMessageBoxRequest request, CancellationToken cancellationToken)
        // the console is read on a thread of its own: the dialog waits for a person, and the connection does not.
        => Task.Run(() =>
        {
            ReplInteraction.WriteOutput(console, request.Output);
            var answer = interaction.ShowMessage(new MessageBoxRequest(request.Prompt, request.Title, (VBMsgBoxStyle)request.Style), cancellationToken);
            return new MessageBoxResult { Handled = true, Result = (int)answer };
        }, cancellationToken);
}

/// <summary>
/// Handles <c>rdcore/session/interaction/inputBox</c>: a program run from the shell opened an <c>InputBox</c>, and the shell asks for the line at its console.
/// </summary>
/// <param name="interaction">The shell's dialogs.</param>
/// <param name="console">The shell's console.</param>
internal sealed class ReplInputBoxHandler(ConsoleUserInteraction interaction, IReplConsole console) : IJsonRpcRequestHandler<SessionInputBoxRequest, InputBoxResult>
{
    /// <inheritdoc/>
    public Task<InputBoxResult> Handle(SessionInputBoxRequest request, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            ReplInteraction.WriteOutput(console, request.Output);
            var answer = interaction.Prompt(new InputBoxRequest(request.Prompt, request.Title, request.Default, request.XPos, request.YPos), cancellationToken);
            return answer is null
                ? new InputBoxResult { Handled = true, Cancelled = true }
                : new InputBoxResult { Handled = true, Value = answer };
        }, cancellationToken);
}

/// <summary>
/// Handles <c>window/showMessageRequest</c> (<strong>LSP 3.17</strong>): the language server asks the shell's user to choose, as it does whether to regenerate
/// a project file it cannot read, and the shell asks at its console.
/// </summary>
/// <remarks>
/// A shell that runs unattended (<c>--unattended</c>) is never asked: it says so as it starts
/// (<see cref="SDK.Platform.Protocol.RDCoreInitializationOptions.Unattended"/>), and a server that asks anyway is answered with an error, that nobody answers
/// (<see cref="ReplNobodyAnswersException"/>) - not with no choice, which is a user who closed the question. So is a server that asks a shell whose input is a
/// script, or one that has ended. Such a shell is not asked as it starts either: it says so (<see cref="SDK.Platform.Protocol.RDCoreInitializationOptions.AnswersDuringInitialize"/>),
/// since its language client would hold the error until the server has answered <c>initialize</c>, which the server would not do before it is answered.
/// <para>
/// The question is asked when the console is the shell's to ask it at (<see cref="ReplServerQuestions"/>): one that comes while a line is typed at the prompt
/// waits for the line to be entered, and the keys of the line are the line's.
/// </para>
/// </remarks>
/// <param name="interaction">The shell's dialogs.</param>
/// <param name="questions">When the shell asks the server's questions.</param>
/// <param name="unattended">Whether the shell runs unattended.</param>
internal sealed class ReplShowMessageRequestHandler(ConsoleUserInteraction interaction, ReplServerQuestions questions, ReplUnattended unattended)
    : OmniSharp.Extensions.LanguageServer.Protocol.Window.IShowMessageRequestHandler
{
    /// <inheritdoc/>
    public async Task<OmniSharp.Extensions.LanguageServer.Protocol.Models.MessageActionItem> Handle(
        OmniSharp.Extensions.LanguageServer.Protocol.Models.ShowMessageRequestParams request, CancellationToken cancellationToken)
    {
        if (unattended.IsUnattended)
        {
            throw new ReplNobodyAnswersException();
        }

        var actions = request.Actions?.ToArray() ?? [];
        if (actions.Length == 0)
        {
            return null!;
        }

        // the last button is the one the Enter key at first chooses: a server puts the one that changes nothing last. The console is read on a thread of its
        // own: the question waits for a person, and the connection does not.
        string[] titles = [.. actions.Select(action => action.Title)];
        var chosen = await Task.Run(
            () => questions.AskAsync((keys, token) => interaction.Choose("RDCore", request.Message, titles, actions.Length - 1, token, keys), cancellationToken),
            cancellationToken);
        return chosen is { } index ? actions[index] : null!;
    }
}

/// <summary>
/// Handles <c>window/showMessage</c> (<strong>LSP 3.17</strong>): what the language server tells the shell's user, such as why the workspace could not be
/// loaded, is written as one of the shell's messages, of the kind the message's type says.
/// </summary>
/// <remarks>
/// The shell's messages are written above the line being edited, and wait while a question is asked (<see cref="Console.PromptAwareMessageWriter"/>). One
/// that comes as the shell starts, while the server answers <c>initialize</c>, is written as it comes.
/// </remarks>
/// <param name="console">The shell's console.</param>
internal sealed class ReplShowMessageHandler(IReplConsole console) : OmniSharp.Extensions.LanguageServer.Protocol.Window.IShowMessageHandler
{
    /// <inheritdoc/>
    public Task<MediatR.Unit> Handle(OmniSharp.Extensions.LanguageServer.Protocol.Models.ShowMessageParams request, CancellationToken cancellationToken)
    {
        console.WriteMessage(KindOf(request.Type), request.Message);
        return MediatR.Unit.Task;
    }

    /// <summary>
    /// The kind of the shell's message a server's message is written as.
    /// </summary>
    internal static MessageKind KindOf(OmniSharp.Extensions.LanguageServer.Protocol.Models.MessageType type) => type switch
    {
        OmniSharp.Extensions.LanguageServer.Protocol.Models.MessageType.Error => MessageKind.Error,
        OmniSharp.Extensions.LanguageServer.Protocol.Models.MessageType.Warning => MessageKind.Warning,
        OmniSharp.Extensions.LanguageServer.Protocol.Models.MessageType.Log => MessageKind.Trace,
        _ => MessageKind.Information,
    };
}

/// <summary>
/// Whether the shell runs unattended (<c>--unattended</c>): nobody is at its console to answer a question.
/// </summary>
/// <param name="IsUnattended">Whether it does.</param>
internal sealed record ReplUnattended(bool IsUnattended);

internal static class ReplInteraction
{
    /// <summary>Writes the lines a program printed before it opened a dialog, as the shell writes what a program prints.</summary>
    public static void WriteOutput(IReplConsole console, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            console.WriteLine(line);
        }
    }
}
