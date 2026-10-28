using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using System.Text;
using System.Text.RegularExpressions;

namespace RDCore.SDK.Formatting;

/// <summary>
/// An edit of the indentation of a line: the <paramref name="Length"/> characters at <paramref name="Character"/> of line <paramref name="Line"/> are replaced by
/// <paramref name="NewText"/>. Only whitespace changes; a line number before the code is written again as it is.
/// </summary>
/// <param name="Line">The line, from 0.</param>
/// <param name="Character">Where what is replaced starts.</param>
/// <param name="Length">How many characters are replaced.</param>
/// <param name="NewText">What replaces them.</param>
public readonly record struct IndentationEdit(int Line, int Character, int Length, string NewText);

/// <summary>
/// Indents the lines of a module by what encloses them in its syntax tree (<see cref="CodeFormattingOptions"/>): a line is indented one level for each body it
/// is in - of a procedure, an <c>Enum</c> or a <c>Type</c>, a branch of an <c>If</c> block, a <c>Case</c>, a loop, a <c>With</c> block, a branch of an
/// <c>#If</c> directive.
/// </summary>
/// <remarks>
/// <para>
/// A level is <see cref="CodeFormattingOptions.IndentSpaces"/> spaces: the code of a module is indented with spaces, as VBA code is. A block's opener and its
/// closer, and the <c>ElseIf</c>, <c>Else</c> and <c>Case</c> lines between them, are at the level of the block; what they guard is a level further in. A
/// procedure's body is indented when <see cref="CodeFormattingOptions.IndentEntireProcedureBody"/> says so, the <c>Case</c> lines of a <c>Select Case</c> when
/// <see cref="CodeFormattingOptions.IndentCase"/> does, and what an <c>#If</c> guards when <see cref="CodeFormattingOptions.IndentCompilerDirectives"/> does.
/// </para>
/// <para>
/// A line the syntax tree has no statement of - a branch of an <c>#If</c> that is not compiled, code the parser could not build as you type - is indented by its
/// keywords instead, from the level of where it is: a block's opener indents the lines after it, and its closer ends that. The indenter never refuses to indent.
/// </para>
/// <para>
/// A line number stays at the start of its line, and the code after it is where the line's level has it, a space after the number at least. A line continued
/// from the one before (<c>_</c>) moves with the first line of its statement, as far as it does. A comment alone on its line is at the level of the code
/// around it; a blank line is left as it is.
/// </para>
/// </remarks>
public static partial class ModuleIndenter
{
    /// <summary>
    /// The edits that indent the lines of a module, from its first line to its last, or of the lines from <paramref name="firstLine"/> to
    /// <paramref name="lastLine"/>; none for a line that is indented as it should be.
    /// </summary>
    /// <param name="parse">The module, as it was parsed from <paramref name="text"/>.</param>
    /// <param name="text">The text of the module.</param>
    /// <param name="options">How code is indented.</param>
    /// <param name="firstLine">The first line to indent, from 0.</param>
    /// <param name="lastLine">The last line to indent.</param>
    public static IReadOnlyList<IndentationEdit> Indent(ModuleParseResult parse, string text, CodeFormattingOptions options, int firstLine = 0, int lastLine = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        var lines = LinesOf(text);
        var module = new ModuleLines(lines);
        var levels = LevelsOf(parse, module, options);
        var firstBlocks = FirstBlocksAtColumn1(parse, module, options);
        var edits = new List<IndentationEdit>();
        var shift = 0;
        var isDeclaration = false;
        for (var line = 0; line < lines.Length; line++)
        {
            string wanted;
            if (module.IsContinuation(line))
            {
                wanted = ContinuedText(module, line, shift, isDeclaration, options);
            }
            else
            {
                isDeclaration = IsDeclaration(lines[line]);
                var level = firstBlocks[line] || IsForcedToColumn1(lines[line], options) ? 0 : levels[line];
                wanted = StartedText(module, line, level, isDeclaration, options, out shift);
            }

            if (line >= firstLine && line <= lastLine)
            {
                edits.AddRange(WhitespaceEdits(line, lines[line], wanted));
            }
        }

        return edits;
    }

    /// <summary>
    /// The text of a module with indentation edits made to it (<see cref="Indent"/>).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="edits">The edits; those of a line do not overlap.</param>
    public static string Apply(string text, IReadOnlyList<IndentationEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(edits);
        var lines = text.Split('\n');

        // the edits of a line, from its last to its first: each is where the text before it puts it.
        foreach (var edit in edits.OrderByDescending(edit => edit.Line).ThenByDescending(edit => edit.Character))
        {
            var line = lines[edit.Line];
            lines[edit.Line] = line[..edit.Character] + edit.NewText + line[(edit.Character + edit.Length)..];
        }

        return string.Join('\n', lines);
    }

    // the edits that make a line the text it is to have: one for each run of whitespace that is not what it is to be. What is to change otherwise is the whole
    // line replaced, which the engine never asks for but does not fail on.
    private static IEnumerable<IndentationEdit> WhitespaceEdits(int line, string text, string wanted)
    {
        if (text == wanted)
        {
            return [];
        }

        var edits = new List<IndentationEdit>();
        var (at, to) = (0, 0);
        while (at < text.Length || to < wanted.Length)
        {
            if (at < text.Length && to < wanted.Length && !char.IsWhiteSpace(text[at]) && !char.IsWhiteSpace(wanted[to]))
            {
                if (text[at] != wanted[to])
                {
                    return [new IndentationEdit(line, 0, text.Length, wanted)];
                }

                (at, to) = (at + 1, to + 1);
                continue;
            }

            var (from, into) = (at, to);
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }

            while (to < wanted.Length && char.IsWhiteSpace(wanted[to]))
            {
                to++;
            }

            if (at == from && to == into)
            {
                return [new IndentationEdit(line, 0, text.Length, wanted)];
            }

            if (text[from..at] != wanted[into..to])
            {
                edits.Add(new IndentationEdit(line, from, at - from, wanted[into..to]));
            }
        }

        return edits;
    }

    // the lines of the text, without what ends them.
    private static string[] LinesOf(string text) => [.. text.Split('\n').Select(line => line.TrimEnd('\r'))];

    // a comment alone on its line: an apostrophe, or Rem.
    private static bool IsComment(string line) => CommentLine().IsMatch(line);

    // a line its setting puts in column 1, whatever its level: a directive, a bare Debug.Print, Debug.Assert or Stop.
    private static bool IsForcedToColumn1(string line, CodeFormattingOptions options)
    {
        var code = Keyworded(line);
        return (options.ForceCompilerDirectivesInColumn1 && code.StartsWith('#'))
            || (options.ForcesDebugPrintInColumn1 && DebugPrint().IsMatch(code))
            || (options.ForcesDebugAssertInColumn1 && DebugAssert().IsMatch(code))
            || (options.ForcesStopInColumn1 && StopStatement().IsMatch(code));
    }

    // the first blocks of a procedure that are not indented: what comes right after its header, its comments and its declarations in any order, up to the
    // first line that is neither - or an empty one, unless empty lines are ignored there. The first comments of an Enum or a Type too, when it is indented as a
    // procedure is.
    private static bool[] FirstBlocksAtColumn1(ModuleParseResult parse, ModuleLines module, CodeFormattingOptions options)
    {
        var atColumn1 = new bool[module.Lines.Length];
        if (parse.SyntaxTree is not { } tree || (options.IndentFirstCommentBlock && options.IndentFirstDeclarationBlock))
        {
            return atColumn1;
        }

        foreach (var member in tree.Descendants().OfType<MemberDeclarationNode>())
        {
            var range = member.SourceLocation.Range;
            var end = module.LastLineOf(range);
            var isProcedure = member.MemberKind is MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;
            var isEnumOrType = member.MemberKind is MemberKind.Enum or MemberKind.UserDefinedType;
            if (!isProcedure && !(isEnumOrType && options.IndentEnumTypeAsProcedure && !options.IndentFirstCommentBlock))
            {
                continue;
            }

            for (var line = module.HeaderEnd(range.Start.Line) + 1; line < end; line++)
            {
                var text = module.Lines[line];
                if (string.IsNullOrWhiteSpace(text))
                {
                    if (!options.IgnoreEmptyLinesInFirstBlocks)
                    {
                        break;
                    }

                    continue;
                }

                if (IsComment(text))
                {
                    atColumn1[line] = !options.IndentFirstCommentBlock;
                    continue;
                }

                if (!isProcedure || !Declaration().IsMatch(Keyworded(text)))
                {
                    break;
                }

                // a declaration and the lines it is continued on.
                var last = module.HeaderEnd(line);
                for (; line <= last; line++)
                {
                    atColumn1[line] = !options.IndentFirstDeclarationBlock;
                }

                line = last;
            }
        }

        return atColumn1;
    }

    // the first line of a statement: indented to its level, a line number kept at the start of it, its declaration and its end-of-line comment aligned. How far
    // its code moved is what its continuations move.
    private static string StartedText(ModuleLines module, int line, int level, bool isDeclaration, CodeFormattingOptions options, out int shift)
    {
        shift = 0;
        var text = module.Lines[line];
        var indentation = level * options.IndentSpaces;
        if (string.IsNullOrWhiteSpace(text))
        {
            // an empty line is left as it is, emptied, or indented to its level, as the setting says.
            return options.EmptyLineHandlingMethod switch
            {
                EmptyLineHandlingMethod.Remove => string.Empty,
                EmptyLineHandlingMethod.Indent => new string(' ', indentation),
                _ => text,
            };
        }

        // a comment alone on its line that is not aligned with the code is where it is written.
        if (!options.AlignCommentsWithCode && IsComment(text))
        {
            return text;
        }

        string head;
        int codeAt;
        if (LineNumber().Match(text) is { Success: true } number)
        {
            // the number where it is written, and the code where the level has it: one space after the number at least.
            var prefix = number.Groups["number"].Value;
            head = prefix + new string(' ', Math.Max(indentation, prefix.Length + 1) - prefix.Length);
            codeAt = number.Length;
        }
        else
        {
            head = new string(' ', indentation);
            codeAt = text.Length - text.TrimStart().Length;
        }

        shift = head.Length - codeAt;
        return Aligned(text, head, text[codeAt..], isDeclaration, options);
    }

    // a line continued from the one before: as far as the first line of its statement moved.
    private static string ContinuedText(ModuleLines module, int line, int shift, bool isDeclaration, CodeFormattingOptions options)
    {
        var text = module.Lines[line];
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var leading = text.Length - text.TrimStart().Length;
        return Aligned(text, new string(' ', Math.Max(0, leading + shift)), text[leading..], isDeclaration, options);
    }

    // a line as it is to be: its head - its indentation, or its line number and what follows it - then its code, with the As of a declaration and an end-of-line
    // comment where the settings have them.
    private static string Aligned(string text, string head, string code, bool isDeclaration, CodeFormattingOptions options)
    {
        if (options.AlignDims && isDeclaration)
        {
            code = WithDimAligned(head.Length, code, options);
        }

        return WithCommentAligned(text, head + code, options);
    }

    // the first As of a declaration's line, where the column of the setting has it: a space before it at least.
    private static string WithDimAligned(int column, string code, CodeFormattingOptions options)
    {
        if (CodeWithoutComment(code) is not { } plain || DimAs().Match(plain) is not { Success: true } match)
        {
            return code;
        }

        var name = match.Groups["name"].Value;
        var gap = Math.Max(options.AlignDimColumn - 1 - column - name.Length, 1);
        return name + new string(' ', gap) + code[(match.Groups["as"].Index)..];
    }

    // an end-of-line comment where the style of the setting has it: at the column it was at, after the gap it had, after a standard gap, or in a column. A
    // space after the code at least.
    private static string WithCommentAligned(string text, string line, CodeFormattingOptions options)
    {
        if (CommentAt(text) is not { } written || CommentAt(line) is not { } now)
        {
            return line;
        }

        var gap = options.EndOfLineCommentStyle switch
        {
            EndOfLineCommentStyle.Absolute => written.Comment - now.CodeEnd,
            EndOfLineCommentStyle.SameGap => written.Comment - written.CodeEnd,
            EndOfLineCommentStyle.StandardGap => 2 * options.IndentSpaces,
            _ => options.EndOfLineCommentColumnSpaceAlignment - 1 - now.CodeEnd,
        };

        return line[..now.CodeEnd] + new string(' ', Math.Max(gap, 1)) + line[now.Comment..];
    }

    // where the code of a line ends and its end-of-line comment starts: none for a line with no code before its comment, or no comment. An apostrophe in a string
    // literal, or in a bracketed name, starts none.
    private static (int CodeEnd, int Comment)? CommentAt(string line)
    {
        var inString = false;
        var inBracket = false;
        for (var index = 0; index < line.Length; index++)
        {
            switch (line[index])
            {
                case '"' when !inBracket:
                    inString = !inString;
                    break;
                case '[' when !inString:
                    inBracket = true;
                    break;
                case ']' when !inString:
                    inBracket = false;
                    break;
                case '\'' when !inString && !inBracket:
                    var codeEnd = line[..index].TrimEnd().Length;
                    return line[..codeEnd].Trim().Length == 0 ? null : (codeEnd, index);
            }
        }

        return null;
    }

    // the code of a line without its end-of-line comment.
    private static string? CodeWithoutComment(string code) => CommentAt(code) is { } at ? code[..at.CodeEnd] : code;

    // a line that starts a declaration: Dim, Const, Static, or an access modifier that is not a procedure's, a Declare's, an Enum's, a Type's or an Event's.
    private static bool IsDeclaration(string line)
    {
        var code = Keyworded(line);
        return Declaration().IsMatch(code) && !ProcedureKeyword().IsMatch(code) && !NotAVariable().IsMatch(code);
    }

    private static int[] LevelsOf(ModuleParseResult parse, ModuleLines module, CodeFormattingOptions options)
    {
        var levels = new int[module.Lines.Length];
        var covered = new bool[module.Lines.Length];
        void Indent(int from, int to)
        {
            for (var line = Math.Max(from, 0); line <= Math.Min(to, levels.Length - 1); line++)
            {
                levels[line]++;
            }
        }

        if (parse.SyntaxTree is { } tree)
        {
            foreach (var node in tree.Descendants())
            {
                Cover(covered, node, module);
                IndentBodiesOf(node, module, options, Indent);
            }
        }

        if (options.IndentCompilerDirectives)
        {
            foreach (var (from, to) in DirectiveBodies(module))
            {
                Indent(from, to);
            }
        }

        IndentByKeywords(module, covered, levels, options);
        return levels;
    }

    // the lines of the statements and declarations the tree has: the others are indented by their keywords.
    private static void Cover(bool[] covered, SyntaxNode node, ModuleLines module)
    {
        if (node is not (StatementNode or MemberDeclarationNode or VariableDeclarationNode or ConstantDeclarationNode or DirectiveNode))
        {
            return;
        }

        var range = node.SourceLocation.Range;
        for (var line = range.Start.Line; line <= module.LastLineOf(range) && line < covered.Length; line++)
        {
            covered[line] = true;
        }
    }

    private static void IndentBodiesOf(SyntaxNode node, ModuleLines module, CodeFormattingOptions options, Action<int, int> indent)
    {
        var range = node.SourceLocation.Range;
        var start = range.Start.Line;
        var end = module.LastLineOf(range);
        switch (node)
        {
            case MemberDeclarationNode { MemberKind: MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet }
                when options.IndentEntireProcedureBody:
                indent(module.HeaderEnd(start) + 1, end - 1);
                break;

            case MemberDeclarationNode { MemberKind: MemberKind.Enum or MemberKind.UserDefinedType }:
                indent(module.HeaderEnd(start) + 1, end - 1);
                break;

            case IfBlockStatementNode block when end > start:
                Branches(module, start, end, [.. block.ElseIfBlocks.Select(StartOf), .. block.ElseBlock is { } elseBlock ? [StartOf(elseBlock)] : Array.Empty<int>()], indent);
                break;

            case SelectCaseStatementNode select when end > start:
                if (options.IndentCase)
                {
                    indent(module.HeaderEnd(start) + 1, end - 1);
                }

                int[] clauses = [.. select.CaseExpressionBlocks.Select(StartOf), .. select.CaseElseBlock is { } caseElse ? [StartOf(caseElse)] : Array.Empty<int>()];
                for (var index = 0; index < clauses.Length; index++)
                {
                    indent(module.HeaderEnd(clauses[index]) + 1, (index + 1 < clauses.Length ? clauses[index + 1] : end) - 1);
                }

                break;

            case ForStatementNode or ForEachStatementNode or DoLoopStatementNode or DoLoopUntilStatementNode or DoLoopWhileStatementNode or DoUntilLoopStatementNode
                or DoWhileLoopStatementNode or WhileWendStatementNode or WithStatementNode when end > module.HeaderEnd(start):
                indent(module.HeaderEnd(start) + 1, end - 1);
                break;
        }
    }

    private static int StartOf(SyntaxNode node) => node.SourceLocation.Range.Start.Line;

    // the body before the first branch, and each branch's: a branch's line is at the level of the block.
    private static void Branches(ModuleLines module, int start, int end, int[] branches, Action<int, int> indent)
    {
        var heads = new[] { start }.Concat(branches.Order()).ToArray();
        for (var index = 0; index < heads.Length; index++)
        {
            indent(module.HeaderEnd(heads[index]) + 1, (index + 1 < heads.Length ? heads[index + 1] : end) - 1);
        }
    }

    // what each branch of an #If guards: the lines after its #If, #ElseIf or #Else, up to the next of them or its #End If.
    private static IEnumerable<(int From, int To)> DirectiveBodies(ModuleLines module)
    {
        var open = new Stack<int>();
        for (var line = 0; line < module.Lines.Length; line++)
        {
            if (module.IsContinuation(line))
            {
                continue;
            }

            var code = module.Lines[line].TrimStart();
            if (DirectiveIf().IsMatch(code))
            {
                open.Push(line);
            }
            else if (DirectiveBranch().IsMatch(code) && open.Count > 0)
            {
                yield return (open.Pop() + 1, line - 1);
                open.Push(line);
            }
            else if (DirectiveEnd().IsMatch(code) && open.Count > 0)
            {
                yield return (open.Pop() + 1, line - 1);
            }
        }
    }

    // a run of lines the tree has no statement of is indented by its keywords, from the level it is at: an opener indents what follows it, a closer ends that.
    private static void IndentByKeywords(ModuleLines module, bool[] covered, int[] levels, CodeFormattingOptions options)
    {
        var depth = 0;
        for (var line = 0; line < module.Lines.Length; line++)
        {
            if (covered[line])
            {
                depth = 0;
                continue;
            }

            var code = Keyworded(module.Lines[line]);
            if (code.Length == 0 || code.StartsWith('\'') || code.StartsWith('#') || module.IsContinuation(line))
            {
                levels[line] += Math.Max(depth, 0);
                continue;
            }

            var (closes, opens) = KeywordsOf(code, options);
            depth = Math.Max(depth - closes, 0);
            levels[line] += depth;
            depth += opens;
        }
    }

    // the code of a line, its line number and an end-of-line comment left out.
    private static string Keyworded(string line)
    {
        var code = LineNumber().Match(line) is { Success: true } number ? line[number.Length..] : line.TrimStart();
        return code.TrimEnd();
    }

    // how many levels a line ends before it, and how many it starts after it.
    private static (int Closes, int Opens) KeywordsOf(string code, CodeFormattingOptions options)
    {
        if (BranchKeyword().IsMatch(code))
        {
            return (1, 1);
        }

        if (CaseKeyword().IsMatch(code))
        {
            return (1, 1);
        }

        if (SelectKeyword().IsMatch(code))
        {
            // the first Case ends a body that is not there: the Select starts one for it, and one more for the Case lines when they are indented.
            return (0, options.IndentCase ? 2 : 1);
        }

        if (EndSelectKeyword().IsMatch(code))
        {
            return (options.IndentCase ? 2 : 1, 0);
        }

        if (CloserKeyword().IsMatch(code))
        {
            return (1, 0);
        }

        if (OpenerKeyword().IsMatch(code) || (ProcedureKeyword().IsMatch(code) && options.IndentEntireProcedureBody))
        {
            return (0, 1);
        }

        return (0, 0);
    }

    // the lines of a module, and which of them continue the one before.
    private sealed class ModuleLines(string[] lines)
    {
        private readonly bool[] _continued = [.. lines.Select(IsContinued)];

        public string[] Lines { get; } = lines;

        public bool IsContinuation(int line) => line > 0 && _continued[line - 1];

        // the last line of a statement that starts at a line: the first that is not continued.
        public int HeaderEnd(int line)
        {
            while (line < _continued.Length - 1 && _continued[line])
            {
                line++;
            }

            return line;
        }

        // the line a node ends on: one that ends at the start of a line ends on the line before it.
        public int LastLineOf(Model.Source.SourceRange range)
            => range.End.Character == 0 && range.End.Line > range.Start.Line ? range.End.Line - 1 : range.End.Line;

        private static bool IsContinued(string line)
        {
            var text = line.TrimEnd();
            return text == "_" || text.EndsWith(" _", StringComparison.Ordinal) || text.EndsWith("\t_", StringComparison.Ordinal);
        }
    }

    [GeneratedRegex(@"^\s*(?<number>(?:-?\d+|&H[0-9A-F]{1,8}):?)(?:\s+|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LineNumber();

    [GeneratedRegex(@"^\s*(?:'|Rem(?:\s|$))", RegexOptions.IgnoreCase)]
    private static partial Regex CommentLine();

    [GeneratedRegex(@"^Debug\.Print\b", RegexOptions.IgnoreCase)]
    private static partial Regex DebugPrint();

    [GeneratedRegex(@"^Debug\.Assert\b", RegexOptions.IgnoreCase)]
    private static partial Regex DebugAssert();

    [GeneratedRegex(@"^Stop\s*(?:$|:|')", RegexOptions.IgnoreCase)]
    private static partial Regex StopStatement();

    [GeneratedRegex(@"^(Dim|Const|Static|Public|Private)\s", RegexOptions.IgnoreCase)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^((Public|Private|Friend)\s+)?(Declare|Enum|Type|Event|Implements)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotAVariable();

    // what a declaration's line names before its first As, and that As.
    [GeneratedRegex(@"^(?<name>.*?\S)\s+(?<as>As\s)", RegexOptions.IgnoreCase)]
    private static partial Regex DimAs();

    [GeneratedRegex(@"^#If\s.+\sThen\b", RegexOptions.IgnoreCase)]
    private static partial Regex DirectiveIf();

    [GeneratedRegex(@"^#(ElseIf\s|Else\b)", RegexOptions.IgnoreCase)]
    private static partial Regex DirectiveBranch();

    [GeneratedRegex(@"^#End\s*If\b", RegexOptions.IgnoreCase)]
    private static partial Regex DirectiveEnd();

    [GeneratedRegex(@"^(ElseIf\s.*\sThen|Else:?)$", RegexOptions.IgnoreCase)]
    private static partial Regex BranchKeyword();

    [GeneratedRegex(@"^Case\s", RegexOptions.IgnoreCase)]
    private static partial Regex CaseKeyword();

    [GeneratedRegex(@"^Select\s+Case\s", RegexOptions.IgnoreCase)]
    private static partial Regex SelectKeyword();

    [GeneratedRegex(@"^End\s+Select\b", RegexOptions.IgnoreCase)]
    private static partial Regex EndSelectKeyword();

    [GeneratedRegex(@"^(End\s+(If|With|Sub|Function|Property|Enum|Type)\b|Next\b|Loop\b|Wend\b)", RegexOptions.IgnoreCase)]
    private static partial Regex CloserKeyword();

    [GeneratedRegex(@"^(If\s.*\sThen|With\s|For\s|Do\b|While\s|((Public|Private)\s+)?(Enum|Type)\s)", RegexOptions.IgnoreCase)]
    private static partial Regex OpenerKeyword();

    [GeneratedRegex(@"^((Public|Private|Friend)\s+)?(Static\s+)?(Sub|Function|Property\s+(Get|Let|Set))\s", RegexOptions.IgnoreCase)]
    private static partial Regex ProcedureKeyword();
}
