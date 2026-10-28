using RDCore.SDK.Formatting;
using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;

namespace RDCore.SDK.Server.Configuration;

/// <summary>
/// The code formatting settings of the platform, bound from <c>appsettings.json</c> and set by a user (<see cref="SdkAppOptions.Formatting"/>): Rubberduck's indenter
/// settings, and RDCore's own.
/// </summary>
/// <remarks>
/// <para>
/// A setting is what <see cref="CodeFormattingOptions"/> says of the property of the same name, and has its default. They are live: the next line that is
/// formatted is formatted as the user's settings say it now.
/// </para>
/// <para>
/// <see cref="IndentSpaces"/> and the tab size of a client's editor are one setting said twice: a client that has a tab size sets this one to it, and one that
/// is told this one sets its tab size to it.
/// </para>
/// </remarks>
public record class SdkFormattingOptions
{
    private static readonly CodeFormattingOptions Defaults = CodeFormattingOptions.Default;

    /// <summary><inheritdoc cref="CodeFormattingOptions.IndentEntireProcedureBody"/></summary>
    public bool IndentEntireProcedureBody { get; set; } = Defaults.IndentEntireProcedureBody;

    /// <summary><inheritdoc cref="CodeFormattingOptions.IndentEnumTypeAsProcedure"/></summary>
    public bool IndentEnumTypeAsProcedure { get; set; } = Defaults.IndentEnumTypeAsProcedure;

    /// <summary><inheritdoc cref="CodeFormattingOptions.IndentFirstCommentBlock"/></summary>
    public bool IndentFirstCommentBlock { get; set; } = Defaults.IndentFirstCommentBlock;

    /// <summary><inheritdoc cref="CodeFormattingOptions.IndentFirstDeclarationBlock"/></summary>
    public bool IndentFirstDeclarationBlock { get; set; } = Defaults.IndentFirstDeclarationBlock;

    /// <summary><inheritdoc cref="CodeFormattingOptions.IgnoreEmptyLinesInFirstBlocks"/></summary>
    public bool IgnoreEmptyLinesInFirstBlocks { get; set; } = Defaults.IgnoreEmptyLinesInFirstBlocks;

    /// <summary><inheritdoc cref="CodeFormattingOptions.AlignCommentsWithCode"/></summary>
    public bool AlignCommentsWithCode { get; set; } = Defaults.AlignCommentsWithCode;

    /// <summary><inheritdoc cref="CodeFormattingOptions.AlignContinuations"/></summary>
    public bool AlignContinuations { get; set; } = Defaults.AlignContinuations;

    /// <summary><inheritdoc cref="CodeFormattingOptions.IgnoreOperatorsInContinuations"/></summary>
    public bool IgnoreOperatorsInContinuations { get; set; } = Defaults.IgnoreOperatorsInContinuations;

    /// <summary><inheritdoc cref="CodeFormattingOptions.IndentCase"/></summary>
    public bool IndentCase { get; set; } = Defaults.IndentCase;

    /// <summary><inheritdoc cref="CodeFormattingOptions.ForceDebugStatementsInColumn1"/></summary>
    public bool ForceDebugStatementsInColumn1 { get; set; } = Defaults.ForceDebugStatementsInColumn1;

    /// <summary><inheritdoc cref="CodeFormattingOptions.ForceDebugPrintInColumn1"/></summary>
    public bool ForceDebugPrintInColumn1 { get; set; } = Defaults.ForceDebugPrintInColumn1;

    /// <summary><inheritdoc cref="CodeFormattingOptions.ForceDebugAssertInColumn1"/></summary>
    public bool ForceDebugAssertInColumn1 { get; set; } = Defaults.ForceDebugAssertInColumn1;

    /// <summary><inheritdoc cref="CodeFormattingOptions.ForceStopInColumn1"/></summary>
    public bool ForceStopInColumn1 { get; set; } = Defaults.ForceStopInColumn1;

    /// <summary><inheritdoc cref="CodeFormattingOptions.ForceCompilerDirectivesInColumn1"/></summary>
    public bool ForceCompilerDirectivesInColumn1 { get; set; } = Defaults.ForceCompilerDirectivesInColumn1;

    /// <summary><inheritdoc cref="CodeFormattingOptions.IndentCompilerDirectives"/></summary>
    public bool IndentCompilerDirectives { get; set; } = Defaults.IndentCompilerDirectives;

    /// <summary><inheritdoc cref="CodeFormattingOptions.AlignDims"/></summary>
    public bool AlignDims { get; set; } = Defaults.AlignDims;

    /// <summary><inheritdoc cref="CodeFormattingOptions.AlignDimColumn"/></summary>
    [Range(0, CodeFormattingOptions.MaximumColumn)]
    public int AlignDimColumn { get; set; } = Defaults.AlignDimColumn;

    /// <summary><inheritdoc cref="CodeFormattingOptions.EndOfLineCommentStyle"/></summary>
    public EndOfLineCommentStyle EndOfLineCommentStyle { get; set; } = Defaults.EndOfLineCommentStyle;

    /// <summary><inheritdoc cref="CodeFormattingOptions.EndOfLineCommentColumnSpaceAlignment"/></summary>
    [Range(0, CodeFormattingOptions.MaximumColumn)]
    public int EndOfLineCommentColumnSpaceAlignment { get; set; } = Defaults.EndOfLineCommentColumnSpaceAlignment;

    /// <summary><inheritdoc cref="CodeFormattingOptions.EmptyLineHandlingMethod"/></summary>
    public EmptyLineHandlingMethod EmptyLineHandlingMethod { get; set; } = Defaults.EmptyLineHandlingMethod;

    /// <summary><inheritdoc cref="CodeFormattingOptions.IndentSpaces"/></summary>
    [Range(0, CodeFormattingOptions.MaximumIndentSpaces)]
    public int IndentSpaces { get; set; } = Defaults.IndentSpaces;

    /// <summary><inheritdoc cref="CodeFormattingOptions.VerticallySpaceProcedures"/></summary>
    public bool VerticallySpaceProcedures { get; set; } = Defaults.VerticallySpaceProcedures;

    /// <summary><inheritdoc cref="CodeFormattingOptions.LinesBetweenProcedures"/></summary>
    [Range(0, CodeFormattingOptions.MaximumLinesBetweenProcedures)]
    public int LinesBetweenProcedures { get; set; } = Defaults.LinesBetweenProcedures;

    /// <summary><inheritdoc cref="CodeFormattingOptions.GroupRelatedProperties"/></summary>
    public bool GroupRelatedProperties { get; set; } = Defaults.GroupRelatedProperties;

    /// <summary><inheritdoc cref="CodeFormattingOptions.FormatOnType"/></summary>
    public bool FormatOnType { get; set; } = Defaults.FormatOnType;

    /// <summary><inheritdoc cref="CodeFormattingOptions.CompleteBlocks"/></summary>
    public bool CompleteBlocks { get; set; } = Defaults.CompleteBlocks;

    /// <summary><inheritdoc cref="CodeFormattingOptions.Snippets"/></summary>
    public bool Snippets { get; set; } = Defaults.Snippets;

    /// <summary><inheritdoc cref="CodeFormattingOptions.SnippetDimPlacement"/></summary>
    public SnippetDimPlacement SnippetDimPlacement { get; set; } = Defaults.SnippetDimPlacement;

    /// <summary><inheritdoc cref="CodeFormattingOptions.PrettyList"/></summary>
    public bool PrettyList { get; set; } = Defaults.PrettyList;

    /// <summary>
    /// The options these settings say, with a number kept to the limits of its setting.
    /// </summary>
    public CodeFormattingOptions ToOptions() => new()
    {
        IndentEntireProcedureBody = IndentEntireProcedureBody,
        IndentEnumTypeAsProcedure = IndentEnumTypeAsProcedure,
        IndentFirstCommentBlock = IndentFirstCommentBlock,
        IndentFirstDeclarationBlock = IndentFirstDeclarationBlock,
        IgnoreEmptyLinesInFirstBlocks = IgnoreEmptyLinesInFirstBlocks,
        AlignCommentsWithCode = AlignCommentsWithCode,
        AlignContinuations = AlignContinuations,
        IgnoreOperatorsInContinuations = IgnoreOperatorsInContinuations,
        IndentCase = IndentCase,
        ForceDebugStatementsInColumn1 = ForceDebugStatementsInColumn1,
        ForceDebugPrintInColumn1 = ForceDebugPrintInColumn1,
        ForceDebugAssertInColumn1 = ForceDebugAssertInColumn1,
        ForceStopInColumn1 = ForceStopInColumn1,
        ForceCompilerDirectivesInColumn1 = ForceCompilerDirectivesInColumn1,
        IndentCompilerDirectives = IndentCompilerDirectives,
        AlignDims = AlignDims,
        AlignDimColumn = Math.Clamp(AlignDimColumn, 0, CodeFormattingOptions.MaximumColumn),
        EndOfLineCommentStyle = EndOfLineCommentStyle,
        EndOfLineCommentColumnSpaceAlignment = Math.Clamp(EndOfLineCommentColumnSpaceAlignment, 0, CodeFormattingOptions.MaximumColumn),
        EmptyLineHandlingMethod = EmptyLineHandlingMethod,
        IndentSpaces = Math.Clamp(IndentSpaces, 0, CodeFormattingOptions.MaximumIndentSpaces),
        VerticallySpaceProcedures = VerticallySpaceProcedures,
        LinesBetweenProcedures = Math.Clamp(LinesBetweenProcedures, 0, CodeFormattingOptions.MaximumLinesBetweenProcedures),
        GroupRelatedProperties = GroupRelatedProperties,
        FormatOnType = FormatOnType,
        CompleteBlocks = CompleteBlocks,
        Snippets = Snippets,
        SnippetDimPlacement = SnippetDimPlacement,
        PrettyList = PrettyList,
    };
}
