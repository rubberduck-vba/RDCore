using RDCore.SDK.Model.AST;
using RDCore.SDK.Workspace;

namespace RDCore.SDK.Formatting;

/// <summary>
/// Indents the lines of a program of the platform's BASIC (<see cref="BasicProgramText"/>) as <see cref="ModuleIndenter"/> indents a module: a statement is
/// indented one level for each block it is in - an <c>If</c>, a <c>Select Case</c>, a loop, a <c>With</c>.
/// </summary>
/// <remarks>
/// <para>
/// A program's lines are the body of a procedure its text does not write (<see cref="BasicProgramText.EntryPointName"/>): the lines that are in no block are at
/// the left, whatever <see cref="CodeFormattingOptions.IndentEntireProcedureBody"/> says, and there is no procedure to space from another.
/// </para>
/// <para>
/// A line's number is not part of its indentation. A shell lists a program with its numbers right-aligned to the widest, so that the statements line up whatever
/// the numbering: the indentation is that of the statement, after its number and the space that follows it.
/// </para>
/// </remarks>
public static class ProgramIndenter
{
    private const string NewLine = "\r\n";

    /// <summary>
    /// The statements of a program, each indented by the blocks it is in.
    /// </summary>
    /// <param name="statements">The statements, in the order of their line numbers, without their numbers; what whitespace they start with is not kept.</param>
    /// <param name="parse">Parses the text of a module: the program's statements as the body of its procedure.</param>
    /// <param name="options">How code is indented.</param>
    /// <returns>
    /// The statements, as many as there are and in their order, each with the indentation it has and its trailing text as it was; a program the indenter cannot
    /// read as many lines as it has is returned as it was given.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="statements"/>, <paramref name="parse"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<string> Indent(IReadOnlyList<string> statements, Func<string, ModuleParseResult> parse, CodeFormattingOptions options)
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(options);
        if (statements.Count == 0)
        {
            return statements;
        }

        // a statement is one line of the program: one that has a line end in it is not one the indenter can tell apart, and the program is left as it is.
        if (statements.Any(statement => statement.Contains('\n') || statement.Contains('\r')))
        {
            return statements;
        }

        var text = BasicProgramText.ToModuleSource(string.Join(NewLine, statements.Select(statement => statement.TrimStart())) + NewLine);
        var programOptions = options with { IndentEntireProcedureBody = false, VerticallySpaceProcedures = false };
        var indented = ModuleIndenter.Apply(text, ModuleIndenter.Indent(parse(text), text, programOptions));

        var lines = indented.Split(NewLine);
        var first = BasicProgramText.HeaderLines;
        return lines.Length < first + statements.Count ? statements : lines[first..(first + statements.Count)];
    }
}
