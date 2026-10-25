using RDCore.SDK.Client.Progress;

namespace RDCore.CLI.App.Console;

/// <summary>
/// The progress of a piece of work as one line of the console, the way PowerShell draws one (<c>Write-Progress</c>): what the work is, a bar of how much of it
/// is done, the percentage, and what it is doing now.
/// </summary>
/// <remarks>
/// Work that does not say how much of it is done has no percentage, and its bar is a segment that moves along it, a step each time the work says something:
/// it is going on, and no more is known.
/// </remarks>
public static class ConsoleProgressBar
{
    /// <summary>The cells of the bar itself, between its brackets.</summary>
    public const int BarWidth = 24;

    // the cells of the segment that moves along the bar of work that does not say how much of it is done.
    private const int SegmentWidth = 6;

    /// <summary>
    /// The line of a piece of work.
    /// </summary>
    /// <param name="state">Where the work stands.</param>
    /// <param name="width">The cells of a line of the console: the line is cut to fit in it.</param>
    /// <param name="frame">How many times the work said something: where the segment of a bar with no percentage is.</param>
    /// <param name="unicode">Whether the bar is drawn in block characters; in <c>#</c> and <c>-</c> otherwise, for an output that cannot show them.</param>
    public static string Render(WorkProgressState state, int width, int frame = 0, bool unicode = true)
    {
        var (full, empty) = unicode ? ('█', '░') : ('#', '-');
        string bar;
        string percentage;
        if (state.Percentage is { } done)
        {
            var filled = BarWidth * Math.Clamp(done, 0, 100) / 100;
            bar = new string(full, filled) + new string(empty, BarWidth - filled);
            percentage = $"{Math.Clamp(done, 0, 100),3}%";
        }
        else
        {
            var cells = new char[BarWidth];
            Array.Fill(cells, empty);
            var start = frame % (BarWidth - SegmentWidth + 1);
            Array.Fill(cells, full, start, SegmentWidth);
            bar = new string(cells);
            percentage = "    ";
        }

        var line = state.Message is { Length: > 0 } message
            ? $"{state.Title} [{bar}] {percentage} {message}"
            : $"{state.Title} [{bar}] {percentage}";
        return ConsoleText.Truncate(line, Math.Max(1, width - 1));
    }
}
