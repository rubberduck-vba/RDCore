using RDCore.CLI.App.Console;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The questions the language server asks the shell's user (<strong>LSP 3.17</strong> <c>window/showMessageRequest</c>), each asked when the console is the
/// shell's to ask it at: one reader of its keys at a time.
/// </summary>
/// <remarks>
/// <para>
/// Until the shell reads its first line, a question is asked as it comes: nothing else reads the keys yet, and the server may be waiting for the answer to
/// answer <c>initialize</c>. From then on a question waits. It is asked before the next prompt is drawn, once the line typed at this one is entered and what
/// it runs is done (<see cref="AskWaiting"/>), or at once at a prompt where nothing is typed, which draws its line again after it
/// (<see cref="ConsolePromptEditor.Interjections"/>).
/// </para>
/// <para>
/// A shell whose input is a script asks nothing, nor does one that has ended (<see cref="Close"/>): the question is answered with no choice.
/// </para>
/// </remarks>
/// <param name="keys">The shell's keys.</param>
internal sealed class ReplServerQuestions(IConsoleKeys keys) : IConsoleInterjections
{
    private readonly object _sync = new();
    private readonly object _console = new();
    private readonly SemaphoreSlim _asking = new(1, 1);
    private readonly Queue<Question> _waiting = new();
    private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _held;
    private bool _closed;

    private sealed record Question(Func<IConsoleKeys, CancellationToken, int?> Ask, CancellationToken Token, TaskCompletionSource<int?> Answer);

    /// <summary>
    /// The prompt's line editor, whose drawing a question is serialized with: what is written above the line waits for the answer. <see langword="null"/> where
    /// the shell reads its lines without one.
    /// </summary>
    public ConsolePromptEditor? Editor { get; set; }

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
    /// <returns>What <paramref name="ask"/> answered; <see langword="null"/> when nobody is at the console.</returns>
    public async Task<int?> AskAsync(Func<IConsoleKeys, CancellationToken, int?> ask, CancellationToken token)
    {
        if (!keys.IsInteractive)
        {
            return null;
        }

        var question = new Question(ask, token, new(TaskCreationOptions.RunContinuationsAsynchronously));
        if (Waits(question))
        {
            using var withdrawn = token.Register(() => question.Answer.TrySetCanceled(token));
            return await question.Answer.Task;
        }

        // nothing reads the keys before the shell reads its first line; the shell waits for the question to be answered before it does. One that started
        // to read them in the meantime has the question wait for it.
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

    // whether the question waits to be asked, which it is put in line for; a shell that has ended answers it with no choice.
    private bool Waits(Question question)
    {
        lock (_sync)
        {
            if (_closed)
            {
                question.Answer.TrySetResult(null);
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
    /// <param name="keys">Where the answers are read: the shell's, or the prompt's, which hand over the key it was waiting for.</param>
    public void AskWaiting(IConsoleKeys keys)
    {
        lock (_sync)
        {
            _held = true;
        }

        // a question asked before the shell read its first line is answered first.
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
    /// The shell has ended: what waits is answered with no choice, and nothing more is asked.
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
            question.Answer.TrySetResult(null);
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

    // a question that was withdrawn is not asked; one is asked holding what the console's drawing is serialized on, so that nothing is written into it.
    private void Ask(Question question, IConsoleKeys keys)
    {
        if (question.Answer.Task.IsCompleted || question.Token.IsCancellationRequested)
        {
            question.Answer.TrySetCanceled(question.Token);
            return;
        }

        try
        {
            lock (Editor?.Sync ?? _console)
            {
                question.Answer.TrySetResult(question.Ask(keys, question.Token));
            }
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
