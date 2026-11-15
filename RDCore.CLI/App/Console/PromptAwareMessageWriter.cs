using RDCore.CLI.ConsoleIO;
using RDCore.CLI.ConsoleIO.Model;

namespace RDCore.CLI.App.Console;

/// <summary>
/// Writes the shell's messages above the line being edited at the prompt (<see cref="ConsolePromptEditor.WriteAbove"/>), and as they come when none is: a
/// message logged while the workspace loads doesn't land in what is typed, nor leave the prompt's progress bar behind on the console.
/// </summary>
/// <remarks>
/// <para>
/// The editor attaches itself as it is made (<see cref="Editor"/>), rather than being given: the shell's logger writes through this, and the editor's completion
/// needs the platform client, which logs.
/// </para>
/// <para>
/// A message that comes while a question is asked at the console waits for it to be answered (<see cref="WhileAsking"/>): it is not written into the question,
/// and what writes it does not wait for the user.
/// </para>
/// </remarks>
/// <param name="inner">What writes a message.</param>
internal sealed class PromptAwareMessageWriter(IConsoleMessageWriter inner) : IConsoleMessageWriter
{
    private readonly object _sync = new();

    // the messages that came while a question is asked, in the order they came; null while none is.
    private Queue<Action>? _held;

    /// <summary>The editor of the prompt's line, once there is one: a message is written as it comes until then.</summary>
    public ConsolePromptEditor? Editor { get; set; }

    /// <summary>
    /// Asks a question at the console: the messages that come while it is asked are written once it is answered, in the order they came.
    /// </summary>
    /// <remarks>
    /// A message held that fails to be written is lost, and the messages after it are written: what <paramref name="ask"/> answered, or threw, is never replaced
    /// by it.
    /// </remarks>
    /// <typeparam name="T">What the question answers.</typeparam>
    /// <param name="ask">Asks it.</param>
    /// <returns>What <paramref name="ask"/> answered.</returns>
    public T WhileAsking<T>(Func<T> ask)
    {
        lock (_sync)
        {
            _held = new();
        }

        try
        {
            return ask();
        }
        finally
        {
            // a message that comes while those held are written is written after them.
            while (true)
            {
                Action[] held;
                lock (_sync)
                {
                    if (_held is not { Count: > 0 } waiting)
                    {
                        _held = null;
                        break;
                    }

                    held = [.. waiting];
                    waiting.Clear();
                }

                foreach (var write in held)
                {
                    WriteHeld(write);
                }
            }
        }
    }

    // a held message that fails to be written is lost: what wrote it has returned, and what is thrown here would take the place of the question's answer.
    private void WriteHeld(Action write)
    {
        try
        {
            WriteNow(write);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Writes something at the console as a message is written: above the prompt's line, and once the question is answered while one is asked.
    /// </summary>
    /// <param name="write">Writes it.</param>
    public void Write(Action write)
    {
        lock (_sync)
        {
            if (_held is { } held)
            {
                held.Enqueue(write);
                return;
            }
        }

        WriteNow(write);
    }

    private void WriteNow(Action write)
    {
        if (Editor is { } editor)
        {
            editor.WriteAbove(write);
        }
        else
        {
            write();
        }
    }

    public IConsoleMessageWriter Clear()
    {
        // the console is cleared with the line on it: the line is drawn again at its top.
        Write(() => inner.Clear());
        return this;
    }

    public IConsoleMessageWriter WriteException(Exception exception)
    {
        Write(() => inner.WriteException(exception));
        return this;
    }

    public IConsoleMessageWriter WriteMessage(ConsoleMessageBuilder builder)
    {
        Write(() => inner.WriteMessage(builder));
        return this;
    }

    public IConsoleMessageWriter WriteAssemblyInfo()
    {
        Write(() => inner.WriteAssemblyInfo());
        return this;
    }

    public IConsoleMessageWriter WriteSlogan(int indent = 0)
    {
        Write(() => inner.WriteSlogan(indent));
        return this;
    }

    public IConsoleMessageWriter WriteLegalNotice()
    {
        Write(() => inner.WriteLegalNotice());
        return this;
    }
}
