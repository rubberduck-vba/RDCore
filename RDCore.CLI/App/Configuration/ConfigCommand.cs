using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Server.Configuration;
using System.Globalization;

namespace RDCore.CLI.App.Configuration;

/// <summary>
/// A line of what <c>CONFIG</c> says: a line of text, or a message of a kind.
/// </summary>
/// <param name="Text">The text.</param>
/// <param name="Kind">The kind of message it is; <see langword="null"/> for a line of text.</param>
/// <param name="Detail">More of the message, for a message that has more to say.</param>
internal sealed record class ConfigOutput(string Text, MessageKind? Kind = null, string? Detail = null);

/// <summary>
/// <c>CONFIG</c>: shows the settings of the platform, and sets them for the user - what the shell's verb and <c>rdc config</c> both do.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>CONFIG</c> shows what the user set.</item>
/// <item><c>CONFIG Server.Verbose</c> shows a setting - what it is, what it can be, and what the user set it to; <c>CONFIG Server</c> shows the settings of a section.</item>
/// <item><c>CONFIG Server.Verbose False</c> sets it, for the user: a setting that decides what a program may do to the machine says so, with a ⚠️ callout.</item>
/// <item><c>CONFIG RESET Server.Verbose</c> removes what the user set of a setting, or of a section; <c>CONFIG RESET</c> of every setting.</item>
/// </list>
/// What the user sets is kept in their settings (<see cref="UserSettingsStore"/>), apart from what the platform was deployed with, which takes its place again when
/// it is reset. A setting is read when the component it is of starts, so a value takes effect the next time the platform does.
/// </remarks>
/// <param name="settings">The settings.</param>
internal sealed class ConfigCommand(IPlatformSettings settings)
{
    /// <summary>The verb.</summary>
    public const string Name = Repl.Commands.ReplCommandNames.Config;

    /// <summary>The other spelling of the verb.</summary>
    public const string Alias = Repl.Commands.ReplCommandNames.Configure;

    private const string ResetArgument = "RESET";

    /// <summary>
    /// The forms of the verb's arguments, the settings a user can set among them: <c>CONFIG RESET [setting]</c> and <c>CONFIG &lt;setting&gt; [value]</c>.
    /// </summary>
    /// <param name="themes">The names of the themes of the console, which <c>CLI.Theme</c> can be: offered as its values, though any name is kept.</param>
    public VerbDescriptor Describe(IEnumerable<string> themes)
    {
        var themeNames = themes.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        IReadOnlyList<SettingDescriptor> settable = [.. settings.Schema.Settings
            .Where(setting => setting.IsSettable)
            .Select(setting => setting.Key.Equals($"{AppOptions.Section}.{nameof(AppOptions.Theme)}", StringComparison.OrdinalIgnoreCase)
                ? setting with { Choices = themeNames }
                : setting)];

        return new VerbDescriptor
        {
            Name = Name,
            Aliases = [Alias],
            Summary = Resources.Config_Summary,
            Forms =
            [
                [
                    new VerbArgument { Name = "reset", Kind = VerbArgumentKind.Keyword, Values = [new() { Value = ResetArgument, Description = Resources.Config_Reset_Detail }] },
                    new VerbArgument { Name = "setting", Kind = VerbArgumentKind.Setting, Settings = settable },
                ],
                [
                    new VerbArgument { Name = "setting", Kind = VerbArgumentKind.Setting, Settings = settable },
                    new VerbArgument { Name = "value", Kind = VerbArgumentKind.SettingValue, ValueOf = "setting" },
                ],
            ],
        };
    }

    /// <summary>
    /// Runs the verb.
    /// </summary>
    /// <param name="arguments">What is written after the verb.</param>
    /// <returns>What it says.</returns>
    public IReadOnlyList<ConfigOutput> Run(string arguments)
    {
        var trimmed = arguments.Trim();
        var separator = trimmed.IndexOfAny([' ', '\t']);
        var key = separator < 0 ? trimmed : trimmed[..separator];
        var value = separator < 0 ? string.Empty : trimmed[(separator + 1)..].Trim();

        if (key.Length == 0)
        {
            return ListSet();
        }

        if (key.Equals(ResetArgument, StringComparison.OrdinalIgnoreCase))
        {
            return Reset(value.Length == 0 ? null : value);
        }

        return value.Length == 0 ? Show(key) : Set(key, value);
    }

    private IReadOnlyList<ConfigOutput> ListSet()
    {
        var store = settings.Store;
        if (!store.IsReadable)
        {
            return [Unreadable(store)];
        }

        var set = store.Read();
        return set.Count == 0
            ? [new(Resources.Config_NothingSet, MessageKind.Information)]
            : [.. set.Select(value => new ConfigOutput($"  {value.Setting.Key} = {value.Value}"))];
    }

    private IReadOnlyList<ConfigOutput> Show(string key)
    {
        var store = settings.Store;
        var set = store.Read();
        if (settings.Schema.TryFind(key, out var setting))
        {
            var lines = new List<ConfigOutput>
            {
                new(set.FirstOrDefault(value => IsSame(value.Setting, setting)) is { } own
                    ? $"{setting.Key} = {own.Value}"
                    : string.Format(Resources.Config_NotSet, setting.Key)),
            };

            if (setting.Description is { Length: > 0 } description)
            {
                lines.Add(new($"  {description}"));
            }

            if (!setting.IsSettable)
            {
                lines.Add(new(string.Format(Resources.Config_Deployment, setting.Key), MessageKind.Warning));
                return lines;
            }

            lines.Add(new($"  {ValuesOf(setting)}"));
            if (setting.IsTrustSensitive)
            {
                lines.Add(TrustSensitive(setting));
            }

            return lines;
        }

        // a section: its settings, and what the user set them to.
        var section = settings.Schema.Settings.Where(candidate => candidate.IsSettable && candidate.Key.StartsWith(key + ".", StringComparison.OrdinalIgnoreCase)).ToList();
        if (section.Count == 0)
        {
            return [new(string.Format(Resources.Config_Unknown, key), MessageKind.Error)];
        }

        return [.. section.Select(candidate => set.FirstOrDefault(value => IsSame(value.Setting, candidate)) is { } own
            ? new ConfigOutput($"  {candidate.Key} = {own.Value}")
            : new ConfigOutput($"  {candidate.Key}"))];
    }

    private IReadOnlyList<ConfigOutput> Set(string key, string value)
    {
        var store = settings.Store;
        var change = store.Set(key, value);
        switch (change.Problem)
        {
            case UserSettingProblem.UnknownSetting:
                return [new(string.Format(Resources.Config_Unknown, key), MessageKind.Error)];

            case UserSettingProblem.NotSettable:
                return [new(string.Format(Resources.Config_Deployment, change.Setting!.Key), MessageKind.Error)];

            case UserSettingProblem.InvalidValue:
                return [new(string.Format(Resources.Config_Invalid, value, change.Setting!.Key), MessageKind.Error, ValuesOf(change.Setting))];

            case UserSettingProblem.Unreadable:
                return [Unreadable(store)];
        }

        var setting = change.Setting!;
        var written = store.Read().FirstOrDefault(own => IsSame(own.Setting, setting))?.Value ?? value;
        var lines = new List<ConfigOutput> { new(string.Format(Resources.Config_Set, setting.Key, written), MessageKind.Success) };
        if (setting.IsTrustSensitive)
        {
            lines.Add(TrustSensitive(setting));
        }

        return lines;
    }

    private IReadOnlyList<ConfigOutput> Reset(string? key)
    {
        var store = settings.Store;
        if (key is not null && !store.IsReadable)
        {
            return [Unreadable(store)];
        }

        var count = store.Reset(key);
        return count == 0
            ? [new(Resources.Config_NothingToReset, MessageKind.Information)]
            : [new(string.Format(Resources.Config_Reset, count), MessageKind.Success)];
    }

    private static bool IsSame(SettingDescriptor x, SettingDescriptor y) => string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase);

    private static ConfigOutput Unreadable(UserSettingsStore store) => new(string.Format(Resources.Config_Unreadable, store.FilePath), MessageKind.Error);

    // ⚠️ what changing it means: a program may do more to the machine, or less.
    private static ConfigOutput TrustSensitive(SettingDescriptor setting)
        => new(string.Format(Resources.Config_TrustSensitive, setting.Key), MessageKind.Warning, setting.Description);

    // what a setting can be, in a sentence.
    private static string ValuesOf(SettingDescriptor setting) => setting.Kind switch
    {
        SettingValueKind.Boolean => Resources.Config_Values_Boolean,
        SettingValueKind.Integer => string.Format(CultureInfo.InvariantCulture, Resources.Config_Values_Integer, setting.Minimum, setting.Maximum),
        SettingValueKind.Choice => string.Format(Resources.Config_Values_Choice, string.Join(", ", setting.Choices)),
        SettingValueKind.TextList => Resources.Config_Values_TextList,
        _ => Resources.Config_Values_Text,
    };
}
