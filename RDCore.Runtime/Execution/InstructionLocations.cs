using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Where a program that waits before an instruction is shown: a break, a step or a breakpoint, the line of each frame in the call stack.
/// </summary>
public static class InstructionLocations
{
    /// <summary>
    /// Where the instruction at <paramref name="offset"/> is shown, as MS-VBA shows a program paused before it.
    /// </summary>
    /// <remarks>
    /// That is the instruction's own location (<see cref="Instruction.Location"/>) when it has one. An instruction that has none ends a block: the jump at the
    /// end of a branch of an <c>If</c> block or of a <c>Case</c>, or the <c>End With</c> of a <c>With</c> block. It is shown on the closing line of the
    /// innermost block it ends: the <c>End If</c>, <c>End Select</c> or <c>End With</c>. The jump past the <c>Else</c> of a single-line <c>If</c> is shown
    /// on the <c>If</c>'s line, which is the whole statement.
    /// </remarks>
    /// <param name="list">The body the instruction is in.</param>
    /// <param name="offset">The instruction's offset in <paramref name="list"/>.</param>
    /// <returns>Where the instruction is shown; <see langword="null"/> past the end of the body.</returns>
    public static SourceLocation? Of(InstructionList list, int offset)
    {
        if (offset < 0 || offset >= list.Items.Length)
        {
            return null;
        }

        if (list.Items[offset].Location is { } location)
        {
            return location;
        }

        // the block headers before the instruction, innermost first: the first whose block goes on past it is the block it ends.
        for (var index = offset - 1; index >= 0; index--)
        {
            var header = list.Items[index];
            if (header.Node is InlineIfStatementNode inlineIf && header.Else == offset + 1)
            {
                return inlineIf.SourceLocation;
            }

            if (header.End > offset && header.Node is IfBlockStatementNode or SelectCaseStatementNode or WithStatementNode)
            {
                return ClosingLine(header.Node);
            }
        }

        return null;
    }

    // a block's last line, where its End If, End Select or End With is: a block whose range ends at the start of the next line ends on the line before it.
    private static SourceLocation ClosingLine(StatementNode block)
    {
        var (start, end) = (block.SourceLocation.Range.Start, block.SourceLocation.Range.End);
        var line = end.Character == 0 && end.Line > start.Line ? end.Line - 1 : end.Line;
        return new SourceLocation(block.SourceLocation.Uri, new SourceRange(new SourcePosition(line, 0), end.Line == line ? end : new SourcePosition(line, 0)));
    }
}
