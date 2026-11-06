using OmniSharp.Extensions.JsonRpc;
using RDCore.External.Interaction;
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
internal sealed class ReplMessageBoxHandler(ReplUserInteraction interaction, IReplConsole console) : IJsonRpcRequestHandler<SessionMessageBoxRequest, MessageBoxResult>
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
internal sealed class ReplInputBoxHandler(ReplUserInteraction interaction, IReplConsole console) : IJsonRpcRequestHandler<SessionInputBoxRequest, InputBoxResult>
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
/// A shell that runs unattended is never asked: it says so as it starts (<see cref="SDK.Platform.Protocol.RDCoreInitializationOptions.Unattended"/>).
/// </remarks>
/// <param name="interaction">The shell's dialogs.</param>
internal sealed class ReplShowMessageRequestHandler(ReplUserInteraction interaction) : OmniSharp.Extensions.LanguageServer.Protocol.Window.IShowMessageRequestHandler
{
    /// <inheritdoc/>
    public Task<OmniSharp.Extensions.LanguageServer.Protocol.Models.MessageActionItem> Handle(
        OmniSharp.Extensions.LanguageServer.Protocol.Models.ShowMessageRequestParams request, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            var actions = request.Actions?.ToArray() ?? [];
            if (actions.Length == 0)
            {
                return null!;
            }

            // the last button is the one an empty line, or the Enter key at first, chooses: a server puts the one that changes nothing last.
            var chosen = interaction.Choose("RDCore", request.Message, [.. actions.Select(action => action.Title)], actions.Length - 1, cancellationToken);
            return chosen is { } index ? actions[index] : null!;
        }, cancellationToken);
}

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
