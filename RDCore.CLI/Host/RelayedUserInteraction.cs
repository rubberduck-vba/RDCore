using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RDCore.External.Interaction;
using RDCore.SDK;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.CLI.Host;

/// <summary>
/// Where the environment host asks the person running a program to answer its dialogs: the language server, which asks its client.
/// </summary>
/// <remarks>
/// Set by whoever owns the connection to the language server, as <see cref="IEnvironmentSessionProvider.OutputStreamed"/> is; a host with no connection asks nobody,
/// and shows the dialogs of the platform it runs on.
/// </remarks>
public interface IHostInteractionChannel
{
    /// <summary>Asks for a <c>MsgBox</c> to be answered (<c>rdcore/host/interaction/msgBox</c>).</summary>
    Task<MessageBoxResult> MessageBoxAsync(HostMessageBoxParams request, CancellationToken token);

    /// <summary>Asks for an <c>InputBox</c> to be answered (<c>rdcore/host/interaction/inputBox</c>).</summary>
    Task<InputBoxResult> InputBoxAsync(HostInputBoxParams request, CancellationToken token);
}

/// <summary>
/// The dialogs of a program that runs in the environment host: answered by the client of the language server when it shows dialogs - the interactive shell asks at
/// its console - and by the platform the host runs on otherwise.
/// </summary>
/// <remarks>
/// <para>
/// The language server answers that nobody was asked when its client did not say it shows dialogs (<see cref="SessionInteraction"/>), and then the platform's own are
/// shown: Windows', or none.
/// </para>
/// <para>
/// A client that does show them is the one that answers, and the platform's dialog is never shown after it: a host that a language server started is seen by
/// nobody, and a dialog of its own is a run that waits forever. A dialog the client declined fails the run with <c>VBR50001</c>, since nobody else will answer it;
/// an answer that cannot be used - none of the buttons of a <c>MsgBox</c> - and a dialog that could not be relayed at all fail it with <c>VBR50002</c>. Both are
/// untrappable, and logged.
/// </para>
/// <para>
/// A program prints before it asks, and what it printed is in the answer to the request that runs it, which comes when it is over. The lines it completed are sent
/// with the dialog instead, so that a client shows them first; once the dialog has been answered they are no longer the run's to answer with.
/// </para>
/// </remarks>
/// <param name="provider">The provider of the session: its connection to the language server, and the output of the run that opens the dialog.</param>
/// <param name="fallback">The dialogs of the platform the host runs on.</param>
/// <param name="logger">Where what fails a run is told: the host's file.</param>
internal sealed class RelayedUserInteraction(IEnvironmentSessionProvider provider, IUserInteraction fallback, ILogger? logger = null) : IUserInteraction
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <inheritdoc/>
    public VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default)
    {
        if (provider.Interaction is not { } client)
        {
            return fallback.ShowMessage(request, token);
        }

        var (buffer, output) = Pending();
        var answer = Ask("MsgBox", () => client.MessageBoxAsync(
            new HostMessageBoxParams { Prompt = request.Prompt, Title = request.Title, Style = (int)request.Style, Output = output }, token), token);

        if (answer.Failure is { } failure)
        {
            throw Unanswered(VBRuntimeErrorId.UnusableDialogAnswer, failure);
        }

        if (answer.Declined)
        {
            throw Unanswered(VBRuntimeErrorId.NobodyToAnswer, string.Format(Exceptions.VBRuntimeError_NobodyToAnswer_Declined, "MsgBox", Exceptions.UnnamedClient));
        }

        if (!answer.Handled)
        {
            return fallback.ShowMessage(request, token);
        }

        if (!request.Choices.Contains((VBMsgBoxResult)answer.Result))
        {
            throw Unanswered(VBRuntimeErrorId.UnusableDialogAnswer, string.Format(
                Exceptions.VBRuntimeError_UnusableDialogAnswer_NoSuchButton, Exceptions.UnnamedClient, VBMsgBoxButtons.NameOf(request.Style), answer.Result));
        }

        buffer?.ForgetCompleted(output.Count);
        return (VBMsgBoxResult)answer.Result;
    }

    /// <inheritdoc/>
    public string? Prompt(InputBoxRequest request, CancellationToken token = default)
    {
        if (provider.Interaction is not { } client)
        {
            return fallback.Prompt(request, token);
        }

        var (buffer, output) = Pending();
        var answer = Ask("InputBox", () => client.InputBoxAsync(
            new HostInputBoxParams
            {
                Prompt = request.Prompt, Title = request.Title, Default = request.Default, XPos = request.XPos, YPos = request.YPos, Output = output,
            }, token), token);

        if (answer.Declined)
        {
            throw Unanswered(VBRuntimeErrorId.NobodyToAnswer, string.Format(Exceptions.VBRuntimeError_NobodyToAnswer_Declined, "InputBox", Exceptions.UnnamedClient));
        }

        if (!answer.Handled)
        {
            return fallback.Prompt(request, token);
        }

        buffer?.ForgetCompleted(output.Count);
        return answer.Cancelled ? null : answer.Value;
    }

    // the lines the run completed and still holds: those of a run that streams its output have been said already.
    private (RuntimeOutputBuffer? Buffer, IReadOnlyList<string> Lines) Pending()
        => provider.Output.Target is RuntimeOutputBuffer buffer ? (buffer, buffer.CompletedLines) : (null, []);

    // the program waits for the dialog as it would for a window, on its own thread. A dialog that could not be relayed fails the run: the platform's would be shown
    // by a host nobody sees.
    private TResult Ask<TResult>(string dialog, Func<Task<TResult>> ask, CancellationToken token) where TResult : class
    {
        try
        {
            return ask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogError(exception, "💬 The program's {Dialog} could not be relayed to the language server's client.", dialog);
            throw new DialogNotAnsweredException(VBRuntimeErrorId.UnusableDialogAnswer,
                string.Format(Exceptions.VBRuntimeError_UnusableDialogAnswer_NotRelayed, dialog, exception.Message));
        }
    }

    private DialogNotAnsweredException Unanswered(VBRuntimeErrorId error, string message)
    {
        _logger.LogWarning("💬 {Message}", message);
        return new DialogNotAnsweredException(error, message);
    }
}
