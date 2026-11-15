using System.Collections.Immutable;

namespace RDCore.Runtime.Execution;

/// <summary>
/// An output list evaluated for an object's <c>Print</c> method (<c>Form1.Print</c>), which lays it out on its own surface: the parts in source order, and
/// whether the output ends the line.
/// </summary>
/// <param name="Parts">The parts, in source order.</param>
/// <param name="EndsLine">Whether the output ends the line: the last item has no separator after it.</param>
public sealed record class PrintedOutput(ImmutableArray<PrintedPart> Parts, bool EndsLine);

/// <summary>
/// A part of an output list (<strong>MS-VBAL §5.4.5.8.1</strong>), evaluated.
/// </summary>
/// <param name="Kind">What the part is.</param>
/// <param name="Text">The output string of an expression (<see cref="PrintedPartKind.Text"/>).</param>
/// <param name="Count">The count of a <c>Spc</c>, never negative, or the column of a <c>Tab</c>; none for a <c>Tab</c> to the next print zone.</param>
public readonly record struct PrintedPart(PrintedPartKind Kind, string? Text = null, int? Count = null);

/// <summary>
/// What a <see cref="PrintedPart"/> is.
/// </summary>
public enum PrintedPartKind
{
    /// <summary>
    /// The output string of an expression.
    /// </summary>
    Text,

    /// <summary>
    /// <c>Spc(n)</c>: spaces.
    /// </summary>
    Spc,

    /// <summary>
    /// <c>Tab(n)</c>, a column; <c>Tab</c> with no column, or a comma, the next print zone.
    /// </summary>
    Tab,
}
