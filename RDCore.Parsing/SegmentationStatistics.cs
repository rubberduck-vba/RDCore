namespace RDCore.Parsing;

/// <summary>
/// How the modules a <see cref="ModuleParser"/> read were read: whole, with the grammar's fast prediction; a segment at a time; or whole, slowly, after the
/// segments gave up - each give-up by why. What says how many modules take the slow path, and why.
/// </summary>
internal sealed class SegmentationStatistics
{
    /// <summary>How a module was read, or why it was not read a segment at a time.</summary>
    public enum Path
    {
        /// <summary>Whole, with the fast prediction: no segment was needed.</summary>
        Whole,

        /// <summary>A segment at a time, stitched.</summary>
        Stitched,

        /// <summary>Whole and slowly: the module is one segment, nothing to split.</summary>
        OneSegment,

        /// <summary>Whole and slowly: a member's header is in another member.</summary>
        HeaderInAMember,

        /// <summary>Whole and slowly: an <c>End Sub</c>, <c>End Function</c> or <c>End Property</c> is outside a member.</summary>
        EndOutsideAMember,

        /// <summary>Whole and slowly: a header or an <c>End</c> is not the first statement of its line.</summary>
        Misplaced,

        /// <summary>Whole and slowly: there is code between two members, or after the last.</summary>
        CodeBetweenMembers,

        /// <summary>Whole and slowly: a member has no <c>End</c>.</summary>
        MemberNotEnded,

        /// <summary>Whole and slowly: a segment has a syntax error.</summary>
        SegmentHadErrors,

        /// <summary>Whole and slowly: a segment's parse failed.</summary>
        SegmentFailed,
    }

    private readonly long[] _counts = new long[Enum.GetValues<Path>().Length];

    /// <summary>Counts a module read by <paramref name="path"/>.</summary>
    public void Count(Path path) => Interlocked.Increment(ref _counts[(int)path]);

    /// <summary>How many modules were read by <paramref name="path"/>.</summary>
    public long this[Path path] => Interlocked.Read(ref _counts[(int)path]);
}
