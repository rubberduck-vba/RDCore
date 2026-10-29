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
/// <para>
/// The two bodies alone do not always say which instructions were kept: of <c>a, Stop, a, Stop, a</c> edited into <c>a, Stop, a</c>, either pair may have
/// been removed, and the text says nothing of which. So the alignment is made twice - keeping the earliest of identical instructions, and keeping the
/// latest - and what the two say alike is <em>certain</em>: it is what every longest common subsequence says. What they say otherwise is a guess, and
/// an activation is not moved on a guess.
/// </para>
/// </remarks>
public sealed class InstructionAlignment
{
    // the cells of the comparison past which what the edit changed is not compared: an instruction in it is then neither kept nor removed for certain.
    private const long MostCellsCompared = 4_000_000;

    private readonly Side _earliest;
    private readonly Side _latest;
    private readonly int _length;

    // one alignment: where each instruction of the body as it was is in the edited body, and which ones were not compared.
    private sealed record class Side(int?[] Offsets, bool[] Uncompared);

    private InstructionAlignment(Side earliest, Side latest, int length) => (_earliest, _latest, _length) = (earliest, latest, length);

    /// <summary>
    /// The instructions of <paramref name="before"/> that <paramref name="after"/>, its edited body, has as they were.
    /// </summary>
    /// <param name="before">The body as it was.</param>
    /// <param name="after">The body as it was edited.</param>
    public static InstructionAlignment Of(InstructionList before, InstructionList after)
    {
        var old = before.Items.Select(Fingerprint).ToArray();
        var now = after.Items.Select(Fingerprint).ToArray();

        // the latest-kept alignment is the earliest-kept one of the two bodies read backwards.
        var earliest = Align(old, now);
        var backwards = Align([.. old.Reverse()], [.. now.Reverse()]);
        var latest = new Side(new int?[old.Length], new bool[old.Length]);
        for (var index = 0; index < old.Length; index++)
        {
            var mirrored = old.Length - 1 - index;
            latest.Offsets[index] = backwards.Offsets[mirrored] is { } kept ? now.Length - 1 - kept : null;
            latest.Uncompared[index] = backwards.Uncompared[mirrored];
        }

        return new InstructionAlignment(earliest, latest, now.Length);
    }

    // a longest common subsequence that keeps the earliest of identical instructions: what is as it was at the start is kept first, then what is as it
    // was at the end, and in between the first match is taken whenever it is one of the longest.
    private static Side Align(string[] old, string[] now)
    {
        var side = new Side(new int?[old.Length], new bool[old.Length]);
        var start = 0;
        while (start < old.Length && start < now.Length && old[start] == now[start])
        {
            side.Offsets[start] = start;
            start++;
        }

        var (oldEnd, nowEnd) = (old.Length, now.Length);
        while (oldEnd > start && nowEnd > start && old[oldEnd - 1] == now[nowEnd - 1])
        {
            side.Offsets[--oldEnd] = --nowEnd;
        }

        var (rows, columns) = (oldEnd - start, nowEnd - start);
        if (rows == 0 || columns == 0)
        {
            return side;
        }

        if ((long)rows * columns > MostCellsCompared)
        {
            Array.Fill(side.Uncompared, true, start, rows);
            return side;
        }

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
            if (old[start + row] == now[start + column] && lengths[row, column] == lengths[row + 1, column + 1] + 1)
            {
                side.Offsets[start + row] = start + column;
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

        return side;
    }

    /// <summary>
    /// Whether what the edit did with the instruction at <paramref name="offset"/> of the body as it was is certain: kept, at the same place of the edited
    /// body, by every longest common subsequence - or by none.
    /// </summary>
    /// <param name="offset">An offset of the body as it was, or its length.</param>
    public bool IsCertain(int offset)
        => offset == _earliest.Offsets.Length
            || (offset >= 0 && offset < _earliest.Offsets.Length && !_earliest.Uncompared[offset] && !_latest.Uncompared[offset]
                && _earliest.Offsets[offset] == _latest.Offsets[offset]);

    /// <summary>
    /// Where the instruction at <paramref name="offset"/> of the body as it was is in the edited body; none when it was edited or removed, or when that is
    /// not certain (<see cref="IsCertain"/>). The end of the body, its length, is the end of the edited one.
    /// </summary>
    /// <param name="offset">An offset of the body as it was, or its length.</param>
    public int? this[int offset] => offset == _earliest.Offsets.Length ? _length : IsCertain(offset) ? _earliest.Offsets[offset] : null;

    /// <summary>
    /// Where an activation that waits before the instruction at <paramref name="offset"/> goes on in the edited body: at that instruction, if it is as it
    /// was; and if it was edited or removed, at what the edit put in its place - the instruction after the last one before it that is as it was. None when
    /// that is not certain.
    /// </summary>
    /// <param name="offset">An offset of the body as it was, or its length.</param>
    public int? Before(int offset) => Agreed(side => offset == side.Offsets.Length ? _length : side.Offsets[offset] ?? Following(side, offset));

    /// <summary>
    /// Where an activation that waits after the instruction at <paramref name="offset"/> - one that has run, as a <c>Stop</c> has - goes on in the edited
    /// body: at the instruction after it, or after what the edit put in its place. A line inserted right after it runs; one edited above it does not run
    /// again. None when that is not certain.
    /// </summary>
    /// <param name="offset">An offset of the body as it was.</param>
    public int? After(int offset) => Agreed(side => side.Offsets[offset] is { } kept ? kept + 1 : Following(side, offset));

    // what both alignments say, when they say it alike and compared what it rests on.
    private int? Agreed(Func<Side, int?> place)
        => place(_earliest) is { } earliest && place(_latest) is { } latest && earliest == latest ? earliest : null;

    // the instruction after the last one before the offset that is as it was: the start of the body, when there is none. None when an instruction it
    // passes over, or the one at the offset, was not compared.
    private static int? Following(Side side, int offset)
    {
        for (var before = Math.Min(offset, side.Offsets.Length - 1); before >= 0; before--)
        {
            if (side.Uncompared[before])
            {
                return null;
            }

            if (before < offset && side.Offsets[before] is { } kept)
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
