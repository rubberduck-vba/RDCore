namespace RDCore.SDK.Formatting;

/// <summary>
/// Where the comment at the end of a line that has code goes.
/// </summary>
public enum EndOfLineCommentStyle
{
    /// <summary>The comment keeps the column it was written at.</summary>
    Absolute = 0,

    /// <summary>The comment keeps the number of spaces between the code and itself that it was written with.</summary>
    SameGap = 1,

    /// <summary>The comment is two spaces after the code.</summary>
    StandardGap = 2,

    /// <summary>The comment is in the column <see cref="CodeFormattingOptions.EndOfLineCommentColumnSpaceAlignment"/> names, or after the code when that is longer.</summary>
    AlignInColumn = 3,
}

/// <summary>
/// What is done with the lines that have nothing on them.
/// </summary>
public enum EmptyLineHandlingMethod
{
    /// <summary>An empty line is left as it is.</summary>
    Ignore = 0,

    /// <summary>An empty line is removed.</summary>
    Remove = 1,

    /// <summary>An empty line is indented as the code around it is.</summary>
    Indent = 2,
}

/// <summary>
/// Where the <c>Dim</c> a snippet declares goes.
/// </summary>
public enum SnippetDimPlacement
{
    /// <summary>
    /// Right above the block the snippet inserts, where the variable is first used: the declaration follows the variable's name as it is typed.
    /// </summary>
    AboveBlock = 0,

    /// <summary>
    /// At the top of the procedure, with the other declarations. The declaration is inserted with the snippet and does not follow the variable's name as it is typed:
    /// a snippet can only mirror text inside itself.
    /// </summary>
    ProcedureTop = 1,
}

/// <summary>
/// What a formatting engine is told to do (<see cref="Server.Configuration.SdkFormattingOptions"/> is where a user says it): how deep to indent, what to align,
/// and what the editing assists do.
/// </summary>
/// <remarks>
/// <para>
/// The first group of settings is Rubberduck's indenter, with the names and meanings its settings have, which are the Smart Indenter's for the most part: a user who
/// had them in Rubberduck has them here as they were (<see cref="Server.Configuration.SdkFormattingOptions"/> reads them the same way). The second group is
/// RDCore's own: what formats as the user types, and what completes as they do.
/// </para>
/// <para>
/// The indentation is made of spaces, <see cref="IndentSpaces"/> of them for a level.
/// </para>
/// </remarks>
public sealed record class CodeFormattingOptions
{
    /// <summary>The largest <see cref="AlignDimColumn"/> and <see cref="EndOfLineCommentColumnSpaceAlignment"/>.</summary>
    public const int MaximumColumn = 100;

    /// <summary>The largest <see cref="IndentSpaces"/>.</summary>
    public const int MaximumIndentSpaces = 32;

    /// <summary>The largest <see cref="LinesBetweenProcedures"/>.</summary>
    public const int MaximumLinesBetweenProcedures = 2;

    /// <summary>The options a user who set nothing has.</summary>
    public static CodeFormattingOptions Default { get; } = new();

    /// <summary>Whether the body of a procedure is indented a level from its <c>Sub</c>, <c>Function</c> or <c>Property</c> line and its <c>End</c>.</summary>
    public bool IndentEntireProcedureBody { get; init; } = true;

    /// <summary>Whether the body of an <c>Enum</c> or a <c>Type</c> is indented a level, as a procedure's is.</summary>
    public bool IndentEnumTypeAsProcedure { get; init; }

    /// <summary>Whether the comments at the top of a module, before its first declaration, are indented a level.</summary>
    public bool IndentFirstCommentBlock { get; init; } = true;

    /// <summary>Whether the declarations at the top of a procedure, before its first statement, are indented a level.</summary>
    public bool IndentFirstDeclarationBlock { get; init; } = true;

    /// <summary>Whether the empty lines inside the first comment block and the first declaration block are ignored when those blocks are found.</summary>
    public bool IgnoreEmptyLinesInFirstBlocks { get; init; }

    /// <summary>Whether a comment on a line of its own is indented as the code that follows it.</summary>
    public bool AlignCommentsWithCode { get; init; } = true;

    /// <summary>Whether the lines a statement continues onto are aligned with each other.</summary>
    public bool AlignContinuations { get; init; } = true;

    /// <summary>Whether an operator that begins a continued line is ignored when the continuations are aligned: the operands line up, not the operators.</summary>
    public bool IgnoreOperatorsInContinuations { get; init; } = true;

    /// <summary>Whether the <c>Case</c> clauses of a <c>Select Case</c> are indented a level from the <c>Select Case</c>.</summary>
    public bool IndentCase { get; init; }

    /// <summary>Whether every <c>Debug</c> statement (<c>Print</c> and <c>Assert</c>) and <c>Stop</c> goes in the first column, whatever is set for each.</summary>
    public bool ForceDebugStatementsInColumn1 { get; init; }

    /// <summary>Whether <c>Debug.Print</c> goes in the first column.</summary>
    public bool ForceDebugPrintInColumn1 { get; init; }

    /// <summary>Whether <c>Debug.Assert</c> goes in the first column.</summary>
    public bool ForceDebugAssertInColumn1 { get; init; }

    /// <summary>Whether <c>Stop</c> goes in the first column.</summary>
    public bool ForceStopInColumn1 { get; init; }

    /// <summary>Whether the lines of conditional compilation (<c>#If</c>, <c>#ElseIf</c>, <c>#Else</c>, <c>#End If</c>, <c>#Const</c>) go in the first column.</summary>
    public bool ForceCompilerDirectivesInColumn1 { get; init; }

    /// <summary>Whether the code between the lines of conditional compilation is indented a level.</summary>
    public bool IndentCompilerDirectives { get; init; } = true;

    /// <summary>Whether the <c>As</c> clauses of consecutive declarations are aligned in a column.</summary>
    public bool AlignDims { get; init; }

    /// <summary>The column the <c>As</c> clauses of declarations are aligned in, from 0 to <see cref="MaximumColumn"/>.</summary>
    public int AlignDimColumn { get; init; } = 15;

    /// <summary>Where a comment at the end of a line that has code goes.</summary>
    public EndOfLineCommentStyle EndOfLineCommentStyle { get; init; } = EndOfLineCommentStyle.AlignInColumn;

    /// <summary>The column a comment at the end of a line is aligned in when <see cref="EndOfLineCommentStyle"/> is <see cref="EndOfLineCommentStyle.AlignInColumn"/>, from 0 to <see cref="MaximumColumn"/>.</summary>
    public int EndOfLineCommentColumnSpaceAlignment { get; init; } = 50;

    /// <summary>What is done with the lines that have nothing on them.</summary>
    public EmptyLineHandlingMethod EmptyLineHandlingMethod { get; init; } = EmptyLineHandlingMethod.Ignore;

    /// <summary>The number of spaces a level of indentation is, from 0 to <see cref="MaximumIndentSpaces"/>.</summary>
    public int IndentSpaces { get; init; } = 4;

    /// <summary>Whether the number of empty lines between the procedures of a module is made <see cref="LinesBetweenProcedures"/>.</summary>
    public bool VerticallySpaceProcedures { get; init; } = true;

    /// <summary>The number of empty lines between procedures, from 0 to <see cref="MaximumLinesBetweenProcedures"/>, when <see cref="VerticallySpaceProcedures"/> is set.</summary>
    public int LinesBetweenProcedures { get; init; } = 1;

    /// <summary>Whether the <c>Property Get</c>, <c>Let</c> and <c>Set</c> of a property are kept together, in that order, one after another.</summary>
    public bool GroupRelatedProperties { get; init; }

    /// <summary>Whether the line just finished is formatted, and the new one indented, when Enter is pressed.</summary>
    public bool FormatOnType { get; init; } = true;

    /// <summary>Whether pressing Enter after a block opener that has no closer inserts the closer, and leaves the caret in the indented body.</summary>
    public bool CompleteBlocks { get; init; } = true;

    /// <summary>Whether completion offers snippets of blocks, with placeholders.</summary>
    public bool Snippets { get; init; } = true;

    /// <summary>Where the <c>Dim</c> a snippet declares goes.</summary>
    public SnippetDimPlacement SnippetDimPlacement { get; init; } = SnippetDimPlacement.AboveBlock;

    /// <summary>
    /// Whether a finished line is replaced by the line listed from what it compiled to, as MS-VBA lists it, rather than by what a rule of spacing and casing makes of
    /// what was typed.
    /// </summary>
    public bool PrettyList { get; init; } = true;

    /// <summary>Whether <c>Debug.Print</c> goes in the first column: <see cref="ForceDebugPrintInColumn1"/>, or <see cref="ForceDebugStatementsInColumn1"/> which says it of all of them.</summary>
    public bool ForcesDebugPrintInColumn1 => ForceDebugStatementsInColumn1 || ForceDebugPrintInColumn1;

    /// <summary>Whether <c>Debug.Assert</c> goes in the first column: <see cref="ForceDebugAssertInColumn1"/>, or <see cref="ForceDebugStatementsInColumn1"/>.</summary>
    public bool ForcesDebugAssertInColumn1 => ForceDebugStatementsInColumn1 || ForceDebugAssertInColumn1;

    /// <summary>Whether <c>Stop</c> goes in the first column: <see cref="ForceStopInColumn1"/>, or <see cref="ForceDebugStatementsInColumn1"/>.</summary>
    public bool ForcesStopInColumn1 => ForceDebugStatementsInColumn1 || ForceStopInColumn1;
}
