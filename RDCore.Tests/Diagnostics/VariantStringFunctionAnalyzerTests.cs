using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00118</c>: a function of the VBA library that returns a Variant, where its <c>$</c> form is the same function returning a String
/// (MS-VBAL §6.1.2).
/// </summary>
[TestClass]
public sealed class VariantStringFunctionAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> ProgramAsync(params string[] lines)
    {
        var program = ModuleWorkspace.StandardModule("Program", lines);
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var parsed = new ModuleParser().Parse(uri, program);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. new VariantStringFunctionAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, model)).OrderBy(finding => finding.Range.Start)];
    }

    // the line and the column each finding starts at; the module's first line is its VB_Name attribute.
    private static string At(AnalyzerFinding[] findings)
        => string.Join(" ", findings.Select(finding => $"{finding.Range.Start.Line}:{finding.Range.Start.Character}"));

    [TestMethod]
    public async Task AVariantForm_IsReported_AndItsStringFormIsNot()
    {
        var findings = await ProgramAsync(
            "Option Explicit",
            "Public Sub Main()",
            "    Dim text As String",
            "    text = Left(\"abc\", 1) & Mid$(\"abc\", 2) & UCase(\"d\")",
            "    Debug.Print Hex(255), Hex$(255), Len(text)",
            "End Sub");

        Assert.AreEqual("4:11 4:45 5:16", At(findings));
        Assert.IsTrue(findings.All(finding => finding.Severity == DiagnosticSeverity.Information && finding.Id == RDCoreDiagnosticId.VariantStringFunction));
        StringAssert.Contains(findings[0].Message, "'Left$'");
    }

    [TestMethod]
    public async Task AQualifiedCall_IsReported()
        => Assert.AreEqual("4:16",
            At(await ProgramAsync("Option Explicit", "Public Sub Main()", "    Dim s As String", "    Debug.Print VBA.Trim(\" a \")", "End Sub")));

    // MS-VBAL §5.6.12: a member of a module written with a type character is the member of that name, and not the one without it.
    [TestMethod]
    public async Task AQualifiedStringForm_IsBoundToIt_AndIsAString()
    {
        var program = ModuleWorkspace.StandardModule("Program", "Option Explicit", "Public Sub Main()", "    Debug.Print VBA.Left$(\"abc\", 1)", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        // the member access and the call of it are both bound to the function.
        var facts = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program").Procedures
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Binding?.Fragment is { } bound
                && (bound.EndsWith("Left$", StringComparison.Ordinal) || bound.EndsWith("Left", StringComparison.Ordinal)))
            .ToArray();

        Assert.IsNotEmpty(facts);
        Assert.IsTrue(facts.All(fact => fact.Binding!.Fragment.EndsWith("Left$", StringComparison.Ordinal) && fact.DeclaredType == "String"),
            string.Join("; ", facts.Select(fact => $"{fact.Binding} {fact.DeclaredType}")));
        Assert.IsTrue(facts.All(fact => fact.StringForm is null));
    }

    [TestMethod]
    public async Task DateAndTime_HaveNoStringFormOfTheSameValue()
        => Assert.IsEmpty(await ProgramAsync("Option Explicit", "Public Sub Main()", "    Debug.Print Date, Time, Date$, Time$", "End Sub"));

    [TestMethod]
    public async Task AFunctionOfTheProjectNamedLikeOne_IsNotReported()
        => Assert.IsEmpty(await ProgramAsync(
            "Option Explicit",
            "Public Sub Main()",
            "    Debug.Print Left(1)",
            "End Sub",
            "Private Function Left(ByVal n As Long) As Variant",
            "    Left = n",
            "End Function"));

    [TestMethod]
    public void WithoutTheHostsModel_NothingIsReported()
    {
        var source = ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "    Debug.Print Left(\"a\", 1)", "End Sub");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        Assert.IsEmpty(new VariantStringFunctionAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), null)));
    }

    [TestMethod]
    public async Task TheFix_WritesTheDollarSign_AndTheProgramPrintsTheSame()
    {
        string[] before = ["Option Explicit", "Public Sub Main()", "    Debug.Print Left(\"abc\", 2); VBA.LCase(\"XY\")", "End Sub"];
        string[] after = ["Option Explicit", "Public Sub Main()", "    Debug.Print Left$(\"abc\", 2); VBA.LCase$(\"XY\")", "End Sub"];
        var findings = await ProgramAsync(before);
        Assert.AreEqual("Use 'Left$'", findings[0].Fixes?.Edits.Single().Title);

        var program = ModuleWorkspace.StandardModule("Program", before);
        var lines = program.Split("\r\n");
        int OffsetOf(Position position) => lines.Take(position.Line).Sum(line => line.Length + 2) + position.Character;
        foreach (var edit in findings.SelectMany(finding => finding.Fixes!.Edits.Single().Edits).OrderByDescending(edit => OffsetOf(edit.Range.Start)))
        {
            program = program[..OffsetOf(edit.Range.Start)] + edit.NewText + program[OffsetOf(edit.Range.End)..];
        }

        Assert.AreEqual(ModuleWorkspace.StandardModule("Program", after), program);
        Assert.IsEmpty(await ProgramAsync(after), "the $ forms are not reported");
        CollectionAssert.AreEqual(
            await ModuleWorkspace.RunAsync([], ModuleWorkspace.StandardModule("Program", before)), await ModuleWorkspace.RunAsync([], program));
    }
}
