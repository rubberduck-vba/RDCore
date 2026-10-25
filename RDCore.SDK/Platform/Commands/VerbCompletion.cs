namespace RDCore.SDK.Platform.Commands;

/// <summary>
/// The argument of a verb that is being written, and what of it is typed.
/// </summary>
/// <param name="Argument">The argument.</param>
/// <param name="Typed">
/// What is typed of it: the word being written, for an argument that is one word; everything after where it starts, for one that takes the rest of the line.
/// </param>
public sealed record class VerbCompletionSite(VerbArgument Argument, string Typed);

/// <summary>
/// Which argument of a verb is being written, as the arguments are typed (<see cref="VerbDescriptor"/>).
/// </summary>
public static class VerbCompletion
{
    /// <summary>
    /// The arguments that can be the one at the end of <paramref name="arguments"/>: one for each form of the verb that what is typed is the start of.
    /// </summary>
    /// <param name="verb">The verb.</param>
    /// <param name="arguments">What is typed after the verb, up to the cursor, as it is typed: not trimmed.</param>
    public static IReadOnlyList<VerbCompletionSite> Read(VerbDescriptor verb, string arguments)
    {
        var words = Words(arguments);
        (string Text, int Start)? typing = arguments.Length == 0 || char.IsWhiteSpace(arguments[^1]) ? null : words[^1];
        var complete = typing is null ? words : words.GetRange(0, words.Count - 1);

        var sites = new List<VerbCompletionSite>();
        foreach (var form in verb.Forms)
        {
            if (SiteIn(form, complete, typing, arguments) is { } site && !sites.Contains(site))
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
        for (var index = 0; index < form.Count; index++)
        {
            var argument = form[index];
            if (argument.TakesTheRest)
            {
                var start = index < complete.Count ? complete[index].Start : typing is { } word && index == complete.Count ? word.Start : arguments.Length;
                return index <= complete.Count ? new VerbCompletionSite(argument, arguments[start..]) : null;
            }

            if (index == complete.Count)
            {
                return new VerbCompletionSite(argument, typing?.Text ?? string.Empty);
            }

            if (!Matches(argument, complete[index].Text))
            {
                return null;
            }
        }

        // every argument of the form is written: there is no more to it.
        return null;
    }

    // what a complete word can be, for the argument it is written for.
    private static bool Matches(VerbArgument argument, string word) => argument.Kind switch
    {
        VerbArgumentKind.Keyword => argument.Values.Any(value => string.Equals(value.Value, word, StringComparison.OrdinalIgnoreCase)),
        VerbArgumentKind.LineNumber => word.All(char.IsAsciiDigit),
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
