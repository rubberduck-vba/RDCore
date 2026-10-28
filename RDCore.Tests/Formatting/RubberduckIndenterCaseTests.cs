using RDCore.Parsing;
using RDCore.SDK.Formatting;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RDCore.Tests.Formatting;

/// <summary>
/// Rubberduck's indenter test cases, as expectations: each input, the settings it sets over the defaults, and the lines it expects, indented by the engine.
/// </summary>
/// <remarks>
/// The cases are data (<c>Data\rubberduck-indenter-cases.json</c>, from RubberduckTests\SmartIndenter): what the expected lines are is what Rubberduck's indenter
/// was tested to write, and nothing of its code is in the engine. A case whose expectation is of what the indenter does not do yet - the alignment of
/// continuations, comments and declarations, empty lines, the spacing of procedures - is said so, with what it waits for.
/// </remarks>
[TestClass]
public sealed class RubberduckIndenterCaseTests
{
    private sealed record class IndenterCase(string Source, JsonObject Settings, string[] Code, string[] Expected);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    private static readonly Lazy<IReadOnlyList<IndenterCase>> Cases = new(() =>
    {
        using var stream = typeof(RubberduckIndenterCaseTests).Assembly.GetManifestResourceStream("RubberduckIndenterCases")!;
        var document = JsonNode.Parse(stream)!;
        return [.. document["cases"]!.AsArray().Select(item => item.Deserialize<IndenterCase>(Json)!)];
    });

    public static IEnumerable<object[]> AllCases => Cases.Value.Select(item => new object[] { item.Source });

    // what the engine does not do yet, and the slice that does it: a case that waits for one is left out until it is done, and said so.
    private static readonly Dictionary<string, string> Waiting = new(StringComparer.Ordinal)
    {
        ["EndOfLineCommentTests#AbsolutePositionWorks"] = "F3: end-of-line comments",
        ["EndOfLineCommentTests#StandardGapWorks"] = "F3: end-of-line comments",
        ["EndOfLineCommentTests#AlignInColumnWorks"] = "F3: end-of-line comments",
        ["EndOfLineCommentTests#AbsoluteFallsBackToOneSpace"] = "F3: end-of-line comments",
        ["EndOfLineCommentTests#WorksOutsideOfProcedures"] = "F3: end-of-line comments",
        ["EndOfLineCommentTests#WorksInsideEnums"] = "F3: end-of-line comments",
        ["LineContinuationTests#DeclarationLineAlignsCorrectly"] = "F3: continuations",
        ["LineContinuationTests#DeclarationPtrSafeLineAlignsCorrectly"] = "F3: continuations",
        ["LineContinuationTests#FunctionParametersAlignCorrectly"] = "F3: continuations",
        ["LineContinuationTests#NamedParametersAlignCorrectly"] = "F3: continuations",
        ["LineContinuationTests#IgnoreOperatorsInContinuationsOnWorks"] = "F3: continuations",
        ["LineContinuationTests#IgnoreOperatorsInContinuationsOffWorks"] = "F3: continuations",
        ["LineContinuationTests#ContinuedEndOfLineCommentAligned"] = "F3: continuations",
        ["LineContinuationTests#DoWhileOnTwoLinesWorks"] = "F3: continuations",
        ["LineContinuationTests#ContinuedIfThenWorks"] = "F3: continuations",
        ["LineContinuationTests#MultipleFunctionCallsWorks"] = "F3: continuations",
        ["LineContinuationTests#MultiLineFunctionCallsWorksWithSubFunctionCall"] = "F3: continuations",
        ["LineContinuationTests#MultiLineFunctionCallsWorksWithSubFunctionCallUnderScoreInParam"] = "F3: continuations",
        ["LineContinuationTests#MultiLineFunctionCallsWorksWithSubFunctionCallUnderScoreInParamString"] = "F3: continuations",
        ["LineContinuationTests#UncontinuedNestedFunctionWorks"] = "F3: continuations",
        ["LineContinuationTests#ContinuedNestedFunctionWorks"] = "F3: continuations",
        ["LineContinuationTests#MultiLineContinuedFunctionWorks"] = "F3: continuations",
        ["LineContinuationTests#ContinuationsInProcedureDeclarationsWorks"] = "F3: continuations",
        ["LineContinuationTests#ContinuationsInProcedureDeclarationsWithAlignWorks"] = "F3: continuations",
        ["LineContinuationTests#ContinuationsInProcedureDeclarationsWithAlignWorksBareType"] = "F3: continuations",
        ["LineContinuationTests#ContinuationWithOnlyCommentWorks"] = "F3: continuations",
        ["LineContinuationTests#TrailingMultiLineCommentFirstWorks"] = "F3: continuations",
        ["LineContinuationTests#TrailingMultiLineCommentSecondWorks"] = "F3: continuations",
        ["LineContinuationTests#SplitNamedParameterAlignsCorrectly"] = "F3: continuations",
        ["LineContinuationTests#MultipleSplitNamedParametersAlignCorrectly"] = "F3: continuations",
        ["MiscAndCornerCaseTests#HexLineNumbersWork"] = "F9: pretty listing, a hex line number listed in decimal",
        ["MiscAndCornerCaseTests#MultiLineDimWithCommasAlignDimsWorks"] = "F3: Dim alignment",
        ["MiscAndCornerCaseTests#MultiLineDimWithCommasDontIndentFirstBlockWorks"] = "F3: the first blocks of a procedure",
        ["MiscAndCornerCaseTests#SingleQuoteInEndOfLineCommentWorks"] = "F3: end-of-line comments",
        ["MiscAndCornerCaseTests#BracketsInEndOfLineCommentsWork"] = "F3: end-of-line comments",
        ["MiscAndCornerCaseTests#AlignmentAnchorsInStringLiteralsAreIgnored"] = "F3: continuations",
        ["MiscAndCornerCaseTests#LineNumbersInsideContinuationsWork"] = "F3: continuations",
        ["SpecificSettingTests#IndentFirstCommentBlockOffWorks"] = "F3: the first blocks of a procedure",
        ["SpecificSettingTests#IndentEnumTypeCommentBlockOnWorks"] = "F3: the first blocks of a procedure",
        ["SpecificSettingTests#IndentFirstCommentBlockOffOnlyOnFirst"] = "F3: the first blocks of a procedure",
        ["SpecificSettingTests#IndentFirstCommentBlockOffIgnoreEmptyOnlyOnFirst"] = "F3: the first blocks of a procedure",
        ["SpecificSettingTests#IndentFirstDeclarationBlockOffWorks"] = "F3: the first blocks of a procedure",
        ["SpecificSettingTests#IndentFirstDeclarationBlockOffIgnoreEmptyWorks"] = "F3: the first blocks of a procedure",
        ["SpecificSettingTests#IndentFirstDeclarationCommentBlockOffWorksIntermingled"] = "F3: the first blocks of a procedure",
        ["SpecificSettingTests#IndentFirstDeclarationCommentBlockOffIgnoreEmptyWorksIntermingled"] = "F3: the first blocks of a procedure",
        ["SpecificSettingTests#IndentFirstDeclarationBlockOffOnlyOnFirst"] = "F3: the first blocks of a procedure",
        ["SpecificSettingTests#ForceDebugPrintInColumn1OnWorks"] = "F3: column 1",
        ["SpecificSettingTests#ForceDebugAssertInColumn1OnWorks"] = "F3: column 1",
        ["SpecificSettingTests#ForceStopInColumn1OnWorks"] = "F3: column 1",
        ["SpecificSettingTests#ForceOnlyDebugPrintInColumn1OnWorks"] = "F3: column 1",
        ["SpecificSettingTests#ForceOnlyDebugAssertInColumn1OnWorks"] = "F3: column 1",
        ["SpecificSettingTests#ForceOnlyStopInColumn1OnWorks"] = "F3: column 1",
        ["SpecificSettingTests#AlignCommentsWithCodeOffWorks"] = "F3: comments",
        ["SpecificSettingTests#ForceCompilerDirectivesInColumn1OnWorks"] = "F3: column 1",
        ["SpecificSettingTests#AlignDimsOnWorks"] = "F3: Dim alignment",
        ["SpecificSettingTests#AlignDimsFallsBackToOneSpace"] = "F3: Dim alignment",
        ["SpecificSettingTests#AlignDimColumnWorksVariedSpacing"] = "F3: Dim alignment",
        ["SpecificSettingTests#RemoveEmptyLinesWorks"] = "F3: empty lines",
        ["SpecificSettingTests#IndentEmptyLinesWorks"] = "F3: empty lines",
        ["VerticalSpacingTests#VerticalSpacing_InsertBlankLinesWorks"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_RemoveExtraLinesWorks"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_RemoveAllExtraLinesWorks"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_MixedInsertsAndDeletesWorks"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_InsertBlankLinesWorksWithProperties"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_GroupSingleProperty"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_GroupMultipleProperties"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_InsertBlankLinesWorksWithTypes"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_InsertBlankLinesWorksWithEnums"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_CommentsDoNotEffectSpacing"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_DimBlocksStayIntact"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_IgnoresDeclareFunctions"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_DoesNotChangeSpacingInProcedures"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_DoesNotChangeSpacingInEnumsOrTypes"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_MaintainsSpacingAboveFirstProcedure"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_MaintainsIgnoresCommentAboveFirstProcedure"] = "F3: vertical spacing",
        ["VerticalSpacingTests#VerticalSpacing_RemovesExtraSpacingAboveFirstProcedure"] = "F3: vertical spacing",
    };

    [TestMethod]
    [DynamicData(nameof(AllCases))]
    public void TheEngine_IndentsAsTheCaseExpects(string source)
    {
        if (Waiting.TryGetValue(source, out var slice))
        {
            Assert.Inconclusive($"{source}: waits for {slice}.");
        }

        var item = Cases.Value.Single(candidate => candidate.Source == source);
        var options = item.Settings.Deserialize<CodeFormattingOptions>(Json)!;
        var text = string.Join("\r\n", item.Code);
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);

        var indented = ModuleIndenter.Apply(text, ModuleIndenter.Indent(parse, text, options));

        Assert.AreEqual(string.Join("\r\n", item.Expected), indented);
    }

    // formatting formatted code changes nothing.
    [TestMethod]
    [DynamicData(nameof(AllCases))]
    public void WhatTheCaseExpects_IsGivenNoEdit(string source)
    {
        if (Waiting.TryGetValue(source, out var slice))
        {
            Assert.Inconclusive($"{source}: waits for {slice}.");
        }

        var item = Cases.Value.Single(candidate => candidate.Source == source);
        var options = item.Settings.Deserialize<CodeFormattingOptions>(Json)!;
        var text = string.Join("\r\n", item.Expected);
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);

        Assert.IsEmpty(ModuleIndenter.Indent(parse, text, options));
    }
}
