using System.Text;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Where a session's <c>Print</c> output goes — the <c>Immediate</c> window's analogue.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.4.5.8</strong>'s output rules are written in terms of a <em>current line
/// position</em>: a <c>,</c> advances to the next fourteen-character print zone, a <c>Tab(n)</c>
/// moves to column <c>n</c>, and a trailing <c>;</c> leaves the line open for the next statement to
/// continue. None of that is expressible against a plain <c>TextWriter</c>, which is why the position
/// lives here rather than in whatever renders the text.
/// </remarks>
public interface IRuntimeOutput
{
    /// <summary>
    /// The current line position, one-based — <c>1</c> at the start of a line, exactly as
    /// <strong>MS-VBAL §5.4.5</strong> counts it.
    /// </summary>
    int LinePosition { get; }

    /// <summary>
    /// Writes <paramref name="text"/> at the current line position, which advances by its length.
    /// </summary>
    /// <param name="text">The text to write; it contains no line terminators.</param>
    void Write(string text);

    /// <summary>
    /// Ends the current line and returns the line position to <c>1</c>.
    /// </summary>
    void WriteLine();
}

/// <summary>
/// The output of a session nobody is watching: everything written is discarded.
/// </summary>
/// <remarks>
/// What a session composed without an output channel gets, so that a <c>Print</c> statement in one
/// is a no-op rather than a crash — the same way a VBA host with no <c>Immediate</c> window still
/// runs <c>Debug.Print</c>.
/// </remarks>
public sealed class NullRuntimeOutput : IRuntimeOutput
{
    /// <summary>The single instance; it has no state to keep.</summary>
    public static IRuntimeOutput Instance { get; } = new NullRuntimeOutput();

    private NullRuntimeOutput() { }

    /// <inheritdoc/>
    public int LinePosition => 1;

    /// <inheritdoc/>
    public void Write(string text) { }

    /// <inheritdoc/>
    public void WriteLine() { }
}

/// <summary>
/// Collects a session's output in memory, as lines.
/// </summary>
/// <remarks>
/// What a host that answers a "run this" request uses: the caller wants the output back with the
/// result, not streamed somewhere. A line the program left open with a trailing <c>;</c> is part of
/// <see cref="Lines"/> as soon as it has anything on it, so output that never reached a line break
/// is still reported.
/// </remarks>
public sealed class RuntimeOutputBuffer : IRuntimeOutput
{
    private readonly List<string> _lines = [];
    private readonly StringBuilder _current = new();

    /// <inheritdoc/>
    public int LinePosition => _current.Length + 1;

    /// <summary>
    /// Every completed line, plus the line in progress when there is one.
    /// </summary>
    public IReadOnlyList<string> Lines => _current.Length == 0 ? _lines : [.. _lines, _current.ToString()];

    /// <inheritdoc/>
    public void Write(string text) => _current.Append(text);

    /// <inheritdoc/>
    public void WriteLine()
    {
        _lines.Add(_current.ToString());
        _current.Clear();
    }
}
