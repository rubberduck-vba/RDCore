using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Statements;
using System.Text.RegularExpressions;

namespace RDCore.SDK.Formatting;

/// <summary>
/// An edit of the whitespace of a module: what is from <paramref name="Character"/> of line <paramref name="Line"/> to <paramref name="EndCharacter"/> of line
/// <paramref name="EndLine"/> is replaced by <paramref name="NewText"/>. What is replaced is whitespace, or nothing, and so is what replaces it: a run of it
/// within a line, or the empty lines between two procedures, from the end of the line before them.
/// </summary>
/// <param name="Line">The line where what is replaced starts, from 0.</param>
/// <param name="Character">Where what is replaced starts, in UTF-16 code units from the start of its line.</param>
/// <param name="EndLine">The line where what is replaced ends.</param>
/// <param name="EndCharacter">Where what is replaced ends, in UTF-16 code units from the start of its line: the character after it.</param>
/// <param name="NewText">What replaces it.</param>
public readonly record struct IndentationEdit(int Line, int Character, int EndLine, int EndCharacter, string NewText)
{
    /// <summary>
    /// An edit within a line: the <paramref name="length"/> characters at <paramref name="character"/> of line <paramref name="line"/> are replaced by
    /// <paramref name="newText"/>.
    /// </summary>
    /// <param name="line">The line, from 0.</param>
    /// <param name="character">Where what is replaced starts.</param>
    /// <param name="length">How many characters are replaced.</param>
    /// <param name="newText">What replaces them.</param>
    public IndentationEdit(int line, int character, int length, string newText)
        : this(line, character, line, character + length, newText)
    {
    }
}

/// <summary>
/// Indents the lines of a module by what encloses them in its syntax tree (<see cref="CodeFormattingOptions"/>): a line is indented one level for each body it
/// is in - of a procedure, an <c>Enum</c> or a <c>Type</c>, a branch of an <c>If</c> block, a <c>Case</c>, a loop, a <c>With</c> block, a branch of an
/// <c>#If</c> directive.
/// </summary>
/// <remarks>
/// <para>
/// A level is <see cref="CodeFormattingOptions.IndentSpaces"/> spaces: the code of a module is indented with spaces, as VBA code is. A block's opener and its
/// closer, and the <c>ElseIf</c>, <c>Else</c> and <c>Case</c> lines between them, are at the level of the block, and so is a line that has its closer after
/// other code (<c>x = 1: End If</c>); what they guard is a level further in. A procedure's body is indented when
/// <see cref="CodeFormattingOptions.IndentEntireProcedureBody"/> says so, the <c>Case</c> lines of a <c>Select Case</c> when
/// <see cref="CodeFormattingOptions.IndentCase"/> does, and what an <c>#If</c> guards when <see cref="CodeFormattingOptions.IndentCompilerDirectives"/> does.
/// A procedure whose header is written once in each branch of an <c>#If</c> has the <c>#If</c> block as its header: its lines are at the procedure's level,
/// and the body after the <c>#End If</c> is indented once.
/// </para>
/// <para>
/// A line the syntax tree has no statement of - code the parser could not build as you type - is indented by its keywords instead, from the level of where it
/// is: a block's opener indents the lines after it, and its closer ends that. A block the parser could not find the closer of is open to its last line. The
/// indenter never refuses to indent.
/// </para>
/// <para>
/// A line number stays at the start of its line, and so does a line label, as the VBE has it: the code after either is where the line's level has it, a space
/// after the number or the label at least. A line continued from the one before (<c>_</c>) lines up with the lines of its statement before it. A comment
/// alone on its line is at the level of the code around it. The header of a class or a form module (<c>VERSION</c>, <c>BEGIN</c> ... <c>END</c>) and its
/// <c>Attribute</c> lines are left as they are written.
/// </para>
/// <para>
/// Between procedures - a <c>Sub</c>, a <c>Function</c>, a <c>Property</c>, an <c>Enum</c> or a <c>Type</c>, with the comments right above it - there are
/// <see cref="CodeFormattingOptions.LinesBetweenProcedures"/> empty lines when <see cref="CodeFormattingOptions.VerticallySpaceProcedures"/> says so, and as many
/// after what is above the first; none between the accessors of a property when <see cref="CodeFormattingOptions.GroupRelatedProperties"/> groups them.
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
        var blocks = DirectiveBlocks(module);
        var splitHeaders = parse.SyntaxTree is { } tree ? SplitHeadersOf(tree, module, blocks).ToDictionary() : [];
        var asWritten = WrittenAsTheyAre(parse, module);
        var labels = LabelsOf(parse, module);
        var levels = LevelsOf(parse, module, options, blocks, splitHeaders);
        var firstBlocks = FirstBlocksAtColumn1(parse, module, options, splitHeaders);
        var members = EnumOrTypeMembers(parse, module);

        // a range never splits a statement: its lines are aligned together.
        while (firstLine > 0 && firstLine < lines.Length && module.IsContinuation(firstLine))
        {
            firstLine--;
        }

        if (lastLine >= 0 && lastLine < lines.Length)
        {
            lastLine = module.HeaderEnd(lastLine);
        }

        // the empty lines between procedures, as many as the setting says: those of a gap that has more or fewer are written again, from the end of the line
        // before them, with its line end.
        var edits = new List<IndentationEdit>();
        var spaced = new bool[lines.Length];
        if (options.VerticallySpaceProcedures && parse.SyntaxTree is { } procedures)
        {
            var ends = LineEnd().Split(text).Where((_, index) => index % 2 == 1).ToArray();
            foreach (var gap in GapsBetweenProcedures(procedures, module, options, splitHeaders, asWritten))
            {
                if (gap.Block - gap.Above - 1 == gap.EmptyLines || gap.Above > lastLine || gap.Block < firstLine)
                {
                    continue;
                }

                edits.Add(new IndentationEdit(gap.Above, lines[gap.Above].Length, gap.Block, 0, string.Concat(Enumerable.Repeat(ends[gap.Above], gap.EmptyLines + 1))));
                for (var line = gap.Above + 1; line < gap.Block; line++)
                {
                    spaced[line] = true;
                }
            }
        }

        Continuation? statement = null;
        var keep = false;
        for (var line = 0; line < lines.Length; line++)
        {
            var next = line + 1 < lines.Length ? lines[line + 1] : null;
            string wanted;
            if (module.IsContinuation(line) && statement is not null)
            {
                wanted = keep ? lines[line] : statement.Place(lines[line], next);
            }
            else
            {
                var isDeclaration = IsDeclaration(lines[line]);
                var level = firstBlocks[line] || IsForcedToColumn1(lines[line], options) ? 0 : levels[line];
                var column = 0;
                keep = asWritten[line];
                wanted = keep ? lines[line] : StartedText(module, line, level, labels, isDeclaration, options, out column);
                statement = new Continuation(options, column, IsComment(lines[line]), members[line], isDeclaration);
                statement.Saw(wanted, next);
            }

            if (line >= firstLine && line <= lastLine && !spaced[line])
            {
                edits.AddRange(WhitespaceEdits(line, lines[line], wanted));
            }
        }

        return [.. edits.OrderBy(edit => edit.Line).ThenBy(edit => edit.Character)];
    }

    // the empty lines between two procedures, or before the first after what is above it: from the line above them to the first line of the procedure, its
    // comments right above it, which are of it. A procedure is a Sub, a Function, a Property, an Enum or a Type; what is in a procedure is not spaced, nor what
    // follows a directive that opens a branch, the module's header or its attributes.
    private readonly record struct Gap(int Above, int Block, int EmptyLines);

    private static IEnumerable<Gap> GapsBetweenProcedures(ModuleNode tree, ModuleLines module, CodeFormattingOptions options, IReadOnlyDictionary<int, DirectiveBlock> splitHeaders,
        bool[] asWritten)
    {
        var procedures = tree.Descendants().OfType<MemberDeclarationNode>().Where(member => member.MemberKind is MemberKind.Procedure or MemberKind.Function
            or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet or MemberKind.Enum or MemberKind.UserDefinedType).ToList();
        var endingOn = new Dictionary<int, MemberDeclarationNode>();
        foreach (var procedure in procedures)
        {
            endingOn.TryAdd(module.LastLineOf(procedure.SourceLocation.Range), procedure);
        }

        var emptyLines = Math.Clamp(options.LinesBetweenProcedures, 0, CodeFormattingOptions.MaximumLinesBetweenProcedures);
        foreach (var procedure in procedures)
        {
            var start = procedure.SourceLocation.Range.Start.Line;
            var block = splitHeaders.TryGetValue(start, out var header) ? header.If : start;
            for (var line = block - 1; line >= 0; line--)
            {
                var first = line;
                while (first > 0 && module.IsContinuation(first))
                {
                    first--;
                }

                if (!IsComment(module.Lines[first]))
                {
                    break;
                }

                (block, line) = (first, first);
            }

            var above = block - 1;
            while (above >= 0 && string.IsNullOrWhiteSpace(module.Lines[above]))
            {
                above--;
            }

            var code = above < 0 ? string.Empty : module.Lines[above].TrimStart();
            if (above < 0 || asWritten[above] || DirectiveIf().IsMatch(code) || DirectiveBranch().IsMatch(code))
            {
                continue;
            }

            // the accessors of a property together, when the setting groups them.
            var grouped = options.GroupRelatedProperties && IsProperty(procedure) && endingOn.TryGetValue(above, out var before) && IsProperty(before)
                && string.Equals(before.Name, procedure.Name, StringComparison.OrdinalIgnoreCase);
            yield return new Gap(above, block, grouped ? 0 : emptyLines);
        }
    }

    private static bool IsProperty(MemberDeclarationNode member) => member.MemberKind is MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;

    /// <summary>
    /// The text of a module with indentation edits made to it (<see cref="Indent"/>).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="edits">The edits, none of them overlapping; in any order.</param>
    /// <returns>The text with the edits made, the line ends it keeps as they were.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="edits"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An edit is of a line the text does not have, or of characters its line does not have.</exception>
    public static string Apply(string text, IReadOnlyList<IndentationEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(edits);
        var parts = LineEnd().Split(text);

        // where a position is in the text: the lines before it, with what ends them, and its character in its line.
        int OffsetOf(int line, int character)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(line);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line * 2, parts.Length);
            ArgumentOutOfRangeException.ThrowIfNegative(character);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(character, parts[line * 2].Length);
            return parts.Take(line * 2).Sum(part => part.Length) + character;
        }

        // the edits from the last to the first: each is where the text before it puts it.
        var edited = new System.Text.StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(edit => edit.Line).ThenByDescending(edit => edit.Character))
        {
            var (start, end) = (OffsetOf(edit.Line, edit.Character), OffsetOf(edit.EndLine, edit.EndCharacter));
            ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
            edited.Remove(start, end - start).Insert(start, edit.NewText);
        }

        return edited.ToString();
    }

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

    // the lines of the text, without what ends them.
    private static string[] LinesOf(string text) => [.. LineEnd().Split(text).Where((_, index) => index % 2 == 0)];

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
    private static bool[] FirstBlocksAtColumn1(ModuleParseResult parse, ModuleLines module, CodeFormattingOptions options, IReadOnlyDictionary<int, DirectiveBlock> splitHeaders)
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

            var headerEnd = splitHeaders.TryGetValue(range.Start.Line, out var header) ? header.End : module.HeaderEnd(range.Start.Line);
            for (var line = headerEnd + 1; line < end; line++)
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

    // the first line of a statement: indented to its level, a line number or a label kept at the start of it, its declaration and its end-of-line comment
    // aligned. The column its code starts at is the statement's.
    private static string StartedText(ModuleLines module, int line, int level, IReadOnlyDictionary<int, int> labels, bool isDeclaration, CodeFormattingOptions options,
        out int column)
    {
        var text = module.Lines[line];
        var indentation = level * options.IndentSpaces;
        column = indentation;
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

        // a line number or a label in column 1, and the code where the level has it: one space after it at least, and nothing after it when there is no code.
        string prefix;
        int codeAt;
        if (LineNumber().Match(text) is { Success: true } number)
        {
            (prefix, codeAt) = (number.Groups["number"].Value, number.Length);
        }
        else if (labels.TryGetValue(line, out var labelEnd))
        {
            var labelAt = text.Length - text.TrimStart().Length;
            (prefix, codeAt) = (text[labelAt..labelEnd], text.Length - text[labelEnd..].TrimStart().Length);
        }
        else
        {
            (prefix, codeAt) = (string.Empty, text.Length - text.TrimStart().Length);
        }

        var head = prefix.Length == 0 ? new string(' ', indentation)
            : codeAt == text.Length ? prefix
            : prefix + new string(' ', Math.Max(indentation, prefix.Length + 1) - prefix.Length);
        column = head.Length;
        return Aligned(text, head, text[codeAt..], isDeclaration, options);
    }

    // a line as it is to be: its head - its indentation, or its line number and what follows it - then its code, with the As of a declaration and an end-of-line
    // comment where the settings have them. A declaration's line that starts with its As has it in the column of the setting.
    private static string Aligned(string text, string head, string code, bool isDeclaration, CodeFormattingOptions options)
    {
        if (options.AlignDims && isDeclaration)
        {
            if (LeadingAs().IsMatch(code))
            {
                head = new string(' ', Math.Max(options.AlignDimColumn - 1, head.Length));
            }
            else
            {
                code = WithDimAligned(head.Length, code, options);
            }
        }

        return WithCommentAligned(text, head + code, options);
    }

    // what the lines inside an Enum or a Type are: their members, whose continued lines are where they start.
    private static bool[] EnumOrTypeMembers(ModuleParseResult parse, ModuleLines module)
    {
        var members = new bool[module.Lines.Length];
        if (parse.SyntaxTree is not { } tree)
        {
            return members;
        }

        foreach (var member in tree.Descendants().OfType<MemberDeclarationNode>().Where(member => member.MemberKind is MemberKind.Enum or MemberKind.UserDefinedType))
        {
            var range = member.SourceLocation.Range;
            for (var line = module.HeaderEnd(range.Start.Line) + 1; line < module.LastLineOf(range) && line < members.Length; line++)
            {
                members[line] = true;
            }
        }

        return members;
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

    private static int[] LevelsOf(ModuleParseResult parse, ModuleLines module, CodeFormattingOptions options, IReadOnlyList<DirectiveBlock> blocks,
        IReadOnlyDictionary<int, DirectiveBlock> splitHeaders)
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
            foreach (var block in splitHeaders.Values)
            {
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
            foreach (var block in blocks.Except(splitHeaders.Values))
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
        return (CodeWithoutComment(code) ?? code).TrimEnd();
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

    // where what a continued line starts with is to be: the anchors of the lines of its statement before it.
    private enum AnchorKind
    {
        // the first argument of an argument list, the one after a comma, a named argument's value: where an argument starts.
        Argument,

        // what an argument list opens on: where it is, once the arguments after a comma are let go.
        ArgumentList,

        // the value an assignment is of.
        Value,

        // what follows the name a statement starts with: a call's arguments.
        AfterName,
    }

    private readonly record struct Anchor(AnchorKind Kind, int Column, int Depth);

    // the lines a statement is continued on, each where what it starts with lines up with the lines before it: with the arguments of the argument list that is
    // open, with the value of an assignment, with what follows the statement's first name - a level further in when there is none of them. A line that starts
    // with an operator has the operator before that column, so that the operands line up, when the setting says so. A declaration's lines are where the
    // declaration starts, and a comment's - the statement's own, a member of an Enum or a Type, or a statement whose lines are not aligned - are too; what a comment
    // is continued on is where the comment is.
    private sealed class Continuation(CodeFormattingOptions options, int column, bool isComment, bool isMember, bool isDeclaration)
    {
        private readonly Stack<Anchor> _anchors = new();
        private int _depth;
        private int? _comment;

        public string Place(string text, string? next)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return options.EmptyLineHandlingMethod switch
                {
                    EmptyLineHandlingMethod.Remove => string.Empty,
                    EmptyLineHandlingMethod.Indent => new string(' ', column),
                    _ => text,
                };
            }

            var code = text.TrimStart();
            int at;
            if (!options.AlignContinuations || isComment || isMember)
            {
                at = column;
            }
            else if (_comment is { } comment)
            {
                at = comment;
            }
            else if (IsComment(code))
            {
                at = AnchorColumn();
                _comment = at;
            }
            else if (isDeclaration)
            {
                at = column;
            }
            else
            {
                var operatorAdjust = options.IgnoreOperatorsInContinuations && LeadingOperator().IsMatch(code) ? 2 : 0;
                at = Math.Max(AnchorColumn() - operatorAdjust, 0);
            }

            // what continues a comment is the comment's text, not the declaration's.
            var wanted = Aligned(text, new string(' ', at), code, isDeclaration && _comment is null, options);
            Saw(wanted, next);
            return wanted;
        }

        // what a line of the statement, as it is to be, says of where its next line goes.
        public void Saw(string line, string? next)
        {
            if (CommentAt(line) is { } eol && line.TrimEnd().EndsWith(" _", StringComparison.Ordinal))
            {
                // an end-of-line comment continued on the next line: what follows it is where it is.
                _comment ??= eol.Comment;
            }

            if (IsComment(line) || !options.AlignContinuations)
            {
                return;
            }

            var code = CodeWithoutComment(line) ?? line;
            Scan(code);

            // a line that ends with a comma, or with a named argument's :=, or is followed by one that starts with :=, starts the next argument: where the argument
            // list is. A line that ends with an opening parenthesis starts the list's first argument where the line before it says.
            var ending = code.TrimEnd().TrimEnd('_').TrimEnd();
            if (ending.EndsWith(',') || ending.EndsWith(":=", StringComparison.Ordinal) || (next?.TrimStart().StartsWith(":=", StringComparison.Ordinal) ?? false))
            {
                while (_anchors.Count > 0 && _anchors.Peek().Kind == AnchorKind.Argument)
                {
                    _anchors.Pop();
                }
            }
            else if (ending.EndsWith('('))
            {
                for (var pop = 0; pop < 2 && _anchors.Count > 0; pop++)
                {
                    _anchors.Pop();
                }
            }
        }

        // the anchors a line's code sets, from after what it starts with that sets none: its line number, its Set, Let or Declare, its indentation.
        private void Scan(string code)
        {
            var start = IgnoredStart().Match(code).Length;
            var inString = false;
            for (var index = start; index < code.Length; index++)
            {
                var character = code[index];
                if (character == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                switch (character)
                {
                    case '(':
                        _depth++;
                        _anchors.Push(new Anchor(AnchorKind.ArgumentList, index + 1, _depth));
                        _anchors.Push(new Anchor(AnchorKind.Argument, index + 1, _depth));
                        break;
                    case ')':
                        // the list is closed: its anchors, and those of what was in it, are let go.
                        while (_anchors.Count > 0 && _anchors.Peek().Depth >= _depth && _depth > 0)
                        {
                            _anchors.Pop();
                        }

                        _depth = Math.Max(_depth - 1, 0);
                        break;
                    case ',':
                        _anchors.Push(new Anchor(AnchorKind.Argument, NextToken(code, index + 1), _depth));
                        break;
                    case ':' when index + 1 < code.Length && code[index + 1] == '=':
                        _anchors.Push(new Anchor(AnchorKind.Argument, NextToken(code, index + 2), _depth));
                        index++;
                        break;
                    case ':':
                        // another statement on the line: nothing of the one before it is an anchor.
                        _anchors.Clear();
                        _depth = 0;
                        break;
                    case ' ' when string.CompareOrdinal(code, index, " = ", 0, 3) == 0:
                        if (!_anchors.Any(anchor => anchor.Kind is AnchorKind.Value or AnchorKind.AfterName))
                        {
                            _anchors.Push(new Anchor(AnchorKind.Value, index + 3, _depth));
                        }

                        break;
                    case ' ' when index >= 5 && string.Compare(code, index - 5, " Then ", 0, 6, StringComparison.OrdinalIgnoreCase) == 0:
                        // what follows the Then of an If is where the If's condition was.
                        while (_anchors.Count > 1)
                        {
                            _anchors.Pop();
                        }

                        break;
                    case ' ' when _anchors.Count == 0 && index < code.Length - 3:
                        _anchors.Push(new Anchor(AnchorKind.AfterName, index + 1, _depth));
                        break;
                }
            }
        }

        // where the next line starts: the innermost argument, argument list or value; what follows the statement's name; or a level in.
        private int AnchorColumn()
        {
            int? anchored = null;
            int? afterName = null;
            foreach (var anchor in _anchors.Reverse())
            {
                if (anchor.Kind == AnchorKind.AfterName)
                {
                    afterName = anchor.Column;
                }
                else
                {
                    anchored = anchor.Column;
                }
            }

            return anchored ?? afterName ?? column + options.IndentSpaces;
        }

        private static int NextToken(string code, int index)
        {
            while (index < code.Length && code[index] == ' ')
            {
                index++;
            }

            return index;
        }
    }

    [GeneratedRegex(@"^[+&]\s")]
    private static partial Regex LeadingOperator();

    // what a line starts with that sets no anchor: its indentation and line number, a Set or a Let, a Declare up to its name.
    [GeneratedRegex(@"^\s*(?:(?:-?\d+|&H[0-9A-F]{1,8}):?\s+)?(?:[LR]?Set\s+|Let\s+|(?:(?:Public|Private)\s+)?Declare\s+(?:PtrSafe\s+)?(?:Function|Sub))?", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoredStart();

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

    [GeneratedRegex(@"^As\s", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingAs();

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
