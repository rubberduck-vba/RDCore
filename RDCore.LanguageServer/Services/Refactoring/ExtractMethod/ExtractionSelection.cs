using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// Why a selection cannot be extracted as it is, before anything about what it does is asked.
/// </summary>
internal enum ExtractionSelectionRefusal
{
    /// <summary>
    /// The selection holds no whole statement of the procedure's body: it is on the header, or in no procedure, or holds only blank lines and comments.
    /// </summary>
    NoStatement,

    /// <summary>
    /// The selection cuts a block statement: it starts or ends inside the body of one it does not hold whole (half an <c>If…End If</c>).
    /// </summary>
    CutsABlock,

    /// <summary>
    /// The selection cuts an <c>#If</c> block: it holds one of its directives (an <c>#Else</c>, its <c>#End If</c>), but not the whole block.
    /// </summary>
    CutsADirectiveBlock,
}

/// <summary>
/// The statements a selection is extracted as: the smallest whole run of statements of one statement list that holds it.
/// </summary>
/// <param name="Range">
/// Where the run is: from the start of its first statement or declaration to the end of its last, or of the <c>#If</c> block at either end of it, which is what the
/// editor shows will move.
/// </param>
/// <param name="Statements">The statements, in order: siblings of one block.</param>
/// <param name="Declarations">
/// The declarations of local variables and constants (<c>Dim</c>, <c>Static</c>, <c>Const</c>) that the run holds, in order, among its statements: they are written
/// in the block as statements are, and run no code.
/// </param>
/// <param name="Region">The offsets of the instructions the statements were lowered to, the closers of their blocks included: a contiguous run of the procedure's instructions.</param>
internal sealed record class ExtractionSelection(SourceRange Range, ImmutableArray<StatementNode> Statements, ImmutableArray<SyntaxNode> Declarations, IReadOnlySet<int> Region)
{
    /// <summary>
    /// The labels and line numbers written in the run, which move with it: a jump from outside to one of them lands in the selection. A label written before the
    /// run names the call that replaces it.
    /// </summary>
    public ImmutableArray<string> Labels { get; init; } = [];

    /// <summary>
    /// Snaps a requested range to whole statements.
    /// </summary>
    /// <param name="procedure">The procedure the range is in.</param>
    /// <param name="instructions">Its lowered body.</param>
    /// <param name="directives">The <c>#If</c> blocks of its module.</param>
    /// <param name="requested">What the user selected.</param>
    /// <param name="selection">The statements, when the range can be extracted as whole statements.</param>
    /// <param name="refusal">Why it cannot, otherwise.</param>
    /// <param name="cut">The block statement a <see cref="ExtractionSelectionRefusal.CutsABlock"/> refusal names.</param>
    /// <returns>Whether the range snapped.</returns>
    /// <remarks>
    /// <para>
    /// Each end of the range is resolved in the innermost statement list that holds both: an end on a line of a statement, but not inside the body of a block
    /// statement, takes that statement whole; an end between two statements takes the nearest one inside the range. An end inside the body of a block statement
    /// that the other end is outside of, or inside another block of it (the <c>Then</c> branch and the <c>Else</c> branch), cuts it: the selection is refused,
    /// naming it, rather than grown across a block boundary.
    /// </para>
    /// <para>
    /// On a line that holds several statements separated by <c>:</c>, an end takes the statement it is in, or, between two of them, the nearest one inside the
    /// range: the others of the line stay where they are. A block statement written on one line (a single-line <c>If</c>) is one statement, and is taken whole.
    /// </para>
    /// <para>
    /// A declaration of a local, and a label, is one of the block's lines as a statement is: an end on it takes it, and a declaration of several variables
    /// (<c>Dim a, b</c>) is taken whole. A run of declarations and labels alone holds no statement.
    /// </para>
    /// <para>
    /// A range that ends at the first character of a line, after the line it starts on, ends on the line before: it is how an editor selects whole lines.
    /// </para>
    /// <para>
    /// An <c>#If</c> block moves whole or not at all: a run that holds one of its directives takes the whole block, directives and every branch, when the range
    /// holds all of its lines, and is refused otherwise. A run inside one branch is extracted from that branch.
    /// </para>
    /// </remarks>
    public static bool TrySnap(MemberDeclarationNode procedure, InstructionList instructions, ExtractionDirectives directives, SourceRange requested,
        out ExtractionSelection? selection, out ExtractionSelectionRefusal refusal, out StatementNode? cut)
    {
        selection = null;
        refusal = ExtractionSelectionRefusal.NoStatement;
        cut = null;

        // a selection of whole lines ends at the start of the line after the last: that line is not in it.
        var start = requested.Start.Line;
        var end = requested.End is { Character: 0, Line: var next } && next > start ? next - 1 : requested.End.Line;
        if (start <= procedure.SourceLocation.Range.Start.Line || end >= procedure.SourceLocation.Range.End.Line)
        {
            // the header and the End line are the procedure's, which is never extracted.
            return false;
        }

        var from = requested.Start;
        var to = end == requested.End.Line ? requested.End : new SourcePosition(end, int.MaxValue);
        IReadOnlyList<SyntaxNode> list = [.. procedure.Children.Where(IsWrittenInTheBlock)];
        while (true)
        {
            var firstIndex = Enumerable.Range(0, list.Count).FirstOrDefault(index => LastLineOf(list[index]) >= start, -1);
            var lastIndex = Enumerable.Range(0, list.Count).LastOrDefault(index => FirstLineOf(list[index]) <= end, -1);

            // on a line of several statements, an end takes the one it is in, not the line: a statement that ends before the start, or starts past the end, stays.
            // A label opens its line: an end on it takes the label with the statement after it, and a start past it leaves it before the call, which it then names.
            while (firstIndex >= 0 && firstIndex + 1 < list.Count && list[firstIndex].SourceLocation.Range.End <= from && FirstLineOf(list[firstIndex + 1]) == from.Line)
            {
                firstIndex++;
            }

            while (lastIndex > 0 && IsAfter(list[lastIndex].SourceLocation.Range.Start, to, empty: from == to) && LastLineOf(list[lastIndex - 1]) == to.Line
                   && list[lastIndex - 1] is not LineLabelNode)
            {
                lastIndex--;
            }

            if (firstIndex < 0 || lastIndex < firstIndex)
            {
                return false;
            }

            var first = list[firstIndex];
            var last = list[lastIndex];

            // a block is told by its first statement: the lists are made again for each question.
            if (ReferenceEquals(first, last) && BlockHolding(first, start) is { } inner && BlockHolding(first, end) is { } other && ReferenceEquals(inner[0], other[0]))
            {
                // both ends inside one block of the same statement: the run is in that block.
                list = inner;
                continue;
            }

            foreach (var (statement, line) in new[] { (first, start), (last, end) })
            {
                if (BlockHolding(statement, line) is not null && !(ReferenceEquals(first, last) && OnHeaderOrCloser(first, start == line ? end : start)))
                {
                    refusal = ExtractionSelectionRefusal.CutsABlock;
                    cut = (StatementNode)statement;
                    return false;
                }
            }

            var run = list.Skip(firstIndex).Take(lastIndex - firstIndex + 1).ToArray();
            var statements = run.OfType<StatementNode>().ToImmutableArray();
            if (statements.IsEmpty)
            {
                return false;
            }

            var range = new SourceRange(first.SourceLocation.Range.Start, last.SourceLocation.Range.End);
            if (!TryHoldWhole(directives, start, end, ref range))
            {
                refusal = ExtractionSelectionRefusal.CutsADirectiveBlock;
                return false;
            }

            selection = new ExtractionSelection(range, statements, [.. run.Where(IsDeclaration)], RegionOf(statements, range, instructions))
            {
                Labels = [.. run.OfType<LineLabelNode>().Select(label => label.Name)],
            };
            return true;
        }
    }

    // grows the range over each #If block it holds a directive of, when the lines from start to end hold all of the block; a block it grows over may hold a
    // directive of another, which it then grows over too. Whether no block is cut.
    private static bool TryHoldWhole(ExtractionDirectives directives, int start, int end, ref SourceRange range)
    {
        var grown = true;
        while (grown)
        {
            grown = false;
            foreach (var block in directives.Blocks)
            {
                var (first, last) = (range.Start.Line, range.End.Line);
                if (!block.DirectiveLines.Any(line => line >= first && line <= last))
                {
                    continue;
                }

                if (block.Range.Start.Line < start || block.Range.End.Line > end)
                {
                    return false;
                }

                if (block.Range.Start < range.Start || block.Range.End > range.End)
                {
                    range = new SourceRange(block.Range.Start < range.Start ? block.Range.Start : range.Start, block.Range.End > range.End ? block.Range.End : range.End);
                    grown = true;
                }
            }
        }

        return true;
    }

    // what a block is written as, line by line: its statements, and the declarations of its locals and its labels among them.
    private static bool IsWrittenInTheBlock(SyntaxNode node) => node is StatementNode or LineLabelNode || IsDeclaration(node);

    private static bool IsDeclaration(SyntaxNode node) => node is VariableDeclarationNode or ConstantDeclarationNode;

    // whether a statement that starts at the position is past the end of the range: one that starts where the range ends is not in it, unless the range is
    // empty, a cursor at its start.
    private static bool IsAfter(SourcePosition position, SourcePosition end, bool empty) => empty ? position > end : position >= end;

    private static int FirstLineOf(SyntaxNode node) => node.SourceLocation.Range.Start.Line;

    private static int LastLineOf(SyntaxNode node) => node.SourceLocation.Range.End.Line;

    // whether a line is one the statement is written on, outside the bodies of its blocks: its header, an ElseIf or Else, its closer.
    private static bool OnHeaderOrCloser(SyntaxNode statement, int line) => line >= FirstLineOf(statement) && line <= LastLineOf(statement) && BlockHolding(statement, line) is null;

    // the statements of the block of the statement that are written around the line, when the line is inside the body of one of its blocks. The statement's own
    // first and last lines are its header and its closer, whatever else is written on them: a single-line If is written on one.
    private static IReadOnlyList<SyntaxNode>? BlockHolding(SyntaxNode statement, int line)
        => line == FirstLineOf(statement) || line == LastLineOf(statement)
            ? null
            : BlocksOf(statement).FirstOrDefault(block => block.Count > 0 && line >= FirstLineOf(block[0]) && line <= LastLineOf(block[^1]));

    // the blocks a statement guards: its own, and those of the branches it holds (ElseIf, Else, Case).
    private static IEnumerable<IReadOnlyList<SyntaxNode>> BlocksOf(SyntaxNode node)
    {
        var own = node.SiblingLists().Skip(1).Select(list => (IReadOnlyList<SyntaxNode>)[.. list.Where(IsWrittenInTheBlock)]).ToArray();
        var inBlocks = own.SelectMany(block => block).ToHashSet(ReferenceEqualityComparer.Instance);
        var children = node.Children.ToHashSet(ReferenceEqualityComparer.Instance);
        var branches = node.ChildNodes().Where(child => !children.Contains(child) && !inBlocks.Contains(child));
        return [.. own, .. branches.SelectMany(BlocksOf)];
    }

    // the instructions of the statements, and the closers lowering emitted for their blocks, which have no statement of their own: a Next, a Loop, and the jump
    // to its end the last branch of an If or a Select Case ends with, which has no line either.
    private static IReadOnlySet<int> RegionOf(ImmutableArray<StatementNode> statements, SourceRange range, InstructionList instructions)
    {
        var offsets = statements
            .SelectMany(statement => (IEnumerable<SyntaxNode>)[statement, .. statement.Descendants()])
            .OfType<StatementNode>()
            .Select(statement => instructions.TryGetOffset(statement.Identity, out var offset) ? offset : -1)
            .Where(offset => offset >= 0)
            .ToArray();
        if (offsets.Length == 0)
        {
            return new HashSet<int>();
        }

        var first = offsets.Min();
        var last = offsets.Max();
        while (last + 1 < instructions.Items.Length && instructions.Items[last + 1] is { Node: null } synthesized
               && (synthesized.Matching is { } opener && opener >= first && opener <= last
                   || synthesized.Location is { } location && location.Range.Start >= range.Start && location.Range.End <= range.End
                   || synthesized is { Kind: InstructionKind.Jump, Target: { } end } && instructions.Items[first..(last + 1)].Any(opener => opener.End == end)))
        {
            last++;
        }

        return Enumerable.Range(first, last - first + 1).ToHashSet();
    }
}
