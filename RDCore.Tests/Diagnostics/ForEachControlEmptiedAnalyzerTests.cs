using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flow;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11032</c>: the host follows the code paths of each procedure when it loads the code, and finds the reads of a <c>For Each</c> control variable that see
/// it as the loop left it when it ran out: <c>Empty</c>, or <c>Nothing</c>, where MS-VBAL §5.4.2.4 says the last element.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.2.4 For Each Statement")]
public sealed class ForEachControlEmptiedAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindAsync(params string[] lines)
    {
        var uri = TestUri.TestModuleUri();
        var source = string.Join("\r\n", ["Attribute VB_Name = \"Program\"", "Option Explicit", .. lines]) + "\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source);

        // what is not stated of a procedure the pass could not see all of is no evidence: the procedure each row is about is seen whole.
        Assert.IsTrue(payload.Modules.Single().Procedures[0].IsFullyAnalyzed, "the first procedure is analyzed whole");
        return [.. new ForEachControlEmptiedAnalyzer()
            .Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), payload.Modules.Single()))
            .OrderBy(finding => finding.Range.Start)];
    }

    // the lines of a procedure that walks a collection of collections with c, then does what the rest says.
    private static string[] Walk(string declaration, params string[] after) =>
    [
        "Public Sub Walk()",
        "    Dim src As New Collection",
        $"    Dim {declaration}",
        "    For Each c In src",
        "        Debug.Print c.Count",
        "    Next",
        .. after.Select(line => "    " + line),
        "End Sub",
    ];

    [TestMethod]
    public async Task AnObjectReadAfterTheLoopRunsOut_IsAWarning_ThatSaysNothing_AtTheRead()
    {
        var findings = await FindAsync(Walk("c As Collection", "Debug.Print c.Count"));

        Assert.HasCount(1, findings);
        Assert.AreEqual(RDCoreDiagnosticId.ForEachControlEmptiedAfterLoop, findings[0].Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, findings[0].Severity);
        Assert.AreEqual(8, findings[0].Range.Start.Line, "the read after the loop, not the one in it");
        Assert.AreEqual(16, findings[0].Range.Start.Character);
        StringAssert.Contains(findings[0].Message, "'c'");
        StringAssert.Contains(findings[0].Message, "Nothing");
    }

    [TestMethod]
    public async Task AVariantReadAfterTheLoopRunsOut_IsAWarning_ThatSaysEmpty()
    {
        var findings = await FindAsync(Walk("c As Variant", "Debug.Print TypeName(c) & c.Count"));

        Assert.IsTrue(findings.Any(finding => finding.Message.Contains("Empty") && finding.Range.Start.Line == 8), string.Join("; ", findings.Select(finding => finding.Message)));
    }

    [TestMethod]
    public async Task AReadAfterALoopOverAnArray_IsAWarning()
        => Assert.HasCount(1, await FindAsync(
            "Public Sub Walk()",
            "    Dim x As Variant",
            "    For Each x In Array(1, 2)",
            "    Next",
            "    Debug.Print x",
            "End Sub"));

    // the usual search: Exit For when the element is found, then the variable is tested. It holds what the loop exited on, or is Nothing when the loop ran out.
    [TestMethod]
    public async Task AReadThatAnExitForReachesToo_IsNotReported()
        => Assert.IsEmpty(await FindAsync(
            "Public Sub Find()",
            "    Dim src As New Collection",
            "    Dim c As Collection",
            "    For Each c In src",
            "        If c.Count = 2 Then Exit For",
            "    Next",
            "    If Not c Is Nothing Then Debug.Print c.Count",
            "End Sub"));

    [TestMethod]
    public async Task AReadThatAGoToOutOfTheLoopReachesToo_IsNotReported()
        => Assert.IsEmpty(await FindAsync(
            "Public Sub Find()",
            "    Dim src As New Collection",
            "    Dim c As Collection",
            "    For Each c In src",
            "        If c.Count = 2 Then GoTo Found",
            "    Next",
            "Found:",
            "    Debug.Print c Is Nothing",
            "End Sub"));

    [TestMethod]
    public async Task AReadAfterTheVariableIsAssignedAgain_IsNotReported()
        => Assert.IsEmpty(await FindAsync(Walk("c As Collection", "Set c = New Collection", "Debug.Print c.Count")));

    [TestMethod]
    public async Task AReadAfterTheVariableIsPassedToAProcedure_IsNotReported_NorThePassing()
        => Assert.IsEmpty(await FindAsync([.. Walk("c As Collection", "Fill c", "Debug.Print c.Count"),
            "Private Sub Fill(ByRef target As Collection)", "    Set target = New Collection", "End Sub"]));

    [TestMethod]
    public async Task AReadOnOnlyOneBranchAfterTheLoop_IsStillReported()
    {
        var findings = await FindAsync(Walk("c As Collection", "If src.Count > 0 Then", "    Debug.Print c.Count", "End If"));

        Assert.HasCount(1, findings);
        Assert.AreEqual(9, findings[0].Range.Start.Line);
    }

    [TestMethod]
    public async Task AReadOnAPathThatSkipsTheLoop_IsNotReported()
        => Assert.IsEmpty(await FindAsync(
            "Public Sub Walk(ByVal go As Boolean)",
            "    Dim src As New Collection",
            "    Dim c As Collection",
            "    Set c = New Collection",
            "    If go Then",
            "        For Each c In src",
            "        Next",
            "    End If",
            "    Debug.Print c.Count",
            "End Sub"));

    [TestMethod]
    public async Task ASecondLoopOverTheSameVariable_IsNoRead()
        => Assert.IsEmpty(await FindAsync(Walk("c As Collection", "For Each c In src", "    Debug.Print c.Count", "Next c")));

    // what else assigns the variable is not known: a Static local keeps its value from one call to the next, a parameter and a variable of the module are
    // someone else's too.
    [TestMethod]
    public async Task AStaticLocal_AParameter_AndAModuleVariable_AreNotFollowed()
        => Assert.IsEmpty(await FindAsync(
            "Private m As Variant",
            "Public Sub Walk(ByVal p As Variant)",
            "    Static s As Variant",
            "    For Each s In Array(1, 2)",
            "    Next",
            "    Debug.Print s",
            "    For Each p In Array(1, 2)",
            "    Next",
            "    Debug.Print p",
            "    For Each m In Array(1, 2)",
            "    Next",
            "    Debug.Print m",
            "End Sub",
            "Public Static Sub Walk2()",
            "    Dim d As Variant",
            "    For Each d In Array(1, 2)",
            "    Next",
            "    Debug.Print d",
            "End Sub"));

    // an error resumed at the next statement goes on along paths the code paths followed do not have: a Next that fails leaves the loop with an element.
    [TestMethod]
    public async Task AProcedureWithOnErrorResumeNext_IsNotFollowed()
        => Assert.IsEmpty(await FindAsync(Walk("c As Collection", "On Error Resume Next", "Debug.Print c.Count")));

    [TestMethod]
    public async Task AnErrorHandlerReachedFromTheLoop_IsNotReported()
        => Assert.IsEmpty(await FindAsync(
            "Public Sub Walk()",
            "    Dim src As New Collection",
            "    Dim c As Collection",
            "    On Error GoTo Failed",
            "    For Each c In src",
            "        Debug.Print c.Count",
            "    Next",
            "    Exit Sub",
            "Failed:",
            "    Debug.Print c Is Nothing",
            "End Sub"));

    [TestMethod]
    public async Task WithNoFactsFromTheHost_TheAnalyzerWillNotGuess()
    {
        var uri = TestUri.TestModuleUri();
        var source = string.Join("\r\n", ["Attribute VB_Name = \"Program\"", .. Walk("c As Collection", "Debug.Print c.Count")]) + "\r\n";

        Assert.IsEmpty(new ForEachControlEmptiedAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), null)));
        await Task.CompletedTask;
    }

    [TestMethod]
    public void TheFactTravelsTheWire()
    {
        var fact = new EmptiedControlReadFact("c", new SDK.Model.Source.SourceLocation(TestUri.TestModuleUri(), default), IsObject: true);
        var dto = new SDK.Platform.Protocol.ProcedureSemanticsDto(new Uri("file://rdcore-test#Mod1.Walk"), true, [], [], EmptiedControlReads: [fact]);

        var back = SDK.Platform.Protocol.PlatformJson.Deserialize<SDK.Platform.Protocol.ProcedureSemanticsDto>(SDK.Platform.Protocol.PlatformJson.Serialize(dto));

        Assert.AreEqual(fact, back.EmptiedControlReads!.Value.Single());
    }
}
