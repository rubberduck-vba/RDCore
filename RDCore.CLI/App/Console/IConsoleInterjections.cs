using RDCore.External.Interaction;

namespace RDCore.CLI.App.Console;

/// <summary>
/// What waits to be asked at the console while a line is read at the prompt (<see cref="ConsolePromptEditor.Interjections"/>): a question the language server
/// asks the shell's user, which the keys of the line must not answer.
/// </summary>
/// <remarks>
/// The prompt asks it at once while nothing is typed on the line, and draws the line again after it; once something is, it is asked after the line is entered,
/// by whatever reads the next one.
/// </remarks>
public interface IConsoleInterjections
{
    /// <summary>
    /// Done once something waits to be asked; a task that is done at once while something does.
    /// </summary>
    Task Waiting { get; }

    /// <summary>
    /// Asks what waits to be asked, in the order it came, with the keys given: the prompt's, which it stopped reading to have it asked.
    /// </summary>
    /// <param name="keys">Where the answers are read.</param>
    void AskWaiting(IConsoleKeys keys);
}
