namespace RDCore.CLI.App.Repl;

/// <summary>
/// A block of the program that folds: a structured statement - <c>If</c>, <c>Select Case</c>, a loop, <c>With</c> - from its first line to its last, as the
/// language server answers <c>textDocument/foldingRange</c>.
/// </summary>
/// <param name="StartLine">The zero-based line of the document the block starts at: the index of its first line in the program.</param>
/// <param name="EndLine">The zero-based line it ends at, its <c>End</c> statement.</param>
public readonly record struct ReplFold(int StartLine, int EndLine);

/// <summary>
/// The blocks of the program that are folded in a listing, by the line number they start at (<c>FOLD</c>).
/// </summary>
/// <remarks>
/// A fold is on a line number, as a breakpoint is: it stays when the line is typed again, and is cleared with the program (<c>NEW</c>, <c>LOAD</c>). A line
/// number that no longer starts a block folds nothing, and a listing leaves it be.
/// </remarks>
public sealed class ReplFolds
{
    private readonly HashSet<int> _folded = [];

    /// <summary>Whether the block that starts at a line is folded.</summary>
    /// <param name="lineNumber">The line number the block starts at.</param>
    public bool IsFolded(int lineNumber) => _folded.Contains(lineNumber);

    /// <summary>Folds the block that starts at a line, or unfolds it when it is folded.</summary>
    /// <param name="lineNumber">The line number the block starts at.</param>
    /// <returns>Whether the block is folded now.</returns>
    public bool Toggle(int lineNumber) => _folded.Add(lineNumber) || !_folded.Remove(lineNumber);

    /// <summary>Unfolds every block.</summary>
    public void Clear() => _folded.Clear();
}
