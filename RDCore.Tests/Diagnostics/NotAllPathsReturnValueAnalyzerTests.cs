using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Semantics.Flow;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00206</c>: the host follows the code paths of each function and property getter when it loads the code, and the analyzer says what is worth saying about the
/// ones that do not assign the return value.
/// </summary>
[TestClass]
public sealed class NotAllPathsReturnValueAnalyzerTests
{
    private const string Source = """
        Attribute VB_Name = "Program"
        Option Explicit
        Public Function Always(ByVal c As Boolean) As Long
            If c Then
                Always = 1
            Else
                Always = 2
            End If
        End Function
        Public Function Sometimes(ByVal c As Boolean) As Long
            If c Then Sometimes = 1
        End Function
        Public Function Never(ByVal c As Boolean) As Long
            Dim x As Long
            x = 1
        End Function
        Public Function Stub(ByVal c As Boolean) As Long
        End Function
        Public Property Get Item(ByVal c As Boolean) As Long
            If c Then Exit Property
            Item = 1
        End Property
        Public Sub Work(ByVal c As Boolean)
            If c Then Exit Sub
        End Sub
        """;

    private static async Task<AnalyzerFinding[]> FindAsync(string source)
    {
        var uri = TestUri.TestModuleUri();
        var payload = await ModuleWorkspace.SemanticsAsync([], source.ReplaceLineEndings("\r\n") + "\r\n");

        return [.. new NotAllPathsReturnValueAnalyzer()
            .Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), payload.Modules.Single()))
            .OrderBy(finding => finding.Range.Start)];
    }

    [TestMethod]
    public async Task AFunctionOrGetterThatDoesNotAssignItsReturnValueOnAPath_IsAWarning_AtItsName()
    {
        var findings = await FindAsync(Source);

        CollectionAssert.AreEqual(new[] { 9, 12, 18 }, findings.Select(finding => finding.Range.Start.Line).ToArray(), "Sometimes, Never and the getter");
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.NotAllPathsReturnValue, Severity: DiagnosticSeverity.Warning }));
        Assert.AreEqual("Sometimes".Length, findings[0].Range.End.Character - findings[0].Range.Start.Character, "the name, and not the whole procedure");
    }

    [TestMethod]
    public async Task AFunctionThatNeverAssignsIt_IsToldApartFromOneThatSometimesDoes()
    {
        var findings = await FindAsync(Source);

        StringAssert.Contains(findings[0].Message, "'Sometimes'");
        StringAssert.Contains(findings[1].Message, "'Never'");
        Assert.AreNotEqual(
            findings[0].Message.Replace("Sometimes", string.Empty), findings[1].Message.Replace("Never", string.Empty), "never assigned is not the same as not on every path");
    }

    [TestMethod]
    public async Task AFunctionThatAssignsItOnEveryPath_AnEmptyBody_AndASub_AreNotReported()
    {
        var findings = await FindAsync(Source);

        Assert.IsFalse(findings.Any(finding => finding.Message.Contains("'Always'") || finding.Message.Contains("'Stub'") || finding.Message.Contains("'Work'")));
    }

    [TestMethod]
    [DataRow("As Long", "    F = 0", DisplayName = "a number")]
    [DataRow("As Date", "    F = 0", DisplayName = "a date")]
    [DataRow("As String", "    F = vbNullString", DisplayName = "a string")]
    [DataRow("As Boolean", "    F = False", DisplayName = "a Boolean")]
    [DataRow("As Variant", "    F = Empty", DisplayName = "a Variant")]
    [DataRow("", "    F = Empty", DisplayName = "no As clause")]
    [DataRow("As Object", "    Set F = Nothing", DisplayName = "an object")]
    public async Task TheFix_AssignsTheDefaultValueBeforeTheFirstStatement(string returns, string assignment)
    {
        var source = $"Attribute VB_Name = \"Program\"\r\nPublic Function F(ByVal c As Boolean) {returns}\r\n    If c Then Exit Function\r\n    Debug.Print c\r\nEnd Function";

        var fix = Assert.ContainsSingle(await FindAsync(source)).Fixes?.Edit;

        Assert.IsNotNull(fix);
        var edit = Assert.ContainsSingle(fix.Edits);
        Assert.AreEqual(edit.Range.Start, edit.Range.End);
        Assert.AreEqual(2, edit.Range.Start.Line, "before the first statement");
        Assert.AreEqual(0, edit.Range.Start.Character);
        Assert.AreEqual($"{assignment}\r\n", edit.NewText);
    }

    [TestMethod]
    [DataRow("Attribute VB_Name = \"Program\"\r\nPublic Type Shape\r\n    Sides As Long\r\nEnd Type\r\nPublic Function F(ByVal c As Boolean) As Shape\r\n    If c Then Exit Function\r\nEnd Function", DisplayName = "a user-defined type")]
    [DataRow("Attribute VB_Name = \"Program\"\r\nPublic Function F(ByVal c As Boolean) As Long()\r\n    If c Then Exit Function\r\nEnd Function", DisplayName = "an array")]
    [DataRow("Attribute VB_Name = \"Program\"\r\nDefLng A-Z\r\nPublic Function F(ByVal c As Boolean)\r\n    If c Then Exit Function\r\nEnd Function", DisplayName = "a type a DefType statement sets")]
    public async Task NoFixIsOffered_WhereTheDefaultHasNoLiteral(string source)
        => Assert.IsNull(Assert.ContainsSingle(await FindAsync(source)).Fixes?.Edit);

    [TestMethod]
    public async Task WithNoFactsFromTheHost_TheAnalyzerWillNotGuess()
    {
        var uri = TestUri.TestModuleUri();
        var parsed = new ModuleParser().Parse(uri, Source);

        Assert.IsEmpty(new NotAllPathsReturnValueAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, null)));
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task ASub_ReturnsNoValue_AndTheHostStatesNothingOfIt()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], Source.ReplaceLineEndings("\r\n") + "\r\n");

        var work = payload.Modules.Single().Procedures.Single(procedure => procedure.Procedure.Fragment.EndsWith(".Work", StringComparison.Ordinal));
        Assert.IsNull(work.ReturnValue);
    }

    [TestMethod]
    public void TheFactTravelsTheWire()
    {
        var fact = new ReturnValueFact(ReturnValueAssignment.Sometimes, "Total", new SDK.Model.Source.SourceLocation(TestUri.TestModuleUri(), default), IsEmpty: false);
        var dto = new SDK.Platform.Protocol.ProcedureSemanticsDto(new Uri("file://rdcore-test#Mod1.Total"), true, [], [], fact);

        var back = SDK.Platform.Protocol.PlatformJson.Deserialize<SDK.Platform.Protocol.ProcedureSemanticsDto>(SDK.Platform.Protocol.PlatformJson.Serialize(dto));

        Assert.AreEqual(fact, back.ReturnValue);
    }
}
