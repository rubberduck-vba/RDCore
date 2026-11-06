using System.Globalization;
using System.Resources;
using System.Text.Json;

namespace RDCore.SDK.Workspace;

/// <summary>
/// The offer to regenerate a project file the platform cannot read (<strong>RD-VBAL §2.2.3</strong>): the file is kept next to itself, and the project is
/// made again from the source files of its folder, as the platform makes up the project of a folder that has no project file
/// (<see cref="IProjectFileLoader.LoadOrSynthesizeAsync"/>).
/// </summary>
/// <remarks>
/// The offer is a question the user answers, never something done without asking: what only the project file says, such as the project's references, is lost.
/// A server asks nothing of a client that runs unattended (<see cref="Platform.Protocol.RDCoreInitializationOptions.Unattended"/>), and then the project does not
/// load.
/// </remarks>
public static class ProjectFileRegeneration
{
    private static readonly ResourceManager Messages = new("RDCore.SDK.Workspace.ProjectFileMessages", typeof(ProjectFileRegeneration).Assembly);

    /// <summary>
    /// Where a project file is kept before it is regenerated: next to it, as <c>.rdproj.bak</c>, or, when a file is there already, as the first of
    /// <c>.rdproj.1.bak</c>, <c>.rdproj.2.bak</c>, and so on, that no file is at.
    /// </summary>
    /// <param name="projectFilePath">The project file.</param>
    /// <param name="exists">Whether a file is at a path.</param>
    public static string BackupPathOf(string projectFilePath, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentNullException.ThrowIfNull(exists);

        var backup = projectFilePath + ".bak";
        for (var copy = 1; exists(backup); copy++)
        {
            backup = $"{projectFilePath}.{copy.ToString(CultureInfo.InvariantCulture)}.bak";
        }

        return backup;
    }

    /// <summary>
    /// Asks the user whether to regenerate the project file.
    /// </summary>
    /// <param name="projectFilePath">The project file.</param>
    /// <param name="backupPath">Where it is kept if it is regenerated (<see cref="BackupPathOf"/>).</param>
    /// <param name="unreadable">
    /// Why it cannot be read, as its reader said (<see cref="IProjectFileLoader.LoadAsync"/>). The question says only where the file stops being valid JSON, in
    /// the lines and positions an editor counts from 1, when the reader's error says it: the rest of what the reader said is in the reader's own language.
    /// </param>
    /// <param name="text">
    /// The file's text, or <see langword="null"/> when it could not be read: the position is counted in its characters, as an editor counts them, where the
    /// reader counts the bytes of its UTF-8. Without it, the position is the reader's, which is the same only on a line of ASCII.
    /// </param>
    /// <param name="ask">Asks the user to choose among the buttons; <see langword="null"/> when they chose none.</param>
    /// <param name="token">A token that cancels the question.</param>
    /// <returns><see langword="true"/> when the user chose to regenerate it; <see langword="false"/> when they did not, or when the client could not ask.</returns>
    public static async Task<bool> AskAsync(
        string projectFilePath, string backupPath, Exception unreadable, string? text, Func<string, IReadOnlyList<string>, CancellationToken, Task<string?>> ask,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(unreadable);
        ArgumentNullException.ThrowIfNull(ask);

        var backup = Path.GetFileName(backupPath);
        var question = (unreadable as JsonException ?? unreadable.InnerException as JsonException) is { LineNumber: { } line } json
            ? Say("ProjectFile_Regenerate_Offer", projectFilePath, line + 1, ColumnOf(text, line, json.BytePositionInLine ?? 0) + 1, backup)
            : Say("ProjectFile_Regenerate_Offer_Unplaced", projectFilePath, backup);
        var regenerate = Say("ProjectFile_Regenerate_Choice_Regenerate");
        string? chosen;
        try
        {
            // the choice that changes nothing is last, where a client that has a default puts it.
            chosen = await ask(question, [regenerate, Say("ProjectFile_Regenerate_Choice_NotNow")], token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // a client that answers the question with an error, or does not know it, chose nothing: nothing is regenerated.
            return false;
        }

        return chosen == regenerate;
    }

    // the characters before a position the JSON reader gives in UTF-8 bytes from the start of a line it counts from 0, lines ending at '\n' as it ends them.
    private static long ColumnOf(string? text, long line, long bytes)
    {
        if (text is null)
        {
            return bytes;
        }

        var start = 0;
        for (var at = 0L; at < line; at++)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
            {
                return bytes;
            }

            start = end + 1;
        }

        var stop = text.IndexOf('\n', start);
        var counted = 0L;
        var characters = 0L;
        foreach (var rune in text.AsSpan(start, (stop < 0 ? text.Length : stop) - start).EnumerateRunes())
        {
            if (counted >= bytes)
            {
                break;
            }

            counted += rune.Utf8SequenceLength;
            characters += rune.Utf16SequenceLength;
        }

        return characters;
    }

    private static string Say(string key, params object[] arguments)
    {
        var culture = CultureInfo.CurrentUICulture;
        return string.Format(culture, Messages.GetString(key, culture) ?? key, arguments);
    }
}
