using System.Collections.Immutable;

namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// A block a line can be inside of, which says what can be written in it: the statement that ends it (<c>Next</c>, <c>Loop</c>, <c>End With</c>), and the ones that
/// can only be written in one (<c>Exit For</c>, <c>Case</c>, <c>ElseIf</c>).
/// </summary>
public enum CompletionBlock
{
    /// <summary>A <c>For</c> loop (<strong>MS-VBAL §5.4.2.3</strong>).</summary>
    For,

    /// <summary>A <c>For Each</c> loop (<strong>MS-VBAL §5.4.2.4</strong>).</summary>
    ForEach,

    /// <summary>A <c>Do</c> loop (<strong>MS-VBAL §5.4.2.2</strong>).</summary>
    Do,

    /// <summary>A <c>While</c> loop (<strong>MS-VBAL §5.4.2.1</strong>).</summary>
    While,

    /// <summary>A <c>With</c> block (<strong>MS-VBAL §5.4.2.21</strong>).</summary>
    With,

    /// <summary>A <c>Select Case</c> block (<strong>MS-VBAL §5.4.2.10</strong>).</summary>
    Select,

    /// <summary>A block <c>If</c> (<strong>MS-VBAL §5.4.2.8</strong>).</summary>
    If,
}

/// <summary>
/// The kind of procedure a line is in, which says how it is exited and ended.
/// </summary>
public enum CompletionProcedure
{
    /// <summary>A <c>Sub</c>.</summary>
    Sub,

    /// <summary>A <c>Function</c>.</summary>
    Function,

    /// <summary>A <c>Property Get</c>, <c>Let</c> or <c>Set</c>.</summary>
    Property,
}

/// <summary>
/// Where a line is in its module: in the declarations section or in a procedure, in which blocks, and what the labels of the procedure are - what decides the
/// statements and the keywords that can be written on it (<strong>MS-VBAL §5.2</strong>, <strong>§5.4</strong>).
/// </summary>
/// <remarks>
/// <para>
/// It is read off the text, statement by statement, and not off the parse of the module. Code that is being typed seldom parses, and least of all where it
/// matters: a <c>For</c> whose <c>Next</c> is the line being written is a <c>For</c> that is not closed. The lines before the line are what say which blocks are
/// open, and the blocks that are open on it are the ones that were opened and not closed before it.
/// </para>
/// <para>
/// A statement is what a line has between its <c>:</c>s, with its continuation lines, its comment left out, and its line number or label taken off: a label is
/// the procedure's, and so is a line number (<strong>MS-VBAL §5.4.1.1</strong>).
/// </para>
/// </remarks>
/// <param name="Procedure">The procedure the line is in; <see langword="null"/> in the declarations section, or between procedures.</param>
/// <param name="Blocks">The blocks the line is inside of, innermost first.</param>
/// <param name="Options">
/// The options the module states on its other lines - <c>Explicit</c>, <c>Base</c>, <c>Compare</c>, <c>Private</c> - each of which a module states once at most
/// (<strong>MS-VBAL §5.2.1</strong>).
/// </param>
/// <param name="Labels">The labels and the line numbers of the procedure the line is in, in the order they are written.</param>
public sealed record class CompletionPlacement(CompletionProcedure? Procedure, ImmutableArray<CompletionBlock> Blocks, ImmutableArray<string> Options, ImmutableArray<string> Labels)
{
    /// <summary>The options a module can state, as the word after <c>Option</c>.</summary>
    public static IReadOnlyList<string> AllOptions { get; } = ["Base", "Compare", "Explicit", "Private"];

    private static readonly string[] Modifiers = ["Public", "Private", "Friend", "Global", "Static"];

    /// <summary>Whether the line is in a procedure, where statements are written; outside of one, only declarations are.</summary>
    public bool IsInProcedure => Procedure is not null;

    /// <summary>Whether the line is inside of a block of <paramref name="kind"/>, however deep.</summary>
    /// <param name="kind">The kind of block.</param>
    public bool IsIn(CompletionBlock kind) => Blocks.Contains(kind);

    /// <summary>
    /// Where line <paramref name="line"/> of a module is.
    /// </summary>
    /// <param name="lines">The lines of the module's source.</param>
    /// <param name="line">The zero-based line.</param>
    public static CompletionPlacement Read(IReadOnlyList<string> lines, int line)
    {
        CompletionProcedure? procedure = null;
        var procedureStart = 0;
        var blocks = new List<CompletionBlock>();

        foreach (var (start, statement, _) in Statements(lines, 0, line))
        {
            var tokens = CompletionStatement.Tokenize(statement);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (ProcedureOf(tokens) is { } opened)
            {
                (procedure, procedureStart) = (opened, start);
                blocks.Clear();
                continue;
            }

            if (procedure is null)
            {
                continue;
            }

            if (tokens[0].Is("End") && tokens.Count > 1 && (tokens[1].Is("Sub") || tokens[1].Is("Function") || tokens[1].Is("Property")))
            {
                procedure = null;
                blocks.Clear();
                continue;
            }

            Follow(tokens, blocks);
        }

        var labels = procedure is null ? [] : LabelsOf(lines, procedureStart, line);
        return new CompletionPlacement(procedure, [.. Enumerable.Reverse(blocks)], [.. OptionsOf(lines, line)], [.. labels]);
    }

    // the options the module states anywhere but on the line being written: one that is stated is not stated again.
    private static IEnumerable<string> OptionsOf(IReadOnlyList<string> lines, int line)
        => Statements(lines, 0, lines.Count)
            .Where(statement => statement.Line != line)
            .Select(statement => CompletionStatement.Tokenize(statement.Statement))
            .Where(tokens => tokens.Count > 1 && tokens[0].Is("Option"))
            .Select(tokens => AllOptions.FirstOrDefault(option => tokens[1].Is(option)))
            .OfType<string>()
            .Distinct();

    /// <summary>
    /// The line of a module that is labelled - or numbered - <paramref name="label"/> (<strong>MS-VBAL §5.4.1.1</strong>), or <c>-1</c> for a label the module does
    /// not have.
    /// </summary>
    /// <param name="lines">The lines of the module's source.</param>
    /// <param name="label">The label, or the line number.</param>
    public static int LineLabelled(IReadOnlyList<string> lines, string label)
        => Statements(lines, 0, lines.Count).FirstOrDefault(statement => statement.Label?.Is(label) == true) is { Label: not null } found ? found.Line : -1;

    /// <summary>
    /// The lines of a module that are numbered (<strong>MS-VBAL §5.4.1.1</strong>), and the statements they are: what a line number of the module names - the
    /// lines of a program of the platform's BASIC, chiefly.
    /// </summary>
    /// <param name="lines">The lines of the module's source.</param>
    public static IReadOnlyList<(string Number, string Statement)> NumberedLinesOf(IReadOnlyList<string> lines)
        => [.. Statements(lines, 0, lines.Count).Where(statement => statement.Label is { } label && char.IsAsciiDigit(label[0])).Select(statement => (statement.Label!, statement.Statement))];

    // the procedure a statement declares the start of: `Sub`, `Function` or `Property`, after the modifiers it can have - and not a `Declare`, which has no body.
    private static CompletionProcedure? ProcedureOf(IReadOnlyList<string> tokens)
    {
        var index = 0;
        while (index < tokens.Count && Modifiers.Any(modifier => tokens[index].Is(modifier)))
        {
            index++;
        }

        return index >= tokens.Count ? null : tokens[index] switch
        {
            var word when word.Is("Sub") => CompletionProcedure.Sub,
            var word when word.Is("Function") => CompletionProcedure.Function,
            var word when word.Is("Property") => CompletionProcedure.Property,
            _ => null,
        };
    }

    // what a statement does to the blocks that are open: it opens one, or closes the innermost one of its kind.
    private static void Follow(IReadOnlyList<string> tokens, List<CompletionBlock> blocks)
    {
        var first = tokens[0];
        var second = tokens.Count > 1 ? tokens[1] : string.Empty;
        if (first.Is("For"))
        {
            blocks.Add(second.Is("Each") ? CompletionBlock.ForEach : CompletionBlock.For);
        }
        else if (first.Is("Next"))
        {
            // `Next i, j` closes two loops.
            var closed = Math.Max(1, tokens.Count(token => token == ",") + (tokens.Count > 1 ? 1 : 0));
            for (var count = 0; count < closed; count++)
            {
                Close(blocks, CompletionBlock.For, CompletionBlock.ForEach);
            }
        }
        else if (first.Is("Do"))
        {
            blocks.Add(CompletionBlock.Do);
        }
        else if (first.Is("Loop"))
        {
            Close(blocks, CompletionBlock.Do);
        }
        else if (first.Is("While"))
        {
            blocks.Add(CompletionBlock.While);
        }
        else if (first.Is("Wend"))
        {
            Close(blocks, CompletionBlock.While);
        }
        else if (first.Is("With"))
        {
            blocks.Add(CompletionBlock.With);
        }
        else if (first.Is("Select"))
        {
            blocks.Add(CompletionBlock.Select);
        }
        else if (first.Is("If") && tokens[^1].Is("Then"))
        {
            // a block If ends its line with Then; a single-line If has its statements after it, and closes itself.
            blocks.Add(CompletionBlock.If);
        }
        else if (first.Is("End") || first.Is("EndIf"))
        {
            if (first.Is("EndIf") || second.Is("If"))
            {
                Close(blocks, CompletionBlock.If);
            }
            else if (second.Is("With"))
            {
                Close(blocks, CompletionBlock.With);
            }
            else if (second.Is("Select"))
            {
                Close(blocks, CompletionBlock.Select);
            }
        }
    }

    private static void Close(List<CompletionBlock> blocks, params CompletionBlock[] kinds)
    {
        var index = blocks.FindLastIndex(block => kinds.Contains(block));
        if (index >= 0)
        {
            blocks.RemoveAt(index);
        }
    }

    // the labels and the line numbers of the procedure that starts at `start`, but the line being written: before it, and after it as far as the procedure goes.
    private static List<string> LabelsOf(IReadOnlyList<string> lines, int start, int line)
    {
        var labels = new List<string>();
        foreach (var (at, statement, label) in Statements(lines, start + 1, lines.Count))
        {
            var tokens = CompletionStatement.Tokenize(statement);
            if (at > line && tokens.Count > 1 && tokens[0].Is("End") && (tokens[1].Is("Sub") || tokens[1].Is("Function") || tokens[1].Is("Property")))
            {
                break;
            }

            if (label is not null && at != line && !labels.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                labels.Add(label);
            }
        }

        return labels;
    }

    // the statements of lines `from` to `to` (excluded): where each starts, its text, and the line number or the label its line starts with, when it is the first
    // statement of the line.
    private static IEnumerable<(int Line, string Statement, string? Label)> Statements(IReadOnlyList<string> lines, int from, int to)
    {
        for (var index = from; index < Math.Min(to, lines.Count); index++)
        {
            var start = index;
            var text = WithoutComment(lines[index].TrimEnd('\r'));

            // a line that ends with ` _` goes on on the next one.
            while (text.EndsWith(" _", StringComparison.Ordinal) && index + 1 < Math.Min(to, lines.Count))
            {
                text = text[..^1] + WithoutComment(lines[++index].TrimEnd('\r'));
            }

            var first = true;
            foreach (var part in SplitStatements(text))
            {
                var statement = part.Trim();
                string? label = null;
                if (first)
                {
                    (label, statement) = TakeLabel(statement, part.Length < text.Length);
                }

                first = false;
                yield return (start, statement, label);
            }
        }
    }

    // a line starts with a line number, or with a label and its `:` - which ends the first statement of the line, as a separator would.
    private static (string? Label, string Statement) TakeLabel(string statement, bool separated)
    {
        var digits = 0;
        while (digits < statement.Length && char.IsAsciiDigit(statement[digits]))
        {
            digits++;
        }

        if (digits > 0 && (digits == statement.Length || char.IsWhiteSpace(statement[digits])))
        {
            return (statement[..digits], statement[digits..].Trim());
        }

        var tokens = CompletionStatement.Tokenize(statement);
        return separated && tokens.Count == 1 && CompletionStatement.IsWord(tokens[0]) && !IsStatementKeyword(tokens[0]) ? (tokens[0], string.Empty) : (null, statement);
    }

    // the statements that can be written alone, which a word before a `:` is - not a label.
    private static bool IsStatementKeyword(string word)
        => word.Is("Else") || word.Is("End") || word.Is("Loop") || word.Is("Next") || word.Is("Wend") || word.Is("Do") || word.Is("Stop") || word.Is("Return")
            || word.Is("Resume") || word.Is("Beep");

    private static IEnumerable<string> SplitStatements(string text)
    {
        var inString = false;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"')
            {
                inString = !inString;
            }
            else if (!inString && text[index] == ':' && (index + 1 >= text.Length || text[index + 1] != '='))
            {
                yield return text[start..index];
                start = index + 1;
            }
        }

        yield return text[start..];
    }

    // the text of a line without its comment: from a `'` that is not in a string literal, or a `Rem` that starts a statement.
    private static string WithoutComment(string text)
    {
        var inString = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"')
            {
                inString = !inString;
            }
            else if (!inString && text[index] == '\'')
            {
                return text[..index];
            }
        }

        var trimmed = text.TrimStart();
        return trimmed.StartsWith("Rem ", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Rem", StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
    }
}
