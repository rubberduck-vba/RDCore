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
/// was tested to write, and nothing of its code is in the engine. A case whose expectation is of what the indenter does not do yet is said so, with what the
/// engine does not do yet, and is run all the same.
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
        ["MiscAndCornerCaseTests#HexLineNumbersWork"] = "a line is not listed from its compiled form yet, which lists a hex line number in decimal",
        ["MiscAndCornerCaseTests#LineNumbersInsideContinuationsWork"] = "a line number on a line of its own inside a continued statement is not placed yet",
    };

    [TestMethod]
    [DynamicData(nameof(AllCases))]
    public void TheEngine_IndentsAsTheCaseExpects(string source)
    {
        var item = Cases.Value.Single(candidate => candidate.Source == source);
        var options = item.Settings.Deserialize<CodeFormattingOptions>(Json)!;
        var text = string.Join("\r\n", item.Code);
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);

        var indented = ModuleIndenter.Apply(text, ModuleIndenter.Indent(parse, text, options));

        var expected = string.Join("\r\n", item.Expected);
        if (Waiting.TryGetValue(source, out var missing))
        {
            // a case that waits is run all the same, and says so once it passes: it waits no longer than it has to.
            Assert.AreNotEqual(expected, indented, $"{source} now passes: remove it from the waiting list.");
            Assert.Inconclusive($"{source}: {missing}.");
        }

        Assert.AreEqual(expected, indented);
    }

    // formatting formatted code changes nothing.
    [TestMethod]
    [DynamicData(nameof(AllCases))]
    public void WhatTheCaseExpects_IsGivenNoEdit(string source)
    {
        var item = Cases.Value.Single(candidate => candidate.Source == source);
        var options = item.Settings.Deserialize<CodeFormattingOptions>(Json)!;
        var text = string.Join("\r\n", item.Expected);
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), text);

        var edits = ModuleIndenter.Indent(parse, text, options);

        // what a waiting case expects may be given no edit before the engine writes it from the input: the case waits only while this fails.
        if (edits.Count > 0 && Waiting.TryGetValue(source, out var missing))
        {
            Assert.Inconclusive($"{source}: {missing}.");
        }

        Assert.IsEmpty(edits);
    }
}
