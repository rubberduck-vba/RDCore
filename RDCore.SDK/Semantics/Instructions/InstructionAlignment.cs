using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Semantics.Instructions;

/// <summary>
/// The instructions of a procedure's body that an edit of its code left as they were, each with where it now is in the edited body: what an
/// activation that was in the middle of the body when it was edited goes on from in the edited one.
/// </summary>
/// <remarks>
/// <para>
/// An instruction is what it does - its kind, and its statement written as <see cref="SyntaxNodeJson.HeaderOptions"/> write it: wherever it is
/// written, and without the statements of a block, which are instructions of their own. Where it branches to is no part of it: those are offsets
/// of the body it is in, and the edited body has its own.
/// </para>
/// <para>
/// The instructions left as they were are a longest common subsequence of the two bodies: the most instructions of the body that can be kept, in
/// the order they are in. An instruction that is not among them was edited or removed.
/// </para>
/// </remarks>
public sealed class InstructionAlignment
{
    // the bodies' length, past which the common subsequence is not looked for in what the edit changed: what it changed is then all changed.
    private const long MostCellsCompared = 4_000_000;

    private readonly int?[] _offsets;
    private readonly int _length;

    private InstructionAlignment(int?[] offsets, int length) => (_offsets, _length) = (offsets, length);

    /// <summary>
    /// The instructions of <paramref name="before"/> that <paramref name="after"/>, its edited body, has as they were.
    /// </summary>
    /// <param name="before">The body as it was.</param>
    /// <param name="after">The body as it was edited.</param>
    public static InstructionAlignment Of(InstructionList before, InstructionList after)
    {
        var old = before.Items.Select(Fingerprint).ToArray();
        var now = after.Items.Select(Fingerprint).ToArray();
        var offsets = new int?[old.Length];

        // what the edit did not reach, at either end, is as it was; the common subsequence is looked for in between.
        var start = 0;
        while (start < old.Length && start < now.Length && old[start] == now[start])
        {
            offsets[start] = start;
            start++;
        }

        var (oldEnd, nowEnd) = (old.Length, now.Length);
        while (oldEnd > start && nowEnd > start && old[oldEnd - 1] == now[nowEnd - 1])
        {
            offsets[--oldEnd] = --nowEnd;
        }

        var (rows, columns) = (oldEnd - start, nowEnd - start);
        if (rows > 0 && columns > 0 && (long)rows * columns <= MostCellsCompared)
        {
            // the length of the longest common subsequence of each pair of suffixes, and the subsequence read off from the start.
            var lengths = new int[rows + 1, columns + 1];
            for (var i = rows - 1; i >= 0; i--)
            {
                for (var j = columns - 1; j >= 0; j--)
                {
                    lengths[i, j] = old[start + i] == now[start + j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
                }
            }

            var (row, column) = (0, 0);
            while (row < rows && column < columns)
            {
                if (old[start + row] == now[start + column])
                {
                    offsets[start + row] = start + column;
                    (row, column) = (row + 1, column + 1);
                }
                else if (lengths[row + 1, column] >= lengths[row, column + 1])
                {
                    row++;
                }
                else
                {
                    column++;
                }
            }
        }

        return new InstructionAlignment(offsets, after.Items.Length);
    }

    /// <summary>
    /// Where the instruction at <paramref name="offset"/> of the body as it was is in the edited body; none when it was edited or removed. The end of
    /// the body, its length, is the end of the edited one.
    /// </summary>
    /// <param name="offset">An offset of the body as it was, or its length.</param>
    public int? this[int offset] => offset == _offsets.Length ? _length : offset >= 0 && offset < _offsets.Length ? _offsets[offset] : null;

    /// <summary>
    /// Where an activation that waits before the instruction at <paramref name="offset"/> goes on in the edited body: at that instruction, if it is as it
    /// was; and if it was edited or removed, at what the edit put in its place - the instruction after the last one before it that is as it was.
    /// </summary>
    /// <param name="offset">An offset of the body as it was, or its length.</param>
    public int Before(int offset) => this[offset] ?? Following(offset);

    /// <summary>
    /// Where an activation that waits after the instruction at <paramref name="offset"/> - one that has run, as a <c>Stop</c> has - goes on in the edited
    /// body: at the instruction after it, or after what the edit put in its place. A line inserted right after it runs; one edited above it does not run
    /// again.
    /// </summary>
    /// <param name="offset">An offset of the body as it was.</param>
    public int After(int offset) => this[offset] is { } kept ? kept + 1 : Following(offset);

    // the instruction after the last one before the offset that is as it was: the start of the body, when there is none.
    private int Following(int offset)
    {
        for (var before = Math.Min(offset, _offsets.Length) - 1; before >= 0; before--)
        {
            if (_offsets[before] is { } kept)
            {
                return kept + 1;
            }
        }

        return 0;
    }

    private static string Fingerprint(Instruction instruction)
        => instruction.Node is { } node
            ? $"{instruction.Kind}|{System.Text.Json.JsonSerializer.Serialize<SyntaxNode>(node, SyntaxNodeJson.HeaderOptions)}"
            : $"{instruction.Kind}|";
}
