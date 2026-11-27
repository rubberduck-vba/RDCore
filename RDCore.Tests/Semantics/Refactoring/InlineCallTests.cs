using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.LanguageServer.Services.Refactoring.InlineMember;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Tests.Cli;
using RDCore.Tests.Semantics.Flow;
using System.Globalization;

namespace RDCore.Tests.Semantics.Refactoring;

[TestClass]
public class InlineCallTests
{
    // the call written in Run's body as `call`, inlined: its module's text after the edits, or why it is not inlined. The declarations follow Run.
    private static (string Before, string? After, InlineRefusal? Refusal) Inlined(
        string body, string declarations, string call, IReadOnlyList<(string Name, string Source)>? classes = null, bool classModule = false,
        string moduleLevel = "", IReadOnlyList<(string Name, string Source)>? modules = null)
    {
        var analyzed = FlowFixture.Analyze(
            body, end: $"End Sub\r\n\r\n{declarations}", declarations: moduleLevel, classes: classes, classModule: classModule, modules: modules);
        var offset = analyzed.Text.IndexOf(call, analyzed.Text.IndexOf("Public Sub Run", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.IsTrue(offset >= 0, $"no {call} in Run");
        var position = TextLines.Of(analyzed.Text).PositionOf(offset + 1);
        var (inlined, refusal) = InlineCall.At(analyzed.Context, module => analyzed.AnalysisOf(module.Module), analyzed.ModuleAddress, position);
        return (analyzed.Text, inlined is null ? null : DeclarationEdits.ApplyTo(analyzed.Text, inlined.Edits), refusal);
    }

    // the program run with the module before and after the call is inlined: Main calls Run 3.
    private static async Task<(string Before, string After, string Module)> BeforeAndAfterAsync(
        string body, string declarations, string call, IReadOnlyList<(string Name, string Source)>? classes = null, string moduleLevel = "",
        IReadOnlyList<(string Name, string Source)>? modules = null)
    {
        var (before, after, refusal) = Inlined(body, declarations, call, classes, moduleLevel: moduleLevel, modules: modules);
        Assert.IsNull(refusal, $"refused: {refusal}");
        return (await RunAsync(before, classes ?? [], modules: modules), await RunAsync(after!, classes ?? [], modules: modules), after!);
    }

    private static async Task<string> RunAsync(
        string module, IReadOnlyList<(string Name, string Source)> classes, string main = "Public Sub Main()\r\nRun 3\r\nEnd Sub\r\n",
        IReadOnlyList<(string Name, string Source)>? modules = null)
    {
        var program = (module.Length == 0
                ? "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
                : module.Replace("Attribute VB_Name = \"Main\"", "Attribute VB_Name = \"Program\"", StringComparison.Ordinal))
            + $"\r\n{main}";
        string? output = null;
        try
        {
            await ModuleWorkspace.InspectAsync(classes, program, (_, result, _) =>
            {
                output = $"{result.Outcome} {result.ErrorNumber}: {string.Join(" | ", result.Output.Select(line => line.Trim()))}";
                return Task.CompletedTask;
            }, modules: modules);
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
            "Private Function Noisy() As Long\r\n    Debug.Print \"evaluated\"\r\n    Noisy = 1\r\nEnd Function\r\n\r\n" +
                "Private Sub Ignore(ByVal x As Long)\r\n    Debug.Print \"body\"\r\nEnd Sub",
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
    public async Task AnExit_ThatEndsATopLevelIf_PutsTheRestOfTheBodyInItsElse()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim i As Long\r\nFor i = 0 To n\r\n    Guarded i\r\nNext",
            "Private Sub Guarded(ByVal x As Long)\r\n    If x = 1 Then\r\n        Debug.Print \"one\"\r\n        Exit Sub\r\n" +
                "    End If\r\n    Debug.Print x\r\nEnd Sub",
            "Guarded i");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Else");
        Assert.IsFalse(module.Contains("GoTo", StringComparison.Ordinal), module);
    }

    [TestMethod]
    public async Task AnExit_AnywhereElse_GoesToALabelAfterTheBody()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim i As Long\r\nFor i = 0 To n\r\n    Debug.Print FirstOver(i)\r\nNext\r\n" +
                "Dim found As Long\r\nfound = FirstOver(n)\r\nDebug.Print found",
            "Private Function FirstOver(ByVal limit As Long) As Long\r\n    Dim i As Long\r\n    For i = 1 To 10\r\n        If i * i > limit Then\r\n" +
                "            FirstOver = i\r\n            Exit Function\r\n        End If\r\n    Next\r\n    FirstOver = -1\r\nEnd Function",
            "FirstOver(n)");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "GoTo FirstOver_exit");
    }

    [TestMethod]
    public async Task ASingleLineGuard_GoesToTheLabel()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim i As Long\r\nFor i = 0 To n\r\n    Skipping i\r\nNext",
            "Private Sub Skipping(ByVal x As Long)\r\n    If x = 1 Then Exit Sub\r\n    Debug.Print x\r\nEnd Sub",
            "Skipping i");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "If x = 1 Then GoTo Skipping_exit");
    }

    [TestMethod]
    public async Task ALabel_WhoseNameTheCallerHas_IsRenamed_WithTheJumpsToIt()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim i As Long\r\nAgain:\r\nFor i = 1 To n\r\n    Retry i\r\nNext",
            "Private Sub Retry(ByVal x As Long)\r\n    Dim tries As Long\r\nAgain:\r\n    tries = tries + 1\r\n" +
                "    If tries < x Then GoTo Again\r\n    Debug.Print tries\r\nEnd Sub",
            "Retry i");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Again2:");
        StringAssert.Contains(module, "Then GoTo Again2");
    }

    [TestMethod]
    public async Task TheLabelsOfAnOnGoTo_AreRenamed_InTheirOrder()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim i As Long\r\nFor i = 0 To n\r\n    Picked i\r\nNext\r\nOne:",
            "Private Sub Picked(ByVal x As Long)\r\n    On x GoTo One, Two\r\n    Debug.Print \"neither\"\r\n    Exit Sub\r\n" +
                "One:\r\n    Debug.Print \"one\"\r\n    Exit Sub\r\nTwo:\r\n    Debug.Print \"two\"\r\nEnd Sub",
            "Picked i");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "On x GoTo One2, Two");
    }

    [TestMethod]
    public async Task ALineNumber_IsRemoved_UnlessAJumpGoesToIt()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim i As Long\r\nFor i = 0 To n\r\n    Numbered i\r\nNext",
            "Private Sub Numbered(ByVal x As Long)\r\n10 Debug.Print x\r\n20 If x = 1 Then 40\r\n" +
                "30 Debug.Print \"not one\"\r\n40 Debug.Print \"done\"\r\nEnd Sub",
            "Numbered i");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "If x = 1 Then GoTo Numbered_40");
        StringAssert.Contains(module, "Numbered_40: Debug.Print \"done\"");
        var run = module[module.IndexOf("Public Sub Run", StringComparison.Ordinal)..module.IndexOf("Private Sub Numbered", StringComparison.Ordinal)];
        Assert.IsFalse(run.Contains("10 Debug", StringComparison.Ordinal) || run.Contains("30 Debug", StringComparison.Ordinal), run);
    }

    [TestMethod]
    public async Task AnOmittedOptional_HasItsDefault_OrItsTypesDefault()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim i As Long\r\nFor i = 1 To n\r\n    Greet\r\nNext",
            "Private Sub Greet(Optional ByVal times As Long = 2, Optional ByVal extra As Long)\r\n" +
                "    Debug.Print times + extra\r\n    extra = extra + 1\r\nEnd Sub",
            "Greet");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "times = 2");
        StringAssert.Contains(module, "extra = 0");
    }

    [TestMethod]
    public async Task IsMissing_OfAnOmittedVariant_IsTrue()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim i As Long\r\nFor i = 1 To n\r\n    Told\r\nNext",
            "Private Sub Told(Optional ByVal v As Variant)\r\n    If IsMissing(v) Then Debug.Print \"none\" Else Debug.Print \"some\"\r\nEnd Sub",
            "Told");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "If True Then");
    }

    [TestMethod]
    public async Task IsMissing_OfAGivenArgument_IsFalse()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim i As Long\r\nFor i = 1 To n\r\n    Told i\r\nNext",
            "Private Sub Told(Optional ByVal v As Variant)\r\n    If IsMissing(v) Then Debug.Print \"none\" Else Debug.Print v\r\nEnd Sub",
            "Told i");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "If False Then");
    }

    [TestMethod]
    public async Task AParamArray_IsAVariantArrayFromZero_OfTheArgumentsItCollects()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim t As Long\r\nt = Total(1, n, 3)\r\nDebug.Print t",
            "Private Function Total(ParamArray values() As Variant) As Long\r\n    Dim i As Long\r\n    For i = LBound(values) To UBound(values)\r\n" +
                "        Total = Total + values(i)\r\n    Next\r\n    Debug.Print LBound(values), UBound(values)\r\nEnd Function",
            "Total(1, n, 3)");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "ReDim values(0 To 2)");
    }

    [TestMethod]
    public async Task AnEmptyParamArray_IsMissing()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Listed\r\nListed n",
            "Private Sub Listed(ParamArray items())\r\n    If IsMissing(items) Then Debug.Print \"none\" Else Debug.Print UBound(items)\r\nEnd Sub",
            "Listed\r\n");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "If True Then");
        StringAssert.Contains(module, "items = Array()");
    }

    // a listener of the class Source, which is the edited class module: Main runs Go, which runs Source's Run 3, and the handler writes its ByRef
    // argument.
    private const string Listener = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Listener\"\r\nOption Explicit\r\n" +
        "Private WithEvents s As Source\r\nPublic Sub Go()\r\n    Set s = New Source\r\n    s.Run 3\r\nEnd Sub\r\n" +
        "Private Sub s_Changed(x As Long)\r\n    Debug.Print \"changed \" & x\r\n    x = x + 100\r\nEnd Sub\r\n";

    private static Task<string> RunAsSourceAsync(string module)
        => RunAsync(string.Empty,
            [("Source", module.Replace("Attribute VB_Name = \"Main\"", "Attribute VB_Name = \"Source\"", StringComparison.Ordinal)), ("Listener", Listener)],
            "Public Sub Main()\r\nDim l As New Listener\r\nl.Go\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task ARaiseEvent_CalledInItsClass_RaisesTheEventOfTheSameObject()
    {
        var (before, after, refusal) = Inlined(
            "Dim a As Long\r\na = n\r\nNotify a\r\nDebug.Print a",
            "Private Sub Notify(ByVal x As Long)\r\n    RaiseEvent Changed(x)\r\n    Debug.Print x\r\nEnd Sub",
            "Notify a", classModule: true, moduleLevel: "Public Event Changed(x As Long)");
        Assert.IsNull(refusal, $"refused: {refusal}");
        var ran = await RunAsSourceAsync(before);
        StringAssert.Contains(ran, "changed 3 | 103 | 3");
        Assert.AreEqual(ran, await RunAsSourceAsync(after!), after);
    }

    [TestMethod]
    public void ACallOfAnObject_InTheMembersOwnClass_IsNotInlined()
    {
        var (_, _, refusal) = Inlined(
            "Dim other As Main\r\nSet other = New Main\r\nother.Notify n",
            "Public Sub Notify(ByVal x As Long)\r\n    RaiseEvent Changed(x)\r\nEnd Sub",
            "Notify n", classModule: true, moduleLevel: "Public Event Changed(x As Long)");
        Assert.AreEqual(InlineRefusalKind.UnsupportedSite, refusal?.Kind);
    }

    [TestMethod]
    public void ARaiseEvent_CalledFromAnotherModule_IsRefused()
    {
        var source = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Source\"\r\nOption Explicit\r\n" +
            "Public Event Changed(ByVal x As Long)\r\nPublic Sub Notify(ByVal x As Long)\r\n    RaiseEvent Changed(x)\r\nEnd Sub\r\n";
        var (_, _, refusal) = Inlined("Dim s As New Source\r\ns.Notify n", string.Empty, "Notify n", classes: [("Source", source)]);
        Assert.AreEqual(InlineRefusalKind.RaisesAnEvent, refusal?.Kind);
    }

    [TestMethod]
    public async Task AnObjectLocal_IsLetGoOfWhereTheCallEnded()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Track\r\nDebug.Print \"after\"",
            "Private Sub Track()\r\n    Dim o As Tracker\r\n    Debug.Print \"body\"\r\n    Set o = New Tracker\r\nEnd Sub",
            "Track", classes: [("Tracker", Tracker)]);
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(before, "body | terminated | after");
        StringAssert.Contains(module, "Set o = Nothing");
    }

    private const string Tracker = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Tracker\"\r\nOption Explicit\r\n" +
        "Private Sub Class_Terminate()\r\n    Debug.Print \"terminated\"\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task AVariant_GivenAnArrayOfObjects_IsLetGoOfWhereTheCallEnded()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Track\r\nDebug.Print \"after\"",
            "Private Sub Track()\r\n    Dim v As Variant\r\n    Debug.Print \"body\"\r\n    v = Array(New Tracker)\r\nEnd Sub",
            "Track", classes: [("Tracker", Tracker)]);
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(before, "body | terminated | after");
        StringAssert.Contains(module, "v = Empty");
    }

    [TestMethod]
    public void ALocalNeverGivenAnObject_IsNotLetGoOf()
    {
        var (_, after, refusal) = Inlined(
            "Track",
            "Private Sub Track()\r\n    Dim v As Variant\r\n    Dim o As Tracker\r\n    v = 1\r\n    Debug.Print v, o Is Nothing\r\nEnd Sub",
            "Track", [("Tracker", Tracker)]);
        Assert.IsNull(refusal, refusal?.ToString());

        // o is read before it is written, and so reset before the body; nothing is let go of after it.
        var last = after!.IndexOf("Debug.Print v, o Is Nothing", StringComparison.Ordinal);
        var tail = after[last..after.IndexOf("End Sub", last, StringComparison.Ordinal)];
        Assert.IsFalse(tail.Contains("v = Empty", StringComparison.Ordinal) || tail.Contains("Set o = Nothing", StringComparison.Ordinal), after);
    }

    [TestMethod]
    public async Task AnObjectLocal_HeldWhenAnErrorLeavesTheBody_IsRefused_ForItWasLetGoOfBeforeTheHandlerRan()
    {
        const string body = "On Error GoTo Handler\r\nTrack n - 3\r\nExit Sub\r\nHandler:\r\nDebug.Print \"handled\"";
        const string declarations = "Private Sub Track(ByVal x As Long)\r\n    Dim o As Tracker\r\n" +
            "    Set o = New Tracker\r\n    Debug.Print 1 / x\r\nEnd Sub";
        var (before, _, refusal) = Inlined(body, declarations, "Track n - 3", [("Tracker", Tracker)]);

        Assert.AreEqual(InlineRefusalKind.HeldThroughAnError, refusal?.Kind);
        StringAssert.Contains(await RunAsync(before, [("Tracker", Tracker)]), "terminated | handled");
    }

    [TestMethod]
    public async Task ACallUnderAHandlerThatDoesNotResume_IsInlined()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "On Error GoTo Handler\r\nShown n - 3\r\nDebug.Print \"done\"\r\nExit Sub\r\nHandler:\r\nDebug.Print \"handled\"",
            "Private Sub Shown(ByVal x As Long)\r\n    Debug.Print 10 / x\r\n    Debug.Print \"rest\"\r\nEnd Sub",
            "Shown n - 3");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(before, "handled");
    }

    [TestMethod]
    [DataRow("On Error Resume Next\r\nShown n - 3\r\nDebug.Print \"done\"", (int)InlineRefusalKind.CallerResumesNext,
        DisplayName = "under On Error Resume Next")]
    [DataRow("On Error GoTo Handler\r\nShown n - 3\r\nExit Sub\r\nHandler:\r\nResume Next", (int)InlineRefusalKind.CallerHandlerResumes,
        DisplayName = "a handler that resumes")]
    public void ACallWhoseErrorTheCallerResumesFrom_IsRefused(string body, int kind)
    {
        var (_, _, refusal) = Inlined(
            body, "Private Sub Shown(ByVal x As Long)\r\n    Debug.Print 10 / x\r\n    Debug.Print \"rest\"\r\nEnd Sub", "Shown n - 3");
        Assert.AreEqual((InlineRefusalKind)kind, refusal?.Kind);
    }

    [TestMethod]
    public void ABodyThatCannotRaise_IsInlined_UnderOnErrorResumeNext()
    {
        var (_, _, refusal) = Inlined("On Error Resume Next\r\nCounted", "Private Sub Counted()\r\n    Dim k As Long\r\n    k = 1\r\nEnd Sub", "Counted");
        Assert.IsNull(refusal, refusal?.ToString());
    }

    [TestMethod]
    public void TwoObjectsLetGoOfAtTheEnd_AreRefused_UntilTheOrderIsKnown()
    {
        var (_, _, refusal) = Inlined(
            "Pair New Tracker, New Tracker",
            "Private Sub Pair(ByVal first As Tracker, ByVal second As Tracker)\r\n    Dim k As Long\r\n    k = 1\r\nEnd Sub",
            "Pair New Tracker", [("Tracker", Tracker)]);
        Assert.AreEqual(InlineRefusalKind.ReleaseOrder, refusal?.Kind);
    }

    [TestMethod]
    [Ignore("not implemented: a call whose procedure lets go of two objects at its end; the order MS-VBA lets go of them in is not measured.")]
    public async Task TwoObjectsLetGoOfAtTheEnd_AreLetGoOfInTheOrderTheCalleeLetGoOfThem()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Pair New Tracker, New Tracker\r\nDebug.Print \"after\"",
            "Private Sub Pair(ByVal first As Tracker, ByVal second As Tracker)\r\n    Dim k As Long\r\n    k = 1\r\nEnd Sub",
            "Pair New Tracker", [("Tracker", Tracker)]);
        Assert.AreEqual(before, after, module);
    }

    [TestMethod]
    [DataRow("Private Sub Again(ByVal x As Long)\r\n    If x > 0 Then Again x - 1\r\nEnd Sub", "Again n", (int)InlineRefusalKind.Recursive,
        DisplayName = "recursive")]
    [DataRow("Private Sub Again(ByVal x As Long)\r\n    If x > 0 Then Other x - 1\r\nEnd Sub\r\n" +
        "Private Sub Other(ByVal x As Long)\r\n    Again x\r\nEnd Sub", "Again n", (int)InlineRefusalKind.Recursive,
        DisplayName = "recursive through another")]
    [DataRow("Private Static Sub Again(ByVal x As Long)\r\n    Debug.Print x\r\nEnd Sub", "Again n", (int)InlineRefusalKind.Static,
        DisplayName = "Static procedure")]
    [DataRow("Private Sub Again(ByVal x As Long)\r\n    Static count As Long\r\n    count = count + x\r\nEnd Sub", "Again n", (int)InlineRefusalKind.Static,
        DisplayName = "Static local")]
    [DataRow("Private Sub Again(ByVal x As Long)\r\n    On Error Resume Next\r\n    Debug.Print 1 / x\r\nEnd Sub", "Again n",
        (int)InlineRefusalKind.ErrorHandling, DisplayName = "On Error")]
    [DataRow("Private Sub Again(Optional ByVal x As Variant)\r\n    Debug.Print x\r\nEnd Sub", "Again\r\n", (int)InlineRefusalKind.UnsupportedParameter,
        DisplayName = "an omitted Optional Variant read")]
    [DataRow("Private Sub Again(ParamArray x())\r\n    x(0) = Empty\r\nEnd Sub", "Again n", (int)InlineRefusalKind.UnsupportedParameter,
        DisplayName = "a ParamArray written")]
    [DataRow("Private Sub Again(ByVal x As Long, Optional ByVal y As Variant)\r\n    y = x\r\n    Debug.Print IsMissing(y)\r\nEnd Sub", "Again n, 1",
        (int)InlineRefusalKind.UnsupportedParameter, DisplayName = "IsMissing of a parameter written")]
    [DataRow("Private Sub Again(ByRef x As Long)\r\n    x = 1\r\nEnd Sub\r\n" +
        "Private Sub Caller()\r\n    Dim values(1 To 2) As Long\r\n    Again values(1)\r\nEnd Sub", "Again values(1)", (int)InlineRefusalKind.ByRefElement,
        DisplayName = "ByRef element")]
    [DataRow("Private Sub Again(ByVal x As Long)\r\n    field = field + x\r\nEnd Sub\r\n" +
        "Private Sub Caller()\r\n    Dim field As Long\r\n    Again field\r\nEnd Sub", "Again field", (int)InlineRefusalKind.Captured,
        DisplayName = "a module field a caller local captures")]
    public void TheMember_IsRefused(string declarations, string call, int kind)
    {
        // the call is in Run, unless the declarations have their own caller.
        var body = declarations.Contains("Caller", StringComparison.Ordinal) ? "Debug.Print n" : call;
        var analyzed = FlowFixture.Analyze(body, end: $"End Sub\r\n\r\n{declarations}");
        var text = analyzed.Text;
        var at = body == call
            ? text.IndexOf(call, text.IndexOf("Public Sub Run", StringComparison.Ordinal), StringComparison.Ordinal)
            : text.IndexOf(call, StringComparison.Ordinal);
        var (inlined, refusal) = InlineCall.At(
            analyzed.Context, module => analyzed.AnalysisOf(module.Module), analyzed.ModuleAddress, TextLines.Of(text).PositionOf(at + 1));
        Assert.IsNull(inlined);
        Assert.AreEqual((InlineRefusalKind)kind, refusal?.Kind);
    }

    [TestMethod]
    public async Task AFunction_AssignedToAVariable_GivesItsResultLocalToTheAssignment()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim a As Long\r\na = Doubled(n)\r\nDebug.Print a",
            Stepwise,
            "Doubled(n)");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "a = Doubled_result");
    }

    // a function the expression form cannot write: its body is more than the assignment of its result.
    private const string Stepwise = "Private Function Doubled(ByVal x As Long) As Long\r\n" +
        "    Dim twice As Long\r\n    twice = x * 2\r\n    Doubled = twice\r\nEnd Function";

    private const string OneLiner = "Private Function Doubled(ByVal x As Long) As Long\r\n    Doubled = x * 2\r\nEnd Function";

    [TestMethod]
    public async Task ACallInAnExpression_OfAOneLiner_IsItsValueInParentheses()
    {
        var (before, after, module) = await BeforeAndAfterAsync("Debug.Print Doubled(n) + 1", OneLiner, "Doubled(n)");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Debug.Print (n * 2) + 1");
    }

    [TestMethod]
    public async Task ALiteralArgument_OfAnotherType_IsWrittenOfTheParametersType()
    {
        // 20000 is an Integer, which doubled in Integer arithmetic overflows; given to a Long parameter, it is doubled as a Long.
        var (before, after, module) = await BeforeAndAfterAsync("Debug.Print Doubled(20000)", OneLiner, "Doubled(20000)");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Debug.Print (20000& * 2)");
        StringAssert.Contains(before, "40000");
    }

    [TestMethod]
    public async Task ACallInALoopCondition_OfAOneLiner_IsInlined()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim i As Long\r\nDo While Doubled(i) < 7\r\n    i = i + 1\r\nLoop\r\nDebug.Print i", OneLiner, "Doubled(i)");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Do While (i * 2) < 7");
    }

    [TestMethod]
    [DataRow("Debug.Print Doubled(n) + 1", "Doubled(n)", false, "Debug.Print Doubled_result + 1", DisplayName = "a body of more than its result")]
    [DataRow("Debug.Print Doubled(G()) + 1", "Doubled(G())", true, "x = G()", DisplayName = "an argument that is a call")]
    [DataRow("Dim i As Long\r\nFor i = 1 To Doubled(n)\r\n    Debug.Print i\r\nNext", "Doubled(n)", false, "For i = 1 To Doubled_result",
        DisplayName = "a For loop's end")]
    public async Task ACallInAnExpression_TheExpressionFormCantWrite_IsHoisted(string body, string call, bool oneLiner, string written)
    {
        var (before, after, module) = await BeforeAndAfterAsync(body, oneLiner ? OneLiner : Stepwise, call);
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, written);
    }

    private const string Bumped = "Private Function Bumped(ByVal x As Long) As Long\r\n    field = field + 1\r\n    Bumped = x + field\r\nEnd Function";

    [TestMethod]
    [DataRow("Debug.Print G() + Doubled(n)", Stepwise, "Doubled(n)", (int)InlineRefusalKind.EvaluationOrder, DisplayName = "another call in the statement")]
    [DataRow("Debug.Print field + Bumped(n)", Bumped, "Bumped(n)", (int)InlineRefusalKind.EvaluationOrder, DisplayName = "a module variable the body writes")]
    [DataRow("Dim i As Long\r\nDo While Doubled(i) < 7\r\n    i = i + 1\r\nLoop", Stepwise, "Doubled(i)", (int)InlineRefusalKind.UnsupportedSite,
        DisplayName = "a loop's condition")]
    [DataRow("If n = 0 Then\r\n    Debug.Print 0\r\nElseIf Doubled(n) > 4 Then\r\n    Debug.Print 1\r\nEnd If", Stepwise, "Doubled(n)",
        (int)InlineRefusalKind.UnsupportedSite, DisplayName = "an ElseIf condition")]
    public void ACallInAnExpression_ThatCantBeHoisted_IsRefused(string body, string declarations, string call, int kind)
        => Assert.AreEqual((InlineRefusalKind)kind, Inlined(body, declarations, call).Refusal?.Kind);

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
            "Private Function Filled(ByVal count As Long) As Collection\r\n    Debug.Print count\r\n    Set Filled = New Collection\r\nEnd Function",
            "Filled(n)");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Set items = Filled_result");
        StringAssert.Contains(module, "Set Filled_result = Nothing");
    }

    [TestMethod]
    public void AnObjectTheBodyHoldsWhereItCanRaise_IsRefused()
    {
        var (_, _, refusal) = Inlined(
            "Dim items As Collection\r\nSet items = Filled(n)",
            "Private Function Filled(ByVal count As Long) As Collection\r\n    Dim i As Long\r\n    Set Filled = New Collection\r\n" +
                "    For i = 1 To count\r\n        Filled.Add i\r\n    Next\r\nEnd Function",
            "Filled(n)");
        Assert.AreEqual(InlineRefusalKind.HeldThroughAnError, refusal?.Kind);
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
    public void AFunction_AssignedToAnElement_IsNotInlined_InTheStatementForm()
        => Assert.AreEqual(InlineRefusalKind.UnsupportedSite,
            Inlined("Dim values(1 To 2) As Long\r\nvalues(1) = Doubled(n)", Stepwise, "Doubled(n)").Refusal?.Kind);

    [TestMethod]
    public async Task AOneLiner_AssignedToAnElement_IsItsValue()
    {
        var (before, after, module) = await BeforeAndAfterAsync(
            "Dim values(1 To 2) As Long\r\nvalues(1) = Doubled(n)\r\nDebug.Print values(1)", OneLiner, "Doubled(n)");
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "values(1) = (n * 2)");
    }

    [TestMethod]
    public void EveryRefusal_IsWrittenInEnglishAndInFrench()
    {
        foreach (var kind in Enum.GetValues<InlineRefusalKind>())
        {
            var english = new InlineRefusal(kind, "F").ToMessage(culture: CultureInfo.InvariantCulture);
            var french = new InlineRefusal(kind, "F").ToMessage(culture: CultureInfo.GetCultureInfo("fr-CA"));
            Assert.AreNotEqual($"InlineMember_{kind}", english, $"{kind} has no text");
            Assert.AreNotEqual(english, french, $"{kind} has no French text");
        }
    }

    // a standard module Helpers beside Main.
    private static (string Name, string Source) Helpers(params string[] lines)
        => ("Helpers", ModuleWorkspace.StandardModule("Helpers", ["Option Explicit", .. lines]));

    [TestMethod]
    public async Task APublicProcedureOfAnotherModule_IsInlined_ItsNamesAsTheyAre()
    {
        var (before, after, module) = await BeforeAndAfterAsync("Shout n", string.Empty, "Shout n",
            modules: [Helpers(
                "Public Total As Long", "Public Sub Shout(ByVal x As Long)", "    Total = Total + x", "    Debug.Print x * 2; Total", "End Sub")]);
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Total = Total + x");
        Assert.IsFalse(module.Contains("Helpers.Total", StringComparison.Ordinal), module);
    }

    [TestMethod]
    public async Task APublicVariableOfTheMembersModule_ThatTheCallersModuleDeclaresToo_IsQualified()
    {
        var (before, after, module) = await BeforeAndAfterAsync("Counter = 100\r\nTick\r\nDebug.Print Counter", string.Empty, "Tick",
            moduleLevel: "Private Counter As Long",
            modules: [Helpers("Public Counter As Long", "Public Sub Tick()", "    Counter = Counter + 1", "    Debug.Print Counter", "End Sub")]);
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(before, "1 | 100");
        StringAssert.Contains(module, "Helpers.Counter = Helpers.Counter + 1");
    }

    [TestMethod]
    public void APrivateDeclarationOfTheMembersModule_IsRefused_InAnotherModule()
    {
        var (_, _, refusal) = Inlined("Tick", string.Empty, "Tick",
            modules: [Helpers("Private Counter As Long", "Public Sub Tick()", "    Counter = Counter + 1", "End Sub")]);
        Assert.AreEqual(InlineRefusalKind.PrivateToItsModule, refusal?.Kind);
        Assert.AreEqual("Counter", refusal?.Detail);
    }

    [TestMethod]
    public void AMemberOfAModuleWithAnotherOptionCompare_IsRefused()
    {
        var (_, _, refusal) = Inlined("Same \"a\"", string.Empty, "Same \"a\"",
            modules: [Helpers("Option Compare Text", "Public Sub Same(ByVal text As String)", "    Debug.Print text = \"A\"", "End Sub")]);
        Assert.AreEqual(InlineRefusalKind.OptionCompare, refusal?.Kind);
    }

    [TestMethod]
    public void AnArrayWithoutALowerBound_UnderAnotherOptionBase_IsRefused()
    {
        var (_, _, refusal) = Inlined("Fill", string.Empty, "Fill",
            modules: [Helpers("Option Base 1", "Public Sub Fill()", "    Dim values(3) As Long", "    Debug.Print LBound(values)", "End Sub")]);
        Assert.AreEqual(InlineRefusalKind.OptionBase, refusal?.Kind);
    }

    [TestMethod]
    public async Task ABodyWithoutArrays_UnderAnotherOptionBase_IsInlined()
    {
        var (before, after, module) = await BeforeAndAfterAsync("Hello", string.Empty, "Hello",
            modules: [Helpers("Option Base 1", "Public Sub Hello()", "    Debug.Print \"hello\"", "End Sub")]);
        Assert.AreEqual(before, after, module);
    }

    private static readonly string[] Logger =
    [
        "#Const Verbose = True", "Public Sub Log(ByVal x As Long)",
        "#If Verbose Then", "    Debug.Print \"verbose\"; x", "#Else", "    Debug.Print x", "#End If",
        "End Sub",
    ];

    [TestMethod]
    public void AnIfBlock_UnderOtherConstants_IsRefused()
        => Assert.AreEqual(InlineRefusalKind.ConditionalConstants, Inlined("Log n", string.Empty, "Log n", modules: [Helpers(Logger)]).Refusal?.Kind);

    [TestMethod]
    public async Task AnIfBlock_UnderTheSameConstants_IsInlined()
    {
        var (before, after, module) =
            await BeforeAndAfterAsync("Log n", string.Empty, "Log n", moduleLevel: "#Const Verbose = True", modules: [Helpers(Logger)]);
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(before, "verbose");
    }

    [TestMethod]
    public async Task ACallOnMe_InTheMembersOwnClass_IsInlined()
    {
        var (before, after, refusal) = Inlined(
            "Dim a As Long\r\na = n\r\nMe.Notify a\r\nDebug.Print a",
            "Public Sub Notify(ByVal x As Long)\r\n    RaiseEvent Changed(x)\r\n    Debug.Print x\r\nEnd Sub",
            "Notify a", classModule: true, moduleLevel: "Public Event Changed(x As Long)");
        Assert.IsNull(refusal, $"refused: {refusal}");
        Assert.IsFalse(after!.Contains("Me.Notify", StringComparison.Ordinal), after);
        var ran = await RunAsSourceAsync(before);
        StringAssert.Contains(ran, "changed 3 | 103 | 3");
        Assert.AreEqual(ran, await RunAsSourceAsync(after), after);
    }
}
