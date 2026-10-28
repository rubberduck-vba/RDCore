using RDCore.SDK.Platform.Protocol;
using System.IO.Abstractions;

namespace RDCore.SDK.Formatting;

/// <summary>
/// What an import of Rubberduck's indenter settings did.
/// </summary>
/// <param name="Source">Where the client read the settings; <see cref="RubberduckIndenterSettingsSource.None"/> when it found none.</param>
/// <param name="Found">How many <c>Formatting</c> settings the client's settings made.</param>
/// <param name="Applied">How many the client took as its own; <c>0</c> when it declined or there were none.</param>
/// <param name="Message">What to tell the user, in the language of the user interface.</param>
public sealed record class IndenterSettingsImportOutcome(RubberduckIndenterSettingsSource Source, int Found, int Applied, string Message);

/// <summary>
/// Imports the indenter settings of Rubberduck from a client that reads them: asks it what it found, maps that to <c>Formatting</c> settings
/// (<see cref="IndenterSettingsImport"/>), and asks it to take them as its own.
/// </summary>
/// <remarks>
/// A client owns its configuration, and the platform writes none of it: the client reads Rubberduck's settings
/// (<see cref="RubberduckIndenterSettingsParams"/>) and stores the mapped ones itself (<see cref="RubberduckIndenterSettingsApplyParams"/>). Only a client that expects
/// <see cref="Client.RubberduckIndenterSettings"/> is asked.
/// </remarks>
public static class IndenterSettingsImportFlow
{
    /// <summary>
    /// Asks the client what it found of Rubberduck's indenter settings.
    /// </summary>
    /// <param name="read">Sends <see cref="RubberduckIndenterSettingsParams"/> to the client.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The settings, mapped; <see langword="null"/> when the client found none.</returns>
    public static async Task<IndenterSettingsImport?> FindAsync(Func<CancellationToken, Task<RubberduckIndenterSettingsResult>> read, CancellationToken token)
        => IndenterSettingsImport.From(await read(token));

    /// <summary>
    /// Asks the client to take the settings as its own.
    /// </summary>
    /// <param name="import">The settings.</param>
    /// <param name="apply">Sends <see cref="RubberduckIndenterSettingsApplyParams"/> to the client.</param>
    /// <param name="token">A token that cancels the request.</param>
    public static async Task<IndenterSettingsImportOutcome> ApplyAsync(
        IndenterSettingsImport import, Func<RubberduckIndenterSettingsApplyParams, CancellationToken, Task<RubberduckIndenterSettingsApplyResult>> apply, CancellationToken token)
    {
        var answer = await apply(new RubberduckIndenterSettingsApplyParams { Source = import.Source, Settings = new Dictionary<string, string>(import.Values) }, token);
        var result = new IndenterSettingsImportResult(import.Source, answer.Applied, answer.Refused);
        return new IndenterSettingsImportOutcome(import.Source, import.Values.Count, answer.Applied,
            answer.Applied == 0 ? IndenterSettingsImportText.Declined(result) : IndenterSettingsImportText.Imported(result));
    }

    /// <summary>
    /// Imports the settings the client has, when it has any.
    /// </summary>
    /// <param name="read">Sends <see cref="RubberduckIndenterSettingsParams"/> to the client.</param>
    /// <param name="apply">Sends <see cref="RubberduckIndenterSettingsApplyParams"/> to the client.</param>
    /// <param name="token">A token that cancels the requests.</param>
    public static async Task<IndenterSettingsImportOutcome> ImportAsync(
        Func<CancellationToken, Task<RubberduckIndenterSettingsResult>> read,
        Func<RubberduckIndenterSettingsApplyParams, CancellationToken, Task<RubberduckIndenterSettingsApplyResult>> apply,
        CancellationToken token)
        => await FindAsync(read, token) is { } import
            ? await ApplyAsync(import, apply, token)
            : new IndenterSettingsImportOutcome(RubberduckIndenterSettingsSource.None, 0, 0, IndenterSettingsImportText.Nothing());
}

/// <summary>
/// The result of setting imported settings.
/// </summary>
/// <param name="Source">Where the settings were read.</param>
/// <param name="Imported">How many settings were set.</param>
/// <param name="Refused">The keys of the settings that could not be.</param>
public sealed record class IndenterSettingsImportResult(RubberduckIndenterSettingsSource Source, int Imported, IReadOnlyList<string> Refused);

/// <summary>
/// Offers, once, to import the indenter settings of Rubberduck: when the client has them, the user never set a <c>Formatting</c> setting, and the user was not offered
/// already.
/// </summary>
/// <remarks>
/// Rubberduck offered its user the settings of the legacy Smart Indenter the same way. The offer is made once, whatever the answer: a file in the folder of the platform's
/// user - it is no setting - remembers it was made, and <c>rdcore.importIndenterSettings</c> imports at any time.
/// </remarks>
/// <param name="fileSystem">The file system the file that remembers the offer is in.</param>
/// <param name="markerPath">The path of the file that remembers the offer was made (<see cref="MarkerIn"/>).</param>
public sealed class IndenterSettingsOffer(IFileSystem fileSystem, string markerPath)
{
    /// <summary>The name of the file that remembers the offer was made.</summary>
    public const string MarkerFileName = "indenter-import.offered";

    /// <summary>The path of the file that remembers the offer was made, in the folder of the platform's user.</summary>
    /// <param name="userDataDirectory">The folder (<see cref="Platform.IPlatformEnvironment.UserDataDirectory"/>).</param>
    public static string MarkerIn(string userDataDirectory) => Path.Combine(userDataDirectory, MarkerFileName);

    /// <summary>Whether the offer was made.</summary>
    public bool WasMade => fileSystem.File.Exists(markerPath);

    /// <summary>
    /// Makes the offer, if there is one to make, and imports the settings when the user says to.
    /// </summary>
    /// <param name="formattingWasSet">Whether the client said any <c>Formatting</c> setting: the user chose already.</param>
    /// <param name="read">Sends <see cref="RubberduckIndenterSettingsParams"/> to the client.</param>
    /// <param name="ask">Asks the user to choose among the buttons; <see langword="null"/> when they chose none.</param>
    /// <param name="apply">Sends <see cref="RubberduckIndenterSettingsApplyParams"/> to the client.</param>
    /// <param name="token">A token that cancels the requests.</param>
    /// <returns>What was imported; <see langword="null"/> when nothing was offered, or the user did not choose to import.</returns>
    public async Task<IndenterSettingsImportOutcome?> OfferAsync(
        bool formattingWasSet,
        Func<CancellationToken, Task<RubberduckIndenterSettingsResult>> read,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<string?>> ask,
        Func<RubberduckIndenterSettingsApplyParams, CancellationToken, Task<RubberduckIndenterSettingsApplyResult>> apply,
        CancellationToken token)
    {
        if (formattingWasSet || WasMade || await IndenterSettingsImportFlow.FindAsync(read, token) is not { } found)
        {
            return null;
        }

        // asked once: a user who closes the question is not asked again at the next start.
        if (fileSystem.Path.GetDirectoryName(markerPath) is { Length: > 0 } directory)
        {
            fileSystem.Directory.CreateDirectory(directory);
        }

        fileSystem.File.WriteAllText(markerPath, string.Empty);

        var import = IndenterSettingsImportText.Import();
        var chosen = await ask(IndenterSettingsImportText.Offer(found.Source), [import, IndenterSettingsImportText.NotNow()], token);
        return chosen == import ? await IndenterSettingsImportFlow.ApplyAsync(found, apply, token) : null;
    }
}
