using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00128</c>: each variable that no statement declares, in a module with no <c>Option Explicit</c>, which only the host can tell.
/// </summary>
[TestClass]
public sealed class UndeclaredVariableAnalyzerTests
{
    private static string Source(string option, params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\n{option}Private Field As Long\r\nPublic Sub Main()\r\n    {string.Join("\r\n    ", body)}\r\nEnd Sub\r\n";

    private static Task<AnalyzerFinding[]> AnalyzeAsync(string option, params string[] body) => FindingsOfAsync(Source(option, body));

    private static async Task<AnalyzerFinding[]> FindingsOfAsync(string source)
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], source);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");

        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new UndeclaredVariableAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model)).OrderBy(finding => finding.Range.Start)];
    }

    // the fix of the one finding declares the variable before the first statement, and brings nothing the language server reports.
    private static async Task AssertFixedAsync(string option, string[] body, string declaration, string title)
    {
        var program = Source(option, body);
        var fix = (await FindingsOfAsync(program)).Single().Fixes?.Edits.SingleOrDefault();
        Assert.IsNotNull(fix, "the finding carries the fix");
        Assert.AreEqual(title, fix.Title);

        var fixedProgram = EveryAnalyzer.Applied(program, fix);
        // the line is written without indentation, which the language server gives it before it offers the fix.
        Assert.AreEqual(program.Replace("Public Sub Main()\r\n", $"Public Sub Main()\r\n{declaration}\r\n"), fixedProgram);
        Assert.IsEmpty(await FindingsOfAsync(fixedProgram), "the variable is declared");
        await EveryAnalyzer.AssertBringsNothingNewAsync(program, fixedProgram);
    }

    [TestMethod]
    public async Task TheFix_DeclaresAVariant()
        => await AssertFixedAsync("", ["totl = 5", "Debug.Print totl"], "Dim totl As Variant", "Declare 'totl' As Variant, the type it has");

    [TestMethod]
    public async Task TheFix_DeclaresTheTypeOfTheDefTypeDirective_ThatCoversTheName()
        => await AssertFixedAsync("DefLng A-Z\r\n", ["count = 1", "count = count + 1"], "Dim count As Long", "Declare 'count' As Long, the type it has");

    [TestMethod]
    public async Task TheFix_DeclaresTheTypeOfTheCharacter_TheFirstUseIsWrittenWith()
        => await AssertFixedAsync("", ["title$ = \"Report\"", "Debug.Print title$"], "Dim title As String", "Declare 'title' As String, the type it has");

    // Rubberduck's undeclared variable inspection reports both: the misspelling, and the name it was meant for.
    [TestMethod]
    public async Task EachNameThatMatchesNothing_IsAVariableNoStatementDeclares()
    {
        var findings = await AnalyzeAsync("", "totl = 5", "Debug.Print total");

        Assert.HasCount(2, findings);
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.UndeclaredVariable, Severity: DiagnosticSeverity.Warning }));
        StringAssert.Contains(findings[0].Message, "'totl'");
        StringAssert.Contains(findings[1].Message, "'total'");
        Assert.AreEqual(3, findings[0].Range.Start.Line, "where it is first used");
    }

    [TestMethod]
    public async Task AVariableUsedTwice_IsReportedOnce_WhereItIsFirstUsed()
    {
        var finding = (await AnalyzeAsync("", "count = 1", "count = count + 1")).Single();

        Assert.AreEqual(3, finding.Range.Start.Line);
    }

    [TestMethod]
    [DataRow("", "Dim total As Long", "total = 5", DisplayName = "a Dim")]
    [DataRow("", "Field = 5", "", DisplayName = "a variable of the module")]
    [DataRow("", "ReDim totals(3)", "", DisplayName = "a ReDim, which RDC00114 reports")]
    [DataRow("Option Explicit\r\n", "totl = 5", "", DisplayName = "Option Explicit, where the name is a compile error instead")]
    public async Task ADeclaredVariable_OrANameUnderOptionExplicit_IsNotReported(string option, string first, string second)
        => Assert.IsEmpty(await AnalyzeAsync(option, first, second));
}
