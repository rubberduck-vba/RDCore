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
/// the order they are in. The two bodies alone do not always say which: of <c>a, Stop, a, Stop, a</c> edited into <c>a, Stop, a</c>, either pair may have
/// been removed, and the text says nothing of which. So what is said of an instruction is what <em>every</em> longest common subsequence says of it:
/// kept, at the one place they all keep it; removed, by all of them; or neither, and not <see cref="IsCertain">certain</see>. An activation is not
/// moved on what is not certain.
/// </para>
/// </remarks>
public sealed class InstructionAlignment
{
    // the cells of the comparison past which the bodies are not compared as a whole: only what is as it was at either end, and is written once in each, is
    // then certain.
    private const long MostCellsCompared = 2_250_000;

    // what is said of an instruction that is not kept.
    private const int Removed = -1;
    private const int Unknown = -2;

    // for each instruction of the body as it was: where every longest common subsequence keeps it, or Removed, or Unknown.
    private readonly int[] _placements;
    private readonly int _length;

    private InstructionAlignment(int[] placements, int length, bool isPartial) => (_placements, _length, IsPartial) = (placements, length, isPartial);

    /// <summary>
    /// The instructions of <paramref name="before"/> that <paramref name="after"/>, its edited body, has as they were.
    /// </summary>
    /// <param name="before">The body as it was.</param>
    /// <param name="after">The body as it was edited.</param>
    public static InstructionAlignment Of(InstructionList before, InstructionList after)
        => Of([.. before.Items.Select(Fingerprint)], [.. after.Items.Select(Fingerprint)]);

    // the alignment of two sequences of instructions, each given by what it is.
    internal static InstructionAlignment Of(string[] old, string[] now)
        => (long)(old.Length + 1) * (now.Length + 1) > MostCellsCompared
            ? new InstructionAlignment(PlaceByTheirEnds(old, now), now.Length, isPartial: true)
            : new InstructionAlignment(Place(old, now), now.Length, isPartial: false);

    /// <summary>
    /// Whether the bodies were too large to be compared as a whole: what is certain of them is then only what is as it was at the start or at the end of
    /// the body, and written once in each.
    /// </summary>
    public bool IsPartial { get; }

    /// <summary>
    /// Whether the edited body is the body as it was: the same instructions, each kept at its own offset - the module loaded again with no edit of this
    /// procedure, or with one that moved its lines only.
    /// </summary>
    public bool IsIdentity => _placements.Length == _length && _placements.Select((placement, offset) => placement == offset).All(kept => kept);

    // what every longest common subsequence says of each instruction. F[i, j] is the length of one of the first i and the first j instructions; B[i, j],
    // of the rest of them. Instruction i is kept at j by some longest one when the two are the same instruction and F[i, j] + 1 + B[i + 1, j + 1] is the
    // longest's length; it is kept by every one when the longest that leaves it out, the most of F[i, j] + B[i + 1, j] for any j, is shorter.
    internal static int[] Place(string[] old, string[] now)
    {
        var (n, m) = (old.Length, now.Length);
        var forward = new int[n + 1, m + 1];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < m; j++)
            {
                forward[i + 1, j + 1] = old[i] == now[j] ? forward[i, j] + 1 : Math.Max(forward[i, j + 1], forward[i + 1, j]);
            }
        }

        var backward = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                backward[i, j] = old[i] == now[j] ? backward[i + 1, j + 1] + 1 : Math.Max(backward[i + 1, j], backward[i, j + 1]);
            }
        }

        var longest = forward[n, m];
        var placements = new int[n];
        for (var i = 0; i < n; i++)
        {
            var (kept, places, without) = (Removed, 0, 0);
            for (var j = 0; j <= m; j++)
            {
                without = Math.Max(without, forward[i, j] + backward[i + 1, j]);
                if (j < m && old[i] == now[j] && forward[i, j] + 1 + backward[i + 1, j + 1] == longest)
                {
                    (kept, places) = (j, places + 1);
                }
            }

            placements[i] = places == 0 ? Removed : places == 1 && without < longest ? kept : Unknown;
        }

        return placements;
    }

    // bodies too large to compare as a whole: what is as it was at the start and at the end is kept, and certain where it is written once in each body.
    private static int[] PlaceByTheirEnds(string[] old, string[] now)
    {
        var placements = Enumerable.Repeat(Unknown, old.Length).ToArray();
        static HashSet<string> Single(string[] body) => [.. body.GroupBy(fingerprint => fingerprint).Where(group => group.Count() == 1).Select(group => group.Key)];
        var once = Single(old);
        once.IntersectWith(Single(now));
        var start = 0;
        while (start < old.Length && start < now.Length && old[start] == now[start])
        {
            placements[start] = once.Contains(old[start]) ? start : Unknown;
            start++;
        }

        var (oldEnd, nowEnd) = (old.Length, now.Length);
        while (oldEnd > start && nowEnd > start && old[oldEnd - 1] == now[nowEnd - 1])
        {
            (oldEnd, nowEnd) = (oldEnd - 1, nowEnd - 1);
            placements[oldEnd] = once.Contains(old[oldEnd]) ? nowEnd : Unknown;
        }

        return placements;
    }

    /// <summary>
    /// Whether what the edit did with the instruction at <paramref name="offset"/> of the body as it was is certain: kept by every longest common
    /// subsequence of the two bodies, at the same place of the edited body - or removed by every one.
    /// </summary>
    /// <param name="offset">An offset of the body as it was, or its length.</param>
    public bool IsCertain(int offset) => offset == _placements.Length || (offset >= 0 && offset < _placements.Length && _placements[offset] != Unknown);

    /// <summary>
    /// Where the instruction at <paramref name="offset"/> of the body as it was is in the edited body; none when it was edited or removed, or when that is
    /// not certain (<see cref="IsCertain"/>). The end of the body, its length, is the end of the edited one.
    /// </summary>
    /// <param name="offset">An offset of the body as it was, or its length.</param>
    public int? this[int offset] => offset == _placements.Length ? _length : offset >= 0 && offset < _placements.Length && _placements[offset] >= 0 ? _placements[offset] : null;

    /// <summary>
    /// Where an activation that waits before the instruction at <paramref name="offset"/> goes on in the edited body: at that instruction, if it is as it
    /// was; and if it was edited or removed, at what the edit put in its place - the instruction after the last one before it that is as it was. None when
    /// that is not certain.
    /// </summary>
    /// <param name="offset">An offset of the body as it was, or its length.</param>
    public int? Before(int offset)
        => offset == _placements.Length ? _length
            : !IsCertain(offset) ? null
            : _placements[offset] >= 0 ? _placements[offset]
            : Following(offset);

    /// <summary>
    /// Where an activation that waits after the instruction at <paramref name="offset"/> - one that has run, as a <c>Stop</c> has - goes on in the edited
    /// body: at the instruction after it, or after what the edit put in its place. A line inserted right after it runs; one edited above it does not run
    /// again. None when that is not certain.
    /// </summary>
    /// <param name="offset">An offset of the body as it was.</param>
    public int? After(int offset)
        => !IsCertain(offset) ? null
            : _placements[offset] >= 0 ? _placements[offset] + 1
            : Following(offset);

    // the instruction after the last one before the offset that is as it was - the start of the body, when there is none - when every one in between was
    // removed for certain.
    private int? Following(int offset)
    {
        for (var before = offset - 1; before >= 0; before--)
        {
            if (_placements[before] == Unknown)
            {
                return null;
            }

            if (_placements[before] >= 0)
            {
                return _placements[before] + 1;
            }
        }

        return 0;
    }

    private static string Fingerprint(Instruction instruction)
        => instruction.Node is { } node
            ? $"{instruction.Kind}|{System.Text.Json.JsonSerializer.Serialize<SyntaxNode>(node, SyntaxNodeJson.HeaderOptions)}"
            : $"{instruction.Kind}|";
}
