using System.Globalization;
using System.Resources;
using System.Text.Json;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Services.Workspace;

// always an offer the user answers, never done unasked: what only the project file says (its references) is lost. Unattended, or with a client that does not
// answer during initialize, the project does not load.
// RD-VBAL §2.2.3
public static class ProjectFileRegeneration
{
    private static readonly ResourceManager Messages = new("RDCore.LanguageServer.Services.Workspace.ProjectFileMessages", typeof(ProjectFileRegeneration).Assembly);

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

    public static async Task<bool> AskAsync(
        string projectFilePath, string backupPath, Exception unreadable, string? text, Func<string, IReadOnlyList<string>, CancellationToken, Task<string?>> ask,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(unreadable);
        ArgumentNullException.ThrowIfNull(ask);

        var backup = Path.GetFileName(backupPath);
        var question = (unreadable as JsonException ?? unreadable.InnerException as JsonException)?.PlaceIn(text) is { } place
            ? Say("ProjectFile_Regenerate_Offer", projectFilePath, place.Line, place.Column, backup)
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

    internal static string Say(string key, params object[] arguments)
    {
        var culture = CultureInfo.CurrentUICulture;
        return string.Format(culture, Messages.GetString(key, culture) ?? key, arguments);
    }
}
