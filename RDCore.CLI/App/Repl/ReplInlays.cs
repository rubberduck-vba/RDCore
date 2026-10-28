namespace RDCore.CLI.App.Repl;

/// <summary>
/// A hint the language server gives for a place in a line of the program (<strong>LSP 3.17</strong> §Inlay Hint Request): text that is not part of the line,
/// shown in it: the name of the parameter an argument is given to, before the argument; the type of a declaration that states none, after its name.
/// </summary>
/// <param name="Line">The zero-based line of the document: the index of the line in the program.</param>
/// <param name="Character">The zero-based character of the line the hint is shown at: the hint is inserted there, before the character that is there.</param>
/// <param name="Label">What the hint says, padded as the server asks.</param>
public readonly record struct ReplInlayHint(int Line, int Character, string Label);

/// <summary>
/// Whether a listing shows the hints of the language server (<c>INLAYS</c>).
/// </summary>
/// <remarks>
/// Off until it is said to be on: a listing is the program as it was typed, and the hints are not part of it.
/// </remarks>
public sealed class ReplInlays
{
    /// <summary>Whether a listing shows the hints.</summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Puts the hints of a line in its runs.
/// </summary>
public static class ReplInlayRuns
{
    /// <summary>
    /// The runs of a line with its hints in them, each as a run of its own in the ghost style, before the character it is for.
    /// </summary>
    /// <param name="runs">The runs of the line, which are all of its text.</param>
    /// <param name="hints">The hints of the line.</param>
    public static IReadOnlyList<ReplTextRun> With(IReadOnlyList<ReplTextRun> runs, IEnumerable<ReplInlayHint> hints)
    {
        var pending = new Queue<ReplInlayHint>(hints.OrderBy(hint => hint.Character));
        if (pending.Count == 0)
        {
            return runs;
        }

        var result = new List<ReplTextRun>(runs.Count + pending.Count);
        var at = 0;
        foreach (var run in runs)
        {
            var start = 0;
            while (pending.Count > 0 && pending.Peek().Character < at + run.Text.Length)
            {
                var hint = pending.Dequeue();
                var cut = Math.Max(start, hint.Character - at);
                if (cut > start)
                {
                    result.Add(run with { Text = run.Text[start..cut] });
                    start = cut;
                }

                result.Add(new ReplTextRun(hint.Label, ReplTextStyle.Ghost));
            }

            result.Add(start == 0 ? run : run with { Text = run.Text[start..] });
            at += run.Text.Length;
        }

        // a hint at the end of the line, or past it.
        result.AddRange(pending.Select(hint => new ReplTextRun(hint.Label, ReplTextStyle.Ghost)));
        return result;
    }
}
