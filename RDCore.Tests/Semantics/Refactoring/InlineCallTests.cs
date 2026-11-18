using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.LanguageServer.Services.Refactoring.InlineMember;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Tests.Cli;
using RDCore.Tests.Semantics.Flow;

namespace RDCore.Tests.Semantics.Refactoring;

[TestClass]
public class InlineCallTests
{
    // the call written in Run's body as `call`, inlined: its module's text after the edits, or why it is not inlined. The declarations follow Run.
    private static (string Before, string? After, InlineRefusal? Refusal) Inlined(
        string body, string declarations, string call, IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        var analyzed = FlowFixture.Analyze(body, end: $"End Sub\r\n\r\n{declarations}", classes: classes);
        var offset = analyzed.Text.IndexOf(call, analyzed.Text.IndexOf("Public Sub Run", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.IsTrue(offset >= 0, $"no {call} in Run");
        var position = new ExtractionText(analyzed.Text).PositionOf(offset + 1);
        var (inlined, refusal) = InlineCall.At(analyzed.Context, analyzed.AnalysisOf, analyzed.ModuleAddress, position);
        return (analyzed.Text, inlined is null ? null : DeclarationEdits.ApplyTo(analyzed.Text, inlined.Edits), refusal);
    }

    // the program run with the module before and after the call is inlined: Main calls Run 3.
    private static async Task<(string Before, string After, string Module)> BeforeAndAfterAsync(
        string body, string declarations, string call, IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        var (before, after, refusal) = Inlined(body, declarations, call, classes);
        Assert.IsNull(refusal, $"refused: {refusal}");
        return (await RunAsync(before, classes ?? []), await RunAsync(after!, classes ?? []), after!);
    }

    private static async Task<string> RunAsync(string module, IReadOnlyList<(string Name, string Source)> classes)
    {
        var program = module.Replace("Attribute VB_Name = \"Main\"", "Attribute VB_Name = \"Program\"", StringComparison.Ordinal)
            + "\r\nPublic Sub Main()\r\nRun 3\r\nEnd Sub\r\n";
        string? output = null;
        try
        {
            await ModuleWorkspace.InspectAsync(classes, program, (_, result, _) =>
            {
                output = $"{result.Outcome} {result.ErrorNumber}: {string.Join(" | ", result.Output.Select(line => line.Trim()))}";
                return Task.CompletedTask;
            });
        }
        catch (AssertFailedException failed)
        {
            throw new AssertFailedException($"{failed.Message}\r\n{program}", failed);
        }

        return output!;
    }

    [TestMethod]
    public async Task AByValParameter_IsALocalOfItsOwn()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim a As Long\r\na = n\r\nTwice a\r\nDebug.Print a",
            "Private Sub Twice(ByVal x As Long)\r\n    x = x * 2\r\n    Debug.Print x\r\nEnd Sub",
            "Twice a");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Dim x As Long");
    }

    [TestMethod]
    public async Task AByRefParameter_GivenAVariable_IsTheVariable()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim a As Long\r\na = n\r\nAddTen a\r\nDebug.Print a",
            "Private Sub AddTen(ByRef v As Long)\r\n    v = v + 10\r\nEnd Sub",
            "AddTen a");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "a = a + 10");
    }

    [TestMethod]
    public async Task ALocal_WhoseNameTheCallerHas_IsRenamed()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim total As Long\r\ntotal = 100\r\nShowSum n\r\nDebug.Print total",
            "Private Sub ShowSum(ByVal x As Long)\r\n    Dim total As Long\r\n    total = x + 1\r\n    Debug.Print total\r\nEnd Sub",
            "ShowSum n");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Dim total2 As Long");
    }

    [TestMethod]
    public async Task ALocalReadBeforeItIsWritten_IsResetAtEachCall()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim i As Long\r\nFor i = 1 To n\r\n    Accumulate i\r\nNext",
            "Private Sub Accumulate(ByVal x As Long)\r\n    Dim total As Long\r\n    total = total + x\r\n    Debug.Print total\r\nEnd Sub",
            "Accumulate i");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "total = 0");
    }

    [TestMethod]
    public async Task AnArgumentTheBodyNeverReads_IsStillEvaluated()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Ignore Noisy()",
            "Private Function Noisy() As Long\r\n    Debug.Print \"evaluated\"\r\n    Noisy = 1\r\nEnd Function\r\n\r\nPrivate Sub Ignore(ByVal x As Long)\r\n    Debug.Print \"body\"\r\nEnd Sub",
            "Ignore Noisy()");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(before, "evaluated | body");
    }

    [TestMethod]
    public async Task AnExitSub_ThatIsTheLastStatement_IsDropped()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Shout n",
            "Private Sub Shout(ByVal x As Long)\r\n    Debug.Print x\r\n    Exit Sub\r\nEnd Sub",
            "Shout n");
        Assert.AreEqual(before, after, module);
        var run = module.IndexOf("Public Sub Run", StringComparison.Ordinal);
        Assert.IsFalse(module[run..module.IndexOf("End Sub", run, StringComparison.Ordinal)].Contains("Exit Sub", StringComparison.Ordinal), module);
    }

    [TestMethod]
    public async Task AnObjectLocal_IsLetGoOfWhereTheCallEnded()
    {
        var tracker = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Tracker\"\r\nOption Explicit\r\nPrivate Sub Class_Terminate()\r\n    Debug.Print \"terminated\"\r\nEnd Sub\r\n";
        var (before, after, module) = await BeforeAndAfterAsync(
            "Track\r\nDebug.Print \"after\"",
            "Private Sub Track()\r\n    Dim o As Tracker\r\n    Set o = New Tracker\r\n    Debug.Print \"body\"\r\nEnd Sub",
            "Track", classes: [("Tracker", tracker)]);
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(before, "body | terminated | after");
    }

    [TestMethod]
    [DataRow("Private Sub Again(ByVal x As Long)\r\n    If x > 0 Then Again x - 1\r\nEnd Sub", "Again n", (int)InlineRefusalKind.Recursive, DisplayName = "recursive")]
    [DataRow("Private Sub Again(ByVal x As Long)\r\n    If x > 0 Then Other x - 1\r\nEnd Sub\r\nPrivate Sub Other(ByVal x As Long)\r\n    Again x\r\nEnd Sub", "Again n", (int)InlineRefusalKind.Recursive, DisplayName = "recursive through another")]
    [DataRow("Private Static Sub Again(ByVal x As Long)\r\n    Debug.Print x\r\nEnd Sub", "Again n", (int)InlineRefusalKind.Static, DisplayName = "Static procedure")]
    [DataRow("Private Sub Again(ByVal x As Long)\r\n    Static count As Long\r\n    count = count + x\r\nEnd Sub", "Again n", (int)InlineRefusalKind.Static, DisplayName = "Static local")]
    [DataRow("Private Sub Again(ByVal x As Long)\r\n    On Error Resume Next\r\n    Debug.Print 1 / x\r\nEnd Sub", "Again n", (int)InlineRefusalKind.ErrorHandling, DisplayName = "On Error")]
    [DataRow("Private Sub Again(ByVal x As Long)\r\n    If x = 0 Then Exit Sub\r\n    Debug.Print x\r\nEnd Sub", "Again n", (int)InlineRefusalKind.UnsupportedConstruct, DisplayName = "an exit before the end")]
    [DataRow("Private Sub Again(Optional ByVal x As Long = 1)\r\n    Debug.Print x\r\nEnd Sub", "Again n", (int)InlineRefusalKind.UnsupportedParameter, DisplayName = "Optional")]
    [DataRow("Private Sub Again(ByRef x As Long)\r\n    x = 1\r\nEnd Sub\r\nPrivate Sub Caller()\r\n    Dim values(1 To 2) As Long\r\n    Again values(1)\r\nEnd Sub", "Again values(1)", (int)InlineRefusalKind.ByRefElement, DisplayName = "ByRef element")]
    public void TheMember_IsRefused(string declarations, string call, int kind)
    {
        // the call is in Run, unless the declarations have their own caller.
        var body = declarations.Contains("Caller", StringComparison.Ordinal) ? "Debug.Print n" : call;
        var analyzed = FlowFixture.Analyze(body, end: $"End Sub\r\n\r\n{declarations}");
        var text = analyzed.Text;
        var at = body == call ? text.IndexOf(call, text.IndexOf("Public Sub Run", StringComparison.Ordinal), StringComparison.Ordinal) : text.IndexOf(call, StringComparison.Ordinal);
        var (inlined, refusal) = InlineCall.At(analyzed.Context, analyzed.AnalysisOf, analyzed.ModuleAddress, new ExtractionText(text).PositionOf(at + 1));
        Assert.IsNull(inlined);
        Assert.AreEqual((InlineRefusalKind)kind, refusal?.Kind);
    }

    [TestMethod]
    public async Task AFunction_AssignedToAVariable_GivesItsResultLocalToTheAssignment()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim a As Long\r\na = Doubled(n)\r\nDebug.Print a",
            "Private Function Doubled(ByVal x As Long) As Long\r\n    Doubled = x * 2\r\nEnd Function",
            "Doubled(n)");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "a = Doubled_result");
    }

    [TestMethod]
    public async Task AFunction_ThatReadsItsResult_ReadsTheResultLocal()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim a As Long\r\na = Answer\r\nDebug.Print a",
            "Private Function Answer() As Long\r\n    Answer = 41\r\n    Answer = Answer + 1\r\nEnd Function",
            "Answer");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(after, "42");
    }

    [TestMethod]
    public async Task AFunctionOfAnObject_IsSetAssigned()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim items As Collection\r\nSet items = Filled(n)\r\nDebug.Print items.Count",
            "Private Function Filled(ByVal count As Long) As Collection\r\n    Dim i As Long\r\n    Set Filled = New Collection\r\n    For i = 1 To count\r\n        Filled.Add i\r\n    Next\r\nEnd Function",
            "Filled(n)");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Set items = Filled_result");
    }

    [TestMethod]
    public async Task AFunction_CalledAsAStatement_DiscardsItsResult()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Shown n",
            "Private Function Shown(ByVal x As Long) As Long\r\n    Debug.Print x\r\n    Shown = x\r\nEnd Function",
            "Shown n");
        Assert.AreEqual(before, after, module);
    }

    [TestMethod]
    public void AFunction_AssignedToAnElement_IsNotInlined()
        => Assert.AreEqual(InlineRefusalKind.UnsupportedSite, Inlined(
            "Dim values(1 To 2) As Long\r\nvalues(1) = Doubled(n)",
            "Private Function Doubled(ByVal x As Long) As Long\r\n    Doubled = x * 2\r\nEnd Function",
            "Doubled(n)").Refusal?.Kind);
}
