using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00219</c>: a <c>Case</c> block, or the <c>Case Else</c>, that no value of the selector reaches, because the blocks before it match every value it
/// matches, or every value of the selector's type, or because its ranges start above where they end.
/// </summary>
[TestClass]
public sealed class UnreachableCaseAnalyzerTests
{
    // the lines and characters the findings start at, in a module whose Test takes the selector, declared as the type given, and selects on it.
    private static async Task<AnalyzerFinding[]> FindAsync(string type, params string[] cases)
    {
        string[] lines =
        [
            "Attribute VB_Name = \"Program\"", "Option Explicit", "Public Enum Product", "    Widget = 1", "    Gadget", "End Enum",
            "Private Const Limit As Long = 10", "Public Sub Main()", "End Sub", $"Public Sub Test(ByVal n As {type}, ByVal other As Long)",
            "    Select Case n", .. cases.Select(line => $"    {line}"), "    End Select", "End Sub",
        ];
        var source = string.Join("\r\n", lines) + "\r\n";
        var uri = TestUri.TestModuleUri();
        var payload = await ModuleWorkspace.SemanticsAsync([], source);

        var findings = new UnreachableCaseAnalyzer()
            .Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), payload.Modules.Single()))
            .OrderBy(finding => finding.Range.Start)
            .ToArray();
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.UnreachableCase, Severity: DiagnosticSeverity.Warning }));
        return findings;
    }

    // the first line of the cases, in the module FindAsync writes.
    private const int FirstCase = 11;

    // the index of each case the findings are at, among the cases given.
    private static int[] At(AnalyzerFinding[] findings)
    {
        Assert.IsTrue(findings.All(finding => finding.Range.Start.Character == 4), "a finding starts at its Case");
        return [.. findings.Select(finding => finding.Range.Start.Line - FirstCase)];
    }

    [TestMethod]
    public async Task ACaseThatTheCasesBeforeItCover_IsUnreachable()
        => CollectionAssert.AreEqual(new[] { 2, 6 },
            At(await FindAsync("Long", "Case 0 To 99", "    Debug.Print 1", "Case 50", "    Debug.Print 2", "Case Is < 100", "    Debug.Print 3",
                "Case Is < 0", "    Debug.Print 4")));

    [TestMethod]
    public async Task ACaseAfterCasesThatCoverEveryValueOfTheType_IsUnreachable_ItsValuesUnknownOrNot_AndSoIsTheCaseElse()
        => CollectionAssert.AreEqual(new[] { 4, 6, 8 },
            At(await FindAsync("Long", "Case Is < 100", "    Debug.Print 1", "Case Is > -100", "    Debug.Print 2", "Case other * other",
                "    Debug.Print 3", "Case 5", "    Debug.Print 4", "Case Else", "    Debug.Print 5")));

    [TestMethod]
    public async Task ARangeThatStartsAboveWhereItEnds_IsUnreachable_AndSaysSo()
    {
        var findings = await FindAsync("Long", "Case 1 To 5", "    Debug.Print 1", "Case 10 To 1", "    Debug.Print 2", "Case 3", "    Debug.Print 3");

        CollectionAssert.AreEqual(new[] { 2, 4 }, At(findings));
        Assert.AreNotEqual(findings[0].Message, findings[1].Message, "an inverted range is told apart from a covered one");
    }

    [TestMethod]
    [DataRow("Byte", true, DisplayName = "a Byte, whose values are 0 to 255")]
    [DataRow("Integer", false, DisplayName = "an Integer, which has more")]
    public async Task TheCaseElse_IsUnreachable_WhenTheCasesBeforeItCoverEveryValueOfTheSelectorsType(string type, bool unreachable)
        => CollectionAssert.AreEqual(unreachable ? new[] { 2 } : Array.Empty<int>(),
            At(await FindAsync(type, "Case 0 To 255", "    Debug.Print 1", "Case Else", "    Debug.Print 2")));

    [TestMethod]
    public async Task ABooleanSelector_IsTrueOrFalse()
        => CollectionAssert.AreEqual(new[] { 4, 6 },
            At(await FindAsync("Boolean", "Case True", "    Debug.Print 1", "Case False", "    Debug.Print 2", "Case True", "    Debug.Print 3", "Case Else",
                "    Debug.Print 4")));

    // the selector of an Enum is a Long, which holds values the Enum does not name; Gadget, written with no value, is the 2 after Widget.
    [TestMethod]
    public async Task AnEnumSelector_IsALong()
        => CollectionAssert.AreEqual(new[] { 4 },
            At(await FindAsync("Product", "Case Widget", "    Debug.Print 1", "Case Gadget", "    Debug.Print 2", "Case 2", "    Debug.Print 3", "Case Else",
                "    Debug.Print 4")));

    [TestMethod]
    public async Task AConstant_AndANegatedLiteral_AreKnown()
        => CollectionAssert.AreEqual(new[] { 2 }, At(await FindAsync("Long", "Case Is <= Limit", "    Debug.Print 1", "Case -5", "    Debug.Print 2")));

    [TestMethod]
    public async Task ACaseThatIsNot_LeavesOnlyThatValue()
        => CollectionAssert.AreEqual(new[] { 4, 6 },
            At(await FindAsync("Long", "Case Is <> 3", "    Debug.Print 1", "Case 3", "    Debug.Print 2", "Case 4", "    Debug.Print 3", "Case Else",
                "    Debug.Print 4")));

    // a value of another type than the selector's is converted to it first (5.4 to the Long 5, "1" to 1): the analyzer does not say what it becomes.
    [TestMethod]
    [DataRow("Long", "Case 5.4", DisplayName = "a fraction, of a Long selector")]
    [DataRow("Long", "Case \"5\"", DisplayName = "a String, of a Long selector")]
    [DataRow("Integer", "Case 70000", DisplayName = "a Long the Integer cannot hold")]
    [DataRow("Boolean", "Case -1", DisplayName = "a number, of a Boolean selector")]
    [DataRow("Variant", "Case 5", DisplayName = "a Variant selector")]
    [DataRow("String", "Case \"5\"", DisplayName = "a String selector")]
    public async Task AValueOfAnotherTypeThanAnIntegralOrBooleanSelector_IsNotKnown(string type, string second)
        => Assert.IsEmpty(await FindAsync(type, type == "Boolean" ? "Case True" : type == "String" ? "Case \"5\"" : "Case 5", "    Debug.Print 1", second,
            "    Debug.Print 2"));

    [TestMethod]
    public async Task ACaseWithOneValueTheCasesBeforeItDoNotCover_IsReachable()
        => Assert.IsEmpty(await FindAsync("Long", "Case 1 To 5", "    Debug.Print 1", "Case 3, 6", "    Debug.Print 2", "Case 2, other", "    Debug.Print 3"));
}
