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
        var module = new ModuleLevel();

        foreach (var (statement, modified) in Statements(lexer.GetAllTokens()))
        {
            var first = statement[0];
            switch (first.Type)
            {
                case VBALexer.SUB or VBALexer.FUNCTION or VBALexer.PROPERTY_GET or VBALexer.PROPERTY_LET or VBALexer.PROPERTY_SET:
                    open.Clear();
                    broken = false;
                    module.Enter();
                    continue;
                case VBALexer.END_SUB or VBALexer.END_FUNCTION or VBALexer.END_PROPERTY:
                    if (!broken && open.Count > 0 && Unclosed(open.Peek().Block) is { } unclosed)
                    {
                        errors.Add(Error(uri, open.Peek().Token, unclosed, anchorOffset));
                    }

                    open.Clear();
                    broken = false;
                    module.Leave();
                    continue;
            }

            if (!module.InProcedure)
            {
                if (module.Refuses(statement, modified) is { } refused)
                {
                    errors.Add(Error(uri, first, refused, anchorOffset));
                }

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

    // what is written outside the procedures of a module (MS-VBAL §5.1): declarations, before the first procedure - a statement that runs there is "Invalid
    // outside procedure", and anything but a procedure after one is "Only comments may appear after End Sub, End Function, or End Property" (MS-VBA, Excel-
    // verified). A class module's header, its attributes and the members of a Type or an Enum are none of these. One error for each stretch of statements
    // that are refused, as the grammar can tell nothing apart in it.
    private sealed class ModuleLevel
    {
        private bool _afterProcedure;
        private bool _inHeader;
        private bool _inTypeOrEnum;
        private bool _refused;

        public bool InProcedure { get; private set; }

        public void Enter()
        {
            InProcedure = true;
            _refused = false;
        }

        public void Leave()
        {
            InProcedure = false;
            _afterProcedure = true;
        }

        public VBCompileErrorId? Refuses(List<IToken> statement, bool modified)
        {
            var first = statement[0].Type;
            if (_inHeader)
            {
                _inHeader = !(first == VBALexer.END && statement.Count == 1);
                return null;
            }

            if (_inTypeOrEnum)
            {
                _inTypeOrEnum = first is not (VBALexer.END_TYPE or VBALexer.END_ENUM);
                return null;
            }

            if (first is VBALexer.VERSION or VBALexer.ATTRIBUTE)
            {
                return null;
            }

            if (first == VBALexer.BEGIN)
            {
                _inHeader = true;
                return null;
            }

            _inTypeOrEnum = first is VBALexer.TYPE or VBALexer.ENUM;
            var refused = _afterProcedure ? VBCompileErrorId.OnlyCommentsAfterEndProcedure
                : modified || IsDeclaration(first) ? (VBCompileErrorId?)null
                : VBCompileErrorId.InvalidOutsideProcedure;
            if (refused is null)
            {
                _refused = false;
                return null;
            }

            var firstOfStretch = !_refused;
            _refused = true;
            return firstOfStretch ? refused : null;
        }

        private static bool IsDeclaration(int token) => token is VBALexer.DIM or VBALexer.CONST or VBALexer.DECLARE or VBALexer.TYPE or VBALexer.ENUM
            or VBALexer.EVENT or VBALexer.IMPLEMENTS or VBALexer.OPTION_BASE or VBALexer.OPTION_EXPLICIT or VBALexer.OPTION_COMPARE
            or VBALexer.OPTION_PRIVATE_MODULE or VBALexer.DEFBOOL or VBALexer.DEFBYTE or VBALexer.DEFDATE or VBALexer.DEFDBL or VBALexer.DEFCUR
            or VBALexer.DEFINT or VBALexer.DEFLNG or VBALexer.DEFLNGLNG or VBALexer.DEFLNGPTR or VBALexer.DEFOBJ or VBALexer.DEFSNG or VBALexer.DEFSTR
            or VBALexer.DEFVAR;
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
    // number or a label before a statement is not part of it, and a single-line If - with the statements after its Then - is one statement. The modifiers
    // a declaration is written with (Public, Private...) are not what it declares, and are said apart: a statement that has them is a declaration.
    private static IEnumerable<(List<IToken> Tokens, bool Modified)> Statements(IList<IToken> tokens)
    {
        var statement = new List<IToken>();
        var modified = false;
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
                        yield return (statement, modified);
                    }

                    statement = [];
                    modified = false;
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
                    yield return (statement, modified);
                }

                statement = [];
                modified = false;
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

            if (statement.Count == 0 && token.Type is VBALexer.PUBLIC or VBALexer.PRIVATE or VBALexer.FRIEND or VBALexer.STATIC or VBALexer.GLOBAL)
            {
                modified = true;
                continue;
            }

            statement.Add(token);
        }

        if (statement.Count > 0)
        {
            yield return (statement, modified);
        }
    }
}
