using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Statements;
using System.Text.RegularExpressions;

namespace RDCore.SDK.Formatting;

/// <summary>
/// An edit of the whitespace of a line: the <paramref name="Length"/> characters at <paramref name="Character"/> of line <paramref name="Line"/> are replaced by
/// <paramref name="NewText"/>. What is replaced is a run of whitespace, or none at all, and what replaces it is whitespace, or nothing.
/// </summary>
/// <param name="Line">The line, from 0.</param>
/// <param name="Character">Where what is replaced starts, in UTF-16 code units from the start of the line.</param>
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
/// A level is <see cref="CodeFormattingOptions.IndentSpaces"/> spaces: the code of a module is indented with spaces, as VBA code is, and a tab is replaced by
/// the spaces up to its tab stop. A block's opener and its closer, and the <c>ElseIf</c>, <c>Else</c> and <c>Case</c> lines between them, are at the level of
/// the block, and so is a line that has its closer after other code (<c>x = 1: End If</c>); what they guard is a level further in. A procedure's body is
/// indented when <see cref="CodeFormattingOptions.IndentEntireProcedureBody"/> says so, the <c>Case</c> lines of a <c>Select Case</c> when
/// <see cref="CodeFormattingOptions.IndentCase"/> does, and what an <c>#If</c> guards when <see cref="CodeFormattingOptions.IndentCompilerDirectives"/> does.
/// A procedure whose header is written once in each branch of an <c>#If</c> has the <c>#If</c> block as its header: its lines are at the procedure's level,
/// and the body after the <c>#End If</c> is indented once.
/// </para>
/// <para>
/// A line the syntax tree has no statement of - a branch of an <c>#If</c> that is not compiled, code the parser could not build as you type - is indented by its
/// keywords instead, from the level of where it is: a block's opener indents the lines after it, and its closer ends that. A block the parser could not find
/// the closer of is open to its last line. The indenter never refuses to indent.
/// </para>
/// <para>
/// A line number stays at the start of its line, and so does a line label, as the VBE has it: the code after either is where the line's level has it, a space
/// after the number or the label at least. A line continued from the one before (<c>_</c>) moves with the first line of its statement, as far as it does. A
/// comment alone on its line is at the level of the code around it; a blank line is left as it is. The header of a class or a form module
/// (<c>VERSION</c>, <c>BEGIN</c> ... <c>END</c>) and its <c>Attribute</c> lines are left as they are written.
/// </para>
/// </remarks>
public static partial class ModuleIndenter
{
    /// <summary>
    /// The edits that indent the lines of a module, from its first line to its last, or of the lines from <paramref name="firstLine"/> to
    /// <paramref name="lastLine"/>; none for a line that is indented as it should be.
    /// </summary>
    /// <param name="parse">The module, as it was parsed from <paramref name="text"/>.</param>
    /// <param name="text">The text of the module. A line ends with a carriage return, a line feed, or both, as the Language Server Protocol counts them.</param>
    /// <param name="options">How code is indented.</param>
    /// <param name="firstLine">The first line to indent, from 0. A line that continues the one before it widens the range to the first line of its statement.</param>
    /// <param name="lastLine">The last line to indent. A line that is continued widens the range to the last line of its statement.</param>
    /// <returns>
    /// The edits, in the order of the text: by line, then by character, none of them overlapping, so that each can be made where the text as it is has it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="parse"/>, <paramref name="text"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<IndentationEdit> Indent(ModuleParseResult parse, string text, CodeFormattingOptions options, int firstLine = 0, int lastLine = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        var lines = LinesOf(text);
        var module = new ModuleLines(lines);
        var asWritten = WrittenAsTheyAre(parse, module);
        var labels = LabelsOf(parse, module);
        var levels = LevelsOf(parse, module, options);

        // a range never splits a statement: its continued lines move with its first.
        while (firstLine > 0 && firstLine < lines.Length && module.IsContinuation(firstLine))
        {
            firstLine--;
        }

        if (lastLine >= 0 && lastLine < lines.Length)
        {
            lastLine = module.HeaderEnd(lastLine);
        }

        var edits = new List<IndentationEdit>();
        var shift = 0;
        for (var line = 0; line < lines.Length; line++)
        {
            string wanted;
            if (asWritten[line])
            {
                (wanted, shift) = (lines[line], 0);
            }
            else
            {
                wanted = module.IsContinuation(line) ? Continued(lines[line], shift, options) : Started(module, line, levels[line], labels, options, out shift);
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
    /// <param name="edits">The edits, none of them overlapping, each within its line; in any order.</param>
    /// <returns>The text with the edits made, its line ends as they were.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="edits"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An edit is of a line the text does not have, or of characters its line does not have.</exception>
    public static string Apply(string text, IReadOnlyList<IndentationEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(edits);
        var parts = LineEnd().Split(text);

        // the edits of a line, from its last to its first: each is where the text before it puts it.
        foreach (var edit in edits.OrderByDescending(edit => edit.Line).ThenByDescending(edit => edit.Character))
        {
            ArgumentOutOfRangeException.ThrowIfNegative(edit.Line);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(edit.Line * 2, parts.Length);
            var line = parts[edit.Line * 2];
            parts[edit.Line * 2] = line[..edit.Character] + edit.NewText + line[(edit.Character + edit.Length)..];
        }

        return string.Concat(parts);
    }

    // the lines of the text, without what ends them.
    private static string[] LinesOf(string text) => [.. LineEnd().Split(text).Where((_, index) => index % 2 == 0)];

    // what ends a line, kept by a split: a carriage return and a line feed, or either alone.
    [GeneratedRegex(@"(\r\n|\r|\n)")]
    private static partial Regex LineEnd();

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

    // the first line of a statement: indented to its level, a line number or a label kept at the start of it, then the code a space after it at least. How far
    // its code moved is what its continuations move.
    private static string Started(ModuleLines module, int line, int level, IReadOnlyDictionary<int, int> labels, CodeFormattingOptions options, out int shift)
    {
        shift = 0;
        var text = module.Lines[line];
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var indentation = level * options.IndentSpaces;
        string prefix;
        int codeAt;
        if (LineNumber().Match(text) is { Success: true } number)
        {
            (prefix, codeAt) = (number.Groups["number"].Value, number.Length);
        }
        else if (labels.TryGetValue(line, out var labelEnd))
        {
            var labelAt = text.Length - text.TrimStart().Length;
            (prefix, codeAt) = (text[labelAt..labelEnd], labelEnd + (text[labelEnd..].Length - text[labelEnd..].TrimStart().Length));
        }
        else
        {
            (prefix, codeAt) = (string.Empty, text.Length - text.TrimStart().Length);
        }

        var code = text[codeAt..];
        var head = prefix.Length == 0 ? new string(' ', indentation)
            : code.Length == 0 ? prefix
            : prefix + new string(' ', Math.Max(indentation, prefix.Length + 1) - prefix.Length);
        shift = head.Length - Width(text[..codeAt], options);
        return head + code;
    }

    // a line continued from the one before: as far as the first line of its statement moved.
    private static string Continued(string text, int shift, CodeFormattingOptions options)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var leading = text.Length - text.TrimStart().Length;
        return new string(' ', Math.Max(0, Width(text[..leading], options) + shift)) + text[leading..];
    }

    // how many columns text takes: a tab to its next tab stop, every level of spaces.
    private static int Width(string text, CodeFormattingOptions options)
    {
        var width = 0;
        foreach (var character in text)
        {
            width = character == '\t' ? (width / options.IndentSpaces + 1) * options.IndentSpaces : width + 1;
        }

        return width;
    }

    // the lines the indenter leaves as they are written: a module's header, and its Attribute lines.
    private static bool[] WrittenAsTheyAre(ModuleParseResult parse, ModuleLines module)
    {
        var asWritten = new bool[module.Lines.Length];
        void Keep(int from, int to)
        {
            for (var line = Math.Max(from, 0); line <= to && line < asWritten.Length; line++)
            {
                asWritten[line] = true;
            }
        }

        foreach (var header in parse.Trivia.OfType<ModuleHeaderTriviaNode>())
        {
            Keep(header.SourceLocation.Range.Start.Line, module.LastLineOf(header.SourceLocation.Range));
        }

        if (parse.SyntaxTree is { } tree)
        {
            foreach (var attribute in tree.Descendants().OfType<AttributeDirectiveNode>())
            {
                var start = attribute.SourceLocation.Range.Start.Line;
                Keep(start, module.HeaderEnd(start));
            }
        }

        return asWritten;
    }

    // the lines that start with a label, and where the label (and its colon) ends.
    private static Dictionary<int, int> LabelsOf(ModuleParseResult parse, ModuleLines module)
    {
        var labels = new Dictionary<int, int>();
        if (parse.SyntaxTree is not { } tree)
        {
            return labels;
        }

        foreach (var label in tree.Descendants().OfType<LineLabelNode>().Where(label => label is not LineNumberNode))
        {
            var range = label.SourceLocation.Range;
            var text = module.Lines[range.Start.Line];
            if (range.Start.Line == range.End.Line && range.Start.Character == text.Length - text.TrimStart().Length && range.End.Character <= text.Length)
            {
                labels[range.Start.Line] = range.End.Character;
            }
        }

        return labels;
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

        var blocks = DirectiveBlocks(module);
        var splitHeaders = new Dictionary<int, DirectiveBlock>();
        if (parse.SyntaxTree is { } tree)
        {
            foreach (var (member, block) in SplitHeadersOf(tree, module, blocks))
            {
                splitHeaders[member] = block;
                for (var line = block.If; line <= block.End; line++)
                {
                    covered[line] = true;
                }
            }

            foreach (var node in tree.Descendants())
            {
                Cover(covered, node, module);
                IndentBodiesOf(node, module, options, splitHeaders, Indent);
            }
        }

        if (options.IndentCompilerDirectives)
        {
            foreach (var block in blocks.Where(block => !splitHeaders.ContainsValue(block)))
            {
                int[] heads = [block.If, .. block.Branches, block.End];
                for (var index = 0; index + 1 < heads.Length; index++)
                {
                    Indent(heads[index] + 1, heads[index + 1] - 1);
                }
            }
        }

        IndentByKeywords(module, covered, levels, options);
        return levels;
    }

    // the lines the statements and declarations of the tree start on: the others are indented by their keywords. A block covers the line of its opener, and
    // what is in it covers its own lines: a body the parser built nothing of is indented by its keywords.
    private static void Cover(bool[] covered, SyntaxNode node, ModuleLines module)
    {
        if (node is not (StatementNode or MemberDeclarationNode or VariableDeclarationNode or ConstantDeclarationNode or DirectiveNode))
        {
            return;
        }

        var start = node.SourceLocation.Range.Start.Line;
        for (var line = start; line <= module.HeaderEnd(start) && line < covered.Length; line++)
        {
            covered[line] = true;
        }
    }

    private static void IndentBodiesOf(SyntaxNode node, ModuleLines module, CodeFormattingOptions options, IReadOnlyDictionary<int, DirectiveBlock> splitHeaders,
        Action<int, int> indent)
    {
        var range = node.SourceLocation.Range;
        var start = range.Start.Line;
        var end = module.LastLineOf(range);
        var bodyEnd = BodyEnd(node, module, start, end);
        switch (node)
        {
            case MemberDeclarationNode { MemberKind: MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet }
                when options.IndentEntireProcedureBody:
                indent((splitHeaders.TryGetValue(start, out var header) ? header.End : module.HeaderEnd(start)) + 1, bodyEnd);
                break;

            case MemberDeclarationNode { MemberKind: MemberKind.Enum or MemberKind.UserDefinedType }:
                indent(module.HeaderEnd(start) + 1, bodyEnd);
                break;

            case IfBlockStatementNode block when end > start:
                Branches(module, start, bodyEnd, [.. block.ElseIfBlocks.Select(StartOf), .. block.ElseBlock is { } elseBlock ? [StartOf(elseBlock)] : Array.Empty<int>()], indent);
                break;

            case SelectCaseStatementNode select when end > start:
                if (options.IndentCase)
                {
                    indent(module.HeaderEnd(start) + 1, bodyEnd);
                }

                int[] clauses = [.. select.CaseExpressionBlocks.Select(StartOf), .. select.CaseElseBlock is { } caseElse ? [StartOf(caseElse)] : Array.Empty<int>()];
                for (var index = 0; index < clauses.Length; index++)
                {
                    indent(module.HeaderEnd(clauses[index]) + 1, index + 1 < clauses.Length ? clauses[index + 1] - 1 : bodyEnd);
                }

                break;

            case ForStatementNode or ForEachStatementNode or DoLoopStatementNode or DoLoopUntilStatementNode or DoLoopWhileStatementNode or DoUntilLoopStatementNode
                or DoWhileLoopStatementNode or WhileWendStatementNode or WithStatementNode when end > module.HeaderEnd(start):
                indent(module.HeaderEnd(start) + 1, bodyEnd);
                break;
        }
    }

    // the last line of a block's body: the line before its closer, which is at the level of the block wherever it is on its line; the last line of the block
    // when the parser found no closer, as it is typed.
    private static int BodyEnd(SyntaxNode node, ModuleLines module, int start, int end)
    {
        string? closer = node switch
        {
            MemberDeclarationNode { MemberKind: MemberKind.Procedure } => "End Sub",
            MemberDeclarationNode { MemberKind: MemberKind.Function } => "End Function",
            MemberDeclarationNode { MemberKind: MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet } => "End Property",
            MemberDeclarationNode { MemberKind: MemberKind.Enum } => "End Enum",
            MemberDeclarationNode { MemberKind: MemberKind.UserDefinedType } => "End Type",
            IfBlockStatementNode => "End If",
            SelectCaseStatementNode => "End Select",
            ForStatementNode or ForEachStatementNode => "Next",
            DoLoopStatementNode or DoLoopUntilStatementNode or DoLoopWhileStatementNode or DoUntilLoopStatementNode or DoWhileLoopStatementNode => "Loop",
            WhileWendStatementNode => "Wend",
            WithStatementNode => "End With",
            _ => null,
        };

        return closer is not null && end > start && HasStatementStartingWith(Keyworded(module.Lines[end]), closer) ? end - 1 : end;
    }

    // whether a statement of a line starts with the words of a keyword: the first, or one after a colon that is not in a string literal.
    private static bool HasStatementStartingWith(string code, string words)
    {
        var inString = false;
        for (var index = 0; index < code.Length; index++)
        {
            if (code[index] == '"')
            {
                inString = !inString;
            }
            else if (!inString && (index == 0 || code[index - 1] == ':') && StartsWithWords(code, index, words))
            {
                return true;
            }
        }

        return false;
    }

    // whether code starts at an index with the words of a keyword, whitespace before and between them, and no more of a name after them.
    private static bool StartsWithWords(string code, int at, string words)
    {
        foreach (var word in words.Split(' '))
        {
            while (at < code.Length && char.IsWhiteSpace(code[at]))
            {
                at++;
            }

            if (string.Compare(code, at, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return false;
            }

            at += word.Length;
        }

        return at == code.Length || !(char.IsLetterOrDigit(code[at]) || code[at] == '_');
    }

    private static int StartOf(SyntaxNode node) => node.SourceLocation.Range.Start.Line;

    // the body before the first branch, and each branch's: a branch's line is at the level of the block.
    private static void Branches(ModuleLines module, int start, int bodyEnd, int[] branches, Action<int, int> indent)
    {
        var heads = new[] { start }.Concat(branches.Order()).ToArray();
        for (var index = 0; index < heads.Length; index++)
        {
            indent(module.HeaderEnd(heads[index]) + 1, index + 1 < heads.Length ? heads[index + 1] - 1 : bodyEnd);
        }
    }

    // an #If block: the line of its #If, those of its #ElseIf and #Else, and that of its #End If.
    private sealed record class DirectiveBlock(int If, IReadOnlyList<int> Branches, int End);

    private static List<DirectiveBlock> DirectiveBlocks(ModuleLines module)
    {
        var blocks = new List<DirectiveBlock>();
        var open = new Stack<(int If, List<int> Branches)>();
        for (var line = 0; line < module.Lines.Length; line++)
        {
            if (module.IsContinuation(line))
            {
                continue;
            }

            var code = module.Lines[line].TrimStart();
            if (DirectiveIf().IsMatch(code))
            {
                open.Push((line, []));
            }
            else if (DirectiveBranch().IsMatch(code) && open.Count > 0)
            {
                open.Peek().Branches.Add(line);
            }
            else if (DirectiveEnd().IsMatch(code) && open.Count > 0)
            {
                var (start, branches) = open.Pop();
                blocks.Add(new DirectiveBlock(start, branches, line));
            }
        }

        // an #If with no #End If yet, as it is typed, is open to the last line.
        while (open.Count > 0)
        {
            var (start, branches) = open.Pop();
            blocks.Add(new DirectiveBlock(start, branches, module.Lines.Length));
        }

        return blocks;
    }

    // the procedures whose header is written in the branches of an #If block, around one body: the line each starts on, and its block. A procedure that starts
    // in an #If block and ends after its #End If has its header there.
    private static IEnumerable<(int Member, DirectiveBlock Block)> SplitHeadersOf(ModuleNode tree, ModuleLines module, IReadOnlyList<DirectiveBlock> blocks)
    {
        foreach (var member in tree.Descendants().OfType<MemberDeclarationNode>()
            .Where(member => member.MemberKind is MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet))
        {
            var range = member.SourceLocation.Range;
            var (start, end) = (range.Start.Line, module.LastLineOf(range));
            if (blocks.Where(block => block.If < start && start < block.End && end > block.End).MaxBy(block => block.If) is { } header)
            {
                yield return (start, header);
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

    // the code of a line, its line number, an end-of-line comment and the whitespace around it left out. A comment alone on its line is kept.
    private static string Keyworded(string line)
    {
        var code = LineNumber().Match(line) is { Success: true } number ? line[number.Length..] : line.TrimStart();
        return code.StartsWith('\'') ? code.TrimEnd() : code[..CodeEnd(code)].TrimEnd();
    }

    // where the code of a line ends: at the apostrophe that starts its end-of-line comment, or at its end. An apostrophe in a string literal, or in a bracketed
    // name, starts no comment.
    private static int CodeEnd(string code)
    {
        var inString = false;
        var inBracket = false;
        for (var index = 0; index < code.Length; index++)
        {
            switch (code[index])
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
                    return index;
            }
        }

        return code.Length;
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
