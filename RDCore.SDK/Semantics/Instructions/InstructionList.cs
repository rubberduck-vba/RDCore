using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Instructions;

/// <summary>
/// The ordered, keyed instruction list of one procedure body — <strong>RD-VBAL §3.5</strong>
/// (<strong>MS-VBAL §2.3.1</strong>: "sequentially evaluate each instruction in the frame"). Built by
/// <see cref="InstructionListLowering"/>; immutable.
/// </summary>
/// <remarks>
/// <see cref="Items"/> is dense: index <c>i</c> is offset <c>i</c>, and every offset a program counter
/// can hold names an entry. A label may resolve to <c>Items.Length</c> itself (a label with nothing
/// after it) — that offset is valid to hold, and a fetch there completes as if execution had reached
/// the end of the body.
/// </remarks>
public sealed class InstructionList
{
    private readonly IReadOnlyDictionary<string, int> _labels;
    private readonly IReadOnlyDictionary<SyntaxNodeId, int> _byNode;

    private readonly ImmutableArray<(int Offset, long LineNumber)> _lineNumbers;

    internal InstructionList(ImmutableArray<Instruction> items, IReadOnlyDictionary<string, int> labels, IReadOnlyDictionary<SyntaxNodeId, int> byNode)
    {
        Items = items;
        _labels = labels;
        _byNode = byNode;

        // a line *number* label is one whose name is decimal digits, which is what Erl reports and a named
        // label never sets. Ordered by offset so the one in effect at a given offset is a search away.
        _lineNumbers =
        [
            .. labels
                .Select(label => (label.Value, Parsed: long.TryParse(label.Key, out var number), Number: number))
                .Where(label => label.Parsed)
                .Select(label => (Offset: label.Value, LineNumber: label.Number))
                .OrderBy(label => label.Offset),
        ];
    }

    /// <summary>
    /// Every instruction, in program-counter order. <c>Items[i].Offset == i</c> for every <c>i</c>.
    /// </summary>
    public ImmutableArray<Instruction> Items { get; }

    /// <summary>
    /// Looks up the offset a <em>line label</em> or <em>line number</em> defines, by name
    /// (<strong>MS-VBAL §5.4.1.1</strong>). Case-insensitive, like every VBA identifier.
    /// </summary>
    /// <param name="label">The label name, or a line number's decimal string form.</param>
    /// <returns><c>false</c> when this procedure defines no such label.</returns>
    public bool TryGetLabelOffset(string label, out int offset) => _labels.TryGetValue(label, out offset);

    /// <summary>
    /// Looks up the offset a source <see cref="StatementNode"/> lowered to, by its stable
    /// <see cref="SyntaxNodeId"/> — the fault-statement identity a breakpoint or a runtime error
    /// anchors to.
    /// </summary>
    /// <returns><c>false</c> when <paramref name="nodeId"/> is not a statement of this body.</returns>
    public bool TryGetOffset(SyntaxNodeId nodeId, out int offset) => _byNode.TryGetValue(nodeId, out offset);

    /// <summary>
    /// The <em>line number</em> in effect at <paramref name="offset"/>: the nearest line-number label at or
    /// before it. This is what <c>Erl</c> reports for an error raised there.
    /// </summary>
    /// <remarks>
    /// A line number is sticky - it labels every statement after it until the next one - which is why this
    /// searches backwards rather than requiring the faulting statement to carry a label of its own. A
    /// <em>named</em> label never sets it; only a label spelled as decimal digits does.
    /// <para>
    /// 🎯 <c>long</c>, deliberately. MS-VBA reports <c>Erl</c> with <c>ushort</c> resolution and wraps
    /// around on anything that does not fit, so a program numbered past 65535 is told it faulted somewhere
    /// it did not. RD-VBA widens it instead, so every legal line number label is representable.
    /// </para>
    /// </remarks>
    /// <param name="offset">A program-counter offset into <see cref="Items"/>.</param>
    /// <param name="lineNumber">The line number in effect there, or <c>0</c> when no line number precedes it.</param>
    /// <returns><c>false</c> when no line number is in effect at <paramref name="offset"/>.</returns>
    public bool TryGetLineNumber(int offset, out long lineNumber)
    {
        lineNumber = 0;
        foreach (var candidate in _lineNumbers)
        {
            if (candidate.Offset > offset)
            {
                break;
            }

            lineNumber = candidate.LineNumber;
        }

        return lineNumber != 0;
    }
}
