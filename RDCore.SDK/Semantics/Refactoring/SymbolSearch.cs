namespace RDCore.SDK.Semantics.Refactoring;

/// <summary>
/// How a name matches what a search for a symbol asks: from the closest match to the loosest. A name is matched regardless of case, as VBA names are.
/// </summary>
public enum SymbolMatch
{
    /// <summary>The name is what is asked: <c>Total</c> for <c>total</c>.</summary>
    Exact,

    /// <summary>The name begins with what is asked: <c>TotalCount</c> for <c>tot</c>.</summary>
    Prefix,

    /// <summary>Each character asked begins a word of the name, in order: <c>GetTotal</c> for <c>gt</c>, <c>max_items</c> for <c>mi</c>.</summary>
    WordStarts,

    /// <summary>The name has what is asked in it: <c>GrandTotal</c> for <c>total</c>.</summary>
    Substring,

    /// <summary>The name has each character asked, in order: <c>GrandTotal</c> for <c>gdt</c>.</summary>
    Subsequence,
}

/// <summary>
/// Searches symbols by name (<strong>LSP 3.17</strong> §Workspace Symbols Request): a name matches what a client asks when it has each character of it, in
/// order, and the closer the match, the earlier the name (<see cref="SymbolMatch"/>).
/// </summary>
public static class SymbolSearch
{
    /// <summary>
    /// How a name matches a query.
    /// </summary>
    /// <param name="query">What is asked: a whole name, the start of one, or some of its characters, in order.</param>
    /// <param name="name">The name.</param>
    /// <returns>How it matches; <see langword="null"/> when it does not. Every name matches an empty query exactly.</returns>
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

    /// <summary>
    /// The candidates that match a query, the closest matches first.
    /// </summary>
    /// <param name="query">What is asked (<see cref="Match"/>).</param>
    /// <param name="candidates">The candidates.</param>
    /// <param name="nameOf">The name of a candidate.</param>
    /// <param name="limit">How many to answer at most: a client asks again as its user types.</param>
    /// <returns>The candidates that match, by how close the match is, then by name.</returns>
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
