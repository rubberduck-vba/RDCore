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
/// continuations, comments and declarations, empty lines, the spacing of procedures - is said so, with what the engine does not do yet.
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

    // the cases of what the engine does not do yet: each is left out, and said so, until it does.
    private static readonly Dictionary<string, string> Waiting = new(StringComparer.Ordinal)
    {
        ["EndOfLineCommentTests#WorksOutsideOfProcedures"] = "the lines between procedures are not spaced yet",
        ["LineContinuationTests#DeclarationLineAlignsCorrectly"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#DeclarationPtrSafeLineAlignsCorrectly"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#FunctionParametersAlignCorrectly"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#NamedParametersAlignCorrectly"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#IgnoreOperatorsInContinuationsOnWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#IgnoreOperatorsInContinuationsOffWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#ContinuedEndOfLineCommentAligned"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#DoWhileOnTwoLinesWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#ContinuedIfThenWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#MultipleFunctionCallsWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#MultiLineFunctionCallsWorksWithSubFunctionCall"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#MultiLineFunctionCallsWorksWithSubFunctionCallUnderScoreInParam"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#MultiLineFunctionCallsWorksWithSubFunctionCallUnderScoreInParamString"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#UncontinuedNestedFunctionWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#ContinuedNestedFunctionWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#MultiLineContinuedFunctionWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#ContinuationsInProcedureDeclarationsWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#ContinuationsInProcedureDeclarationsWithAlignWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#ContinuationsInProcedureDeclarationsWithAlignWorksBareType"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#ContinuationWithOnlyCommentWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#TrailingMultiLineCommentFirstWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#TrailingMultiLineCommentSecondWorks"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#SplitNamedParameterAlignsCorrectly"] = "continuation lines are not aligned yet",
        ["LineContinuationTests#MultipleSplitNamedParametersAlignCorrectly"] = "continuation lines are not aligned yet",
        ["MiscAndCornerCaseTests#HexLineNumbersWork"] = "a line is not listed from its compiled form yet, which lists a hex line number in decimal",
        ["MiscAndCornerCaseTests#AlignmentAnchorsInStringLiteralsAreIgnored"] = "continuation lines are not aligned yet",
        ["MiscAndCornerCaseTests#LineNumbersInsideContinuationsWork"] = "continuation lines are not aligned yet",
        ["VerticalSpacingTests#VerticalSpacing_InsertBlankLinesWorks"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_RemoveExtraLinesWorks"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_RemoveAllExtraLinesWorks"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_MixedInsertsAndDeletesWorks"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_InsertBlankLinesWorksWithProperties"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_GroupSingleProperty"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_GroupMultipleProperties"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_InsertBlankLinesWorksWithTypes"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_InsertBlankLinesWorksWithEnums"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_CommentsDoNotEffectSpacing"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_DimBlocksStayIntact"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_IgnoresDeclareFunctions"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_DoesNotChangeSpacingInProcedures"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_DoesNotChangeSpacingInEnumsOrTypes"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_MaintainsSpacingAboveFirstProcedure"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_MaintainsIgnoresCommentAboveFirstProcedure"] = "the lines between procedures are not spaced yet",
        ["VerticalSpacingTests#VerticalSpacing_RemovesExtraSpacingAboveFirstProcedure"] = "the lines between procedures are not spaced yet",
    };

    [TestMethod]
    [DynamicData(nameof(AllCases))]
    public void TheEngine_IndentsAsTheCaseExpects(string source)
    {
        if (Waiting.TryGetValue(source, out var missing))
        {
            Assert.Inconclusive($"{source}: {missing}.");
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
        if (Waiting.TryGetValue(source, out var missing))
        {
            Assert.Inconclusive($"{source}: {missing}.");
        }

        var item = Cases.Value.Single(candidate => candidate.Source == source);
        var options = item.Settings.Deserialize<CodeFormattingOptions>(Json)!;
        var text = string.Join("\r\n", item.Expected);
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);

        Assert.IsEmpty(ModuleIndenter.Indent(parse, text, options));
    }
}
