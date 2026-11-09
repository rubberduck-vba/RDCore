using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring;

public enum SymbolMatch
{
    Exact,

    Prefix,

    WordStarts,

    Substring,

    Subsequence,
}

public static class SymbolSearch
{
    public static SymbolMatch? Match(string query, string name)
    {
        if (query.Length == 0 || string.Equals(query, name, StringComparison.OrdinalIgnoreCase))
        {
            return SymbolMatch.Exact;
        }

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return SymbolMatch.Prefix;
        }

        if (IsWordStarts(query, name))
        {
            return SymbolMatch.WordStarts;
        }

        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return SymbolMatch.Substring;
        }

        return IsSubsequence(query, name) ? SymbolMatch.Subsequence : null;
    }

    public static IReadOnlyList<T> Search<T>(string query, IEnumerable<T> candidates, Func<T, string> nameOf, int limit)
        => [.. candidates
            .Select(candidate => (Candidate: candidate, Name: nameOf(candidate), Match: Match(query.Trim(), nameOf(candidate))))
            .Where(found => found.Match is not null)
            .OrderBy(found => found.Match)
            .ThenBy(found => found.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(found => found.Candidate)];

    // each character of the query is, in order, the first of a word of the name: its first character, an uppercase letter after a lowercase one, or a
    // character after an underscore.
    private static bool IsWordStarts(string query, string name)
    {
        var next = 0;
        for (var i = 0; i < name.Length && next < query.Length; i++)
        {
            var isWordStart = i == 0
                || (char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                || (name[i - 1] == '_' && name[i] != '_');
            if (isWordStart && char.ToUpperInvariant(name[i]) == char.ToUpperInvariant(query[next]))
            {
                next++;
            }
        }

        return next == query.Length;
    }

    private static bool IsSubsequence(string query, string name)
    {
        var next = 0;
        for (var i = 0; i < name.Length && next < query.Length; i++)
        {
            if (char.ToUpperInvariant(name[i]) == char.ToUpperInvariant(query[next]))
            {
                next++;
            }
        }

        return next == query.Length;
    }
}
