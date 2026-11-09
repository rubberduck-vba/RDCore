using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer.Services.Completion;

public sealed record class VerbCompletionSite(VerbArgument Argument, string Typed, IReadOnlyDictionary<string, string> Written)
{
    public string? EvaluatedAt => Argument.EvaluatedAt is { } name && Written.TryGetValue(name, out var line) ? line : null;

    public IReadOnlyList<VerbArgument> Form { get; init; } = [];

    public SettingDescriptor? Setting
        => Argument.ValueOf is { } name && Written.TryGetValue(name, out var key)
            && Form.FirstOrDefault(argument => argument.Name == name) is { Kind: VerbArgumentKind.Setting } owner
                ? owner.Settings.FirstOrDefault(setting => string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase))
                : null;
}

public static class VerbCompletion
{
    public static IReadOnlyList<VerbCompletionSite> Read(VerbDescriptor verb, string arguments)
    {
        var words = Words(arguments);
        (string Text, int Start)? typing = arguments.Length == 0 || char.IsWhiteSpace(arguments[^1]) ? null : words[^1];
        var complete = typing is null ? words : words.GetRange(0, words.Count - 1);

        var sites = new List<VerbCompletionSite>();
        foreach (var form in verb.Forms)
        {
            if (SiteIn(form, complete, typing, arguments) is { } site && !sites.Any(other => other.Argument == site.Argument && other.Typed == site.Typed))
            {
                sites.Add(site);
            }
        }

        return sites;
    }

    // the argument of the form that is being written: the words that are complete are its first arguments, and the next one is where the cursor is - or the one that
    // takes the rest of the line, which the cursor is in however many words are after its start.
    private static VerbCompletionSite? SiteIn(IReadOnlyList<VerbArgument> form, IReadOnlyList<(string Text, int Start)> complete, (string Text, int Start)? typing, string arguments)
    {
        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < form.Count; index++)
        {
            var argument = form[index];
            if (argument.TakesTheRest)
            {
                var start = index < complete.Count ? complete[index].Start : typing is { } word && index == complete.Count ? word.Start : arguments.Length;
                return index <= complete.Count ? new VerbCompletionSite(argument, arguments[start..], written) { Form = form } : null;
            }

            if (index == complete.Count)
            {
                return new VerbCompletionSite(argument, typing?.Text ?? string.Empty, written) { Form = form };
            }

            if (!Matches(argument, complete[index].Text))
            {
                return null;
            }

            written[argument.Name] = complete[index].Text;
        }

        // every argument of the form is written: there is no more to it.
        return null;
    }

    public static IReadOnlyList<(string Part, SettingDescriptor? Setting)> SettingPartsAfter(
        IEnumerable<SettingDescriptor> settings, string typed)
    {
        var dot = typed.LastIndexOf('.');
        var section = dot < 0 ? string.Empty : typed[..(dot + 1)];
        var parts = new List<(string Part, SettingDescriptor? Setting)>();
        foreach (var setting in settings.Where(setting => setting.Key.StartsWith(section, StringComparison.OrdinalIgnoreCase)))
        {
            var rest = setting.Key[section.Length..];
            var end = rest.IndexOf('.');
            (string Part, SettingDescriptor? Setting) part = end < 0 ? (rest, setting) : (rest[..end], null);
            if (part.Part.Length > 0 && !parts.Any(other => string.Equals(other.Part, part.Part, StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add(part);
            }
        }

        return parts;
    }

    // what a complete word can be, for the argument it is written for.
    private static bool Matches(VerbArgument argument, string word) => argument.Kind switch
    {
        VerbArgumentKind.Keyword => argument.Values.Any(value => string.Equals(value.Value, word, StringComparison.OrdinalIgnoreCase)),
        VerbArgumentKind.LineNumber => word.All(char.IsAsciiDigit),
        VerbArgumentKind.Setting => argument.Settings.Any(setting => string.Equals(setting.Key, word, StringComparison.OrdinalIgnoreCase)
            || setting.Key.StartsWith(word + ".", StringComparison.OrdinalIgnoreCase)),
        _ => true,
    };

    private static List<(string Text, int Start)> Words(string text)
    {
        var words = new List<(string Text, int Start)>();
        var index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            words.Add((text[start..index], start));
        }

        return words;
    }
}
