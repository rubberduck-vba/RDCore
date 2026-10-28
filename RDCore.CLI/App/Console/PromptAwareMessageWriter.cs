using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Console;

/// <summary>
/// Writes the shell's messages above the line being edited at the prompt (<see cref="ConsolePromptEditor.WriteAbove"/>), and as they come when none is: a
/// message logged while the workspace loads doesn't land in what is typed, nor leave the prompt's progress bar behind on the console.
/// </summary>
/// <remarks>
/// The editor attaches itself as it is made (<see cref="Editor"/>), rather than being given: the shell's logger writes through this, and the editor's completion
/// needs the platform client, which logs.
/// </remarks>
/// <param name="inner">What writes a message.</param>
internal sealed class PromptAwareMessageWriter(IConsoleMessageWriter inner) : IConsoleMessageWriter
{
    /// <summary>The editor of the prompt's line, once there is one: a message is written as it comes until then.</summary>
    public ConsolePromptEditor? Editor { get; set; }

    private void Write(Action write)
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
        inner.Clear();
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

    public IConsoleMessageWriter WriteSlogan()
    {
        Write(() => inner.WriteSlogan());
        return this;
    }

    public IConsoleMessageWriter WriteLegalNotice()
    {
        Write(() => inner.WriteLegalNotice());
        return this;
    }
}
