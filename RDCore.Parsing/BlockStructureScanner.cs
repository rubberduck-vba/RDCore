using Antlr4.Runtime;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.Parsing;

/// <summary>
/// The structure of the blocks of a module's procedures - <c>For</c>/<c>Next</c>, <c>Do</c>/<c>Loop</c>, block <c>If</c>/<c>End If</c>,
/// <c>Select Case</c>/<c>End Select</c>, <c>With</c>/<c>End With</c> - and the first statement that does not fit it, as MS-VBA reports it (Excel-verified):
/// <c>Next without For</c>, <c>Block If without End If</c>, and so on.
/// </summary>
/// <remarks>
/// The grammar refuses a module whose blocks do not match, at whatever token it can no longer parse - often the end of the procedure - and with what it
/// expected instead, which says little of what is wrong. This reads the statements that open and close blocks, one procedure at a time, and says which
/// one has nothing to close or is not closed. What it does not know MS-VBA's message for (<c>While</c>/<c>Wend</c>, <c>End Select</c>, <c>ElseIf</c>) it
/// leaves to the grammar's error.
/// </remarks>
internal static class BlockStructureScanner
{
    private enum Block { For, Do, While, If, Select, With }

    /// <summary>
    /// The block-structure errors of a module: one, at most, for each procedure - the first statement that does not fit, as MS-VBA stops at it.
    /// </summary>
    /// <param name="uri">The module.</param>
    /// <param name="source">Its source, as it is parsed.</param>
    /// <param name="anchorOffset">Where the source starts in the document.</param>
    public static ImmutableArray<VBSyntaxErrorInfo> Scan(Uri uri, string source, SourcePosition anchorOffset)
    {
        var lexer = new VBALexer(new AntlrInputStream(source));
        lexer.RemoveErrorListeners();
        var errors = ImmutableArray.CreateBuilder<VBSyntaxErrorInfo>();
        var open = new Stack<(Block Block, IToken Token)>();
        var broken = false;

        foreach (var statement in Statements(lexer.GetAllTokens()))
        {
            var first = statement[0];
            switch (first.Type)
            {
                case VBALexer.SUB or VBALexer.FUNCTION or VBALexer.PROPERTY_GET or VBALexer.PROPERTY_LET or VBALexer.PROPERTY_SET:
                    open.Clear();
                    broken = false;
                    continue;
                case VBALexer.END_SUB or VBALexer.END_FUNCTION or VBALexer.END_PROPERTY:
                    if (!broken && open.Count > 0 && Unclosed(open.Peek().Block) is { } unclosed)
                    {
                        errors.Add(Error(uri, open.Peek().Token, unclosed, anchorOffset));
                    }

                    open.Clear();
                    broken = false;
                    continue;
            }

            if (broken)
            {
                continue;
            }

            var mismatch = first.Type switch
            {
                VBALexer.FOR => Open(open, Block.For, first),
                VBALexer.DO => Open(open, Block.Do, first),
                VBALexer.WHILE => Open(open, Block.While, first),
                VBALexer.SELECT => Open(open, Block.Select, first),
                VBALexer.WITH => Open(open, Block.With, first),
                VBALexer.IF when statement[^1].Type == VBALexer.THEN => Open(open, Block.If, first),
                VBALexer.NEXT => CloseLoops(open, statement),
                VBALexer.LOOP => Close(open, Block.Do, VBCompileErrorId.LoopWithoutDo),
                VBALexer.WEND => Close(open, Block.While, null),
                VBALexer.END_IF => Close(open, Block.If, VBCompileErrorId.EndIfWithoutBlockIf),
                VBALexer.END_SELECT => Close(open, Block.Select, null),
                VBALexer.END_WITH => Close(open, Block.With, VBCompileErrorId.EndWithWithoutWith),
                VBALexer.ELSE => Within(open, Block.If, VBCompileErrorId.ElseWithoutIf),
                VBALexer.ELSEIF => Within(open, Block.If, null),
                VBALexer.CASE => Within(open, Block.Select, VBCompileErrorId.CaseWithoutSelectCase),
                _ => Fits.Yes,
            };

            if (!ReferenceEquals(mismatch, Fits.Yes))
            {
                broken = true;
                if (mismatch.Id is { } id)
                {
                    errors.Add(Error(uri, first, id, anchorOffset));
                }
            }
        }

        return errors.ToImmutable();
    }

    // whether a statement fits the blocks open around it; when it does not, MS-VBA's error for it, if it is one this knows.
    private sealed record class Fits(VBCompileErrorId? Id)
    {
        public static readonly Fits Yes = new((VBCompileErrorId?)null);
    }

    private static Fits Open(Stack<(Block, IToken)> open, Block block, IToken token)
    {
        open.Push((block, token));
        return Fits.Yes;
    }

    private static Fits Close(Stack<(Block Block, IToken)> open, Block block, VBCompileErrorId? error)
    {
        if (open.Count > 0 && open.Peek().Block == block)
        {
            open.Pop();
            return Fits.Yes;
        }

        return new Fits(error);
    }

    private static Fits Within(Stack<(Block Block, IToken)> open, Block block, VBCompileErrorId? error)
        => open.Count > 0 && open.Peek().Block == block ? Fits.Yes : new Fits(error);

    // `Next` closes one For loop, and `Next j, i` one for each variable it names.
    private static Fits CloseLoops(Stack<(Block Block, IToken)> open, List<IToken> statement)
    {
        var loops = 1 + statement.Count(token => token.Type == VBALexer.COMMA);
        for (var loop = 0; loop < loops; loop++)
        {
            if (Close(open, Block.For, VBCompileErrorId.NextWithoutFor) is { Id: not null } mismatch)
            {
                return mismatch;
            }
        }

        return Fits.Yes;
    }

    // what a block still open at the end of its procedure is reported as; null for one whose message is not known.
    private static VBCompileErrorId? Unclosed(Block block) => block switch
    {
        Block.For => VBCompileErrorId.ForWithoutNext,
        Block.Do => VBCompileErrorId.DoWithoutLoop,
        Block.If => VBCompileErrorId.BlockIfWithoutEndIf,
        Block.Select => VBCompileErrorId.SelectCaseWithoutEndSelect,
        Block.With => VBCompileErrorId.ExpectedEndWith,
        _ => null,
    };

    private static VBSyntaxErrorInfo Error(Uri uri, IToken token, VBCompileErrorId id, SourcePosition anchorOffset)
    {
        var position = new SourcePosition(token.Line - 1, token.Column).AnchoredAt(anchorOffset);
        return VBSyntaxErrorInfo.For(id, new SourceLocation(uri, new SourceRange(position, position)), token.Text);
    }

    // the statements of the source, each as its significant tokens: a statement ends at the end of its line or at a colon. A comment is not one, a line
    // number or a label before a statement is not part of it, and a single-line If - with the statements after its Then - is one statement.
    private static IEnumerable<List<IToken>> Statements(IList<IToken> tokens)
    {
        var statement = new List<IToken>();
        var inComment = false;
        var inSingleLineIf = false;
        foreach (var token in tokens)
        {
            switch (token.Type)
            {
                case VBALexer.NEWLINE:
                    inComment = false;
                    inSingleLineIf = false;
                    if (statement.Count > 0)
                    {
                        yield return statement;
                    }

                    statement = [];
                    continue;
                case VBALexer.WS or VBALexer.LINE_CONTINUATION:
                    continue;
            }

            if (inComment)
            {
                continue;
            }

            if (token.Type == VBALexer.SINGLEQUOTE || token.Type == VBALexer.REM && statement.Count == 0)
            {
                inComment = true;
                continue;
            }

            if (token.Type == VBALexer.COLON && !inSingleLineIf)
            {
                if (statement.Count > 0)
                {
                    yield return statement;
                }

                statement = [];
                continue;
            }

            // a line number before the statement.
            if (statement.Count == 0 && token.Type == VBALexer.INTEGERLITERAL)
            {
                continue;
            }

            // what follows the Then of a single-line If is the If's, to the end of the line.
            if (token.Type == VBALexer.THEN && statement.Count > 0 && statement[0].Type == VBALexer.IF)
            {
                statement.Add(token);
                inSingleLineIf = true;
                continue;
            }

            if (inSingleLineIf)
            {
                // the If is single-line: a token after its Then makes it so, and the statement is no block If.
                if (statement[^1].Type == VBALexer.THEN)
                {
                    statement.Add(token);
                }

                continue;
            }

            // the modifiers of a procedure's declaration are not what the declaration is.
            if (statement.Count == 0 && token.Type is VBALexer.PUBLIC or VBALexer.PRIVATE or VBALexer.FRIEND or VBALexer.STATIC or VBALexer.GLOBAL)
            {
                continue;
            }

            statement.Add(token);
        }

        if (statement.Count > 0)
        {
            yield return statement;
        }
    }
}
