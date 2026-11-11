using RDCore.External.Interaction;
using RDCore.CLI.App.Console;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The questions the language server asks the shell's user (<strong>LSP 3.17</strong> <c>window/showMessageRequest</c>), each asked when the console is the
/// shell's to ask it at: one reader of its keys at a time.
/// </summary>
/// <remarks>
/// <para>
/// Until the language server has answered <c>initialize</c>, a question is asked as it comes: nothing else reads the keys yet, and the server waits for the
/// answer to answer <c>initialize</c>. From then on a question waits (<see cref="Hold"/>), while the shell comes up as well: it is asked once the shell says
/// it is ready, before its first prompt is drawn. After that, it is asked before the next prompt is drawn, once the line typed at this one is entered and
/// what it runs is done (<see cref="AskWaiting"/>), or at once at a prompt where nothing is typed, which draws its line again after it
/// (<see cref="ConsolePromptEditor.Interjections"/>).
/// </para>
/// <para>
/// The shell's messages that come while a question is asked are written once it is answered (<see cref="PromptAwareMessageWriter.WhileAsking"/>), and what
/// writes them does not wait for the user.
/// </para>
/// <para>
/// A shell whose input is a script asks nothing, nor does one that has ended (<see cref="Close"/>): nobody answers (<see cref="ReplNobodyAnswersException"/>),
/// which is not the user closing the question.
/// </para>
/// </remarks>
/// <param name="keys">The shell's keys.</param>
/// <param name="messages">The shell's messages, which wait while a question is asked; <see langword="null"/> where they are not written above a prompt's line.</param>
internal sealed class ReplServerQuestions(IConsoleKeys keys, PromptAwareMessageWriter? messages = null) : IConsoleInterjections
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _asking = new(1, 1);
    private readonly Queue<Question> _waiting = new();
    private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _held;
    private bool _closed;

    private sealed record Question(Func<IConsoleKeys, CancellationToken, int?> Ask, CancellationToken Token, TaskCompletionSource<int?> Answer);

    /// <summary>
    /// Whether a question is answered at all: nobody answers one at a console whose input is a script (<see cref="ReplNobodyAnswersException"/>).
    /// </summary>
    public bool AreAnswered => keys.IsInteractive;

    /// <inheritdoc/>
    public Task Waiting
    {
        get
        {
            lock (_sync)
            {
                return _waiting.Count > 0 ? Task.CompletedTask : _arrived.Task;
            }
        }
    }

    /// <summary>
    /// Asks a question when the console is the shell's to ask it at.
    /// </summary>
    /// <param name="ask">Asks it with the keys given, and answers the index of the choice, or <see langword="null"/> for none.</param>
    /// <param name="token">A token that withdraws the question.</param>
    /// <returns>What <paramref name="ask"/> answered.</returns>
    /// <exception cref="ReplNobodyAnswersException">Nobody is at the console: its input is a script, or the shell has ended.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="token"/> withdrew the question before it was answered.</exception>
    public async Task<int?> AskAsync(Func<IConsoleKeys, CancellationToken, int?> ask, CancellationToken token)
    {
        if (!keys.IsInteractive)
        {
            throw new ReplNobodyAnswersException();
        }

        var question = new Question(ask, token, new(TaskCreationOptions.RunContinuationsAsynchronously));
        if (Waits(question))
        {
            using var withdrawn = token.Register(() => question.Answer.TrySetCanceled(token));
            return await question.Answer.Task;
        }

        // nothing reads the keys before initialize is answered; the shell waits for the question to be answered before it reads them. One that started to
        // read them in the meantime has the question wait for it.
        await _asking.WaitAsync(token);
        try
        {
            if (!Waits(question))
            {
                Ask(question, keys);
            }
        }
        finally
        {
            _asking.Release();
        }

        using var cancelled = token.Register(() => question.Answer.TrySetCanceled(token));
        return await question.Answer.Task;
    }

    // whether the question waits to be asked, which it is put in line for; nobody answers it in a shell that has ended.
    private bool Waits(Question question)
    {
        lock (_sync)
        {
            if (_closed)
            {
                question.Answer.TrySetException(new ReplNobodyAnswersException());
                return true;
            }

            if (!_held)
            {
                return false;
            }

            _waiting.Enqueue(question);
            _arrived.TrySetResult();
            return true;
        }
    }

    /// <summary>
    /// Asks the questions that wait, in the order they came, with the keys given; from the first call on, a question waits to be asked. Called before the shell
    /// draws a prompt, and by the prompt while nothing is typed on its line.
    /// </summary>
    /// <param name="keys">Where the answers are read: the shell's, or the prompt's, which stopped reading its key to have them asked.</param>
    public void AskWaiting(IConsoleKeys keys)
    {
        lock (_sync)
        {
            _held = true;
        }

        // a question asked before initialize was answered is answered first.
        _asking.Wait();
        try
        {
            while (Next() is { } question)
            {
                Ask(question, keys);
            }
        }
        finally
        {
            _asking.Release();
        }
    }

    /// <summary>
    /// The language server has answered <c>initialize</c>: from now on a question waits to be asked (<see cref="AskWaiting"/>), and none is asked while the
    /// shell comes up.
    /// </summary>
    public void Hold()
    {
        lock (_sync)
        {
            _held = true;
        }
    }

    /// <summary>
    /// The shell has ended: nobody answers what waits, and nothing more is asked.
    /// </summary>
    public void Close()
    {
        Question[] left;
        lock (_sync)
        {
            _closed = true;
            left = [.. _waiting];
            _waiting.Clear();
        }

        foreach (var question in left)
        {
            question.Answer.TrySetException(new ReplNobodyAnswersException());
        }
    }

    private Question? Next()
    {
        lock (_sync)
        {
            if (_waiting.TryDequeue(out var question))
            {
                return question;
            }

            if (_arrived.Task.IsCompleted)
            {
                _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return null;
        }
    }

    // a question that was withdrawn is not asked; the shell's messages that come while one is asked wait for its answer, so that none is written into it.
    private void Ask(Question question, IConsoleKeys keys)
    {
        if (question.Answer.Task.IsCompleted || question.Token.IsCancellationRequested)
        {
            question.Answer.TrySetCanceled(question.Token);
            return;
        }

        try
        {
            var answer = messages is null ? question.Ask(keys, question.Token) : messages.WhileAsking(() => question.Ask(keys, question.Token));
            question.Answer.TrySetResult(answer);
        }
        catch (OperationCanceledException)
        {
            question.Answer.TrySetCanceled(question.Token);
        }
        catch (Exception exception)
        {
            question.Answer.TrySetException(exception);
        }
    }
}

/// <summary>
/// Nobody is at the shell's console to answer a question the language server asks: its input is a script, it runs unattended, or it has ended. The server is
/// answered with an error, not with no choice, which is a user who closed the question: an offer made once is still to make (<see cref="SDK.Formatting.IndenterSettingsOffer"/>).
/// </summary>
internal sealed class ReplNobodyAnswersException() : Exception(Resources.Repl_NobodyAnswers);
