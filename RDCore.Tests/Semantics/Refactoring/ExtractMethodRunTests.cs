using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// An extraction changes how the code reads, never what it does: each program is run before the extraction and after it, in RDCore's runtime, and prints the
/// same, ends the same, and raises the same error. Where the plan says the behaviour changes, the runs show the change it says.
/// </summary>
[TestClass]
public sealed class ExtractMethodRunTests
{
    // the runs before and after the extraction, which must print the same and end the same; what they printed.
    private static async Task<string> SameAsync(
        string body, int first, int last, string driver = "Run 3", string? returnValue = null, string header = "Public Sub Run(ByVal n As Long)",
        string end = "End Sub", IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        var (before, after, module, _) = await ExtractionRuns.BeforeAndAfterAsync(body, first, last, driver, returnValue, header, end, classes: classes);
        Assert.AreEqual(before, after, module);
        return before.Output;
    }

    // the loop RD got wrong: x passed ByVal, the loop never ends.
    [TestMethod]
    public async Task TheLoop_EndsAsItDidInline()
        => Assert.AreEqual("10", await SameAsync("Dim x As Long\nDo While x < 10\nx = x + 1\nLoop\nDebug.Print x", 2, 2));

    [TestMethod]
    public async Task TheSourceFunctionsResult_IsWhatItWasInline()
        => Assert.AreEqual("7", await SameAsync("Run = n * 2\nRun = Run + 1", 0, 1, driver: "Debug.Print Run(3)",
            header: "Public Function Run(ByVal n As Long) As Long", end: "End Function"));

    [TestMethod]
    public async Task AReturnValue_IsWhatTheVariableHeldInline()
        => Assert.AreEqual("6", await SameAsync("Dim y As Long\ny = n * 2\nDebug.Print y", 1, 1, returnValue: "y"));

    [TestMethod]
    public async Task ADeclarationThatMoves_LeavesTheOthersAsTheyWere()
        => Assert.AreEqual("7", await SameAsync("Dim a As Long, b As Long\nb = n * 2\na = b + 1\nDebug.Print a", 1, 2));

    [TestMethod]
    public async Task AnExit_LeavesTheSourceProcedure()
        => Assert.AreEqual("1 | 2 | 1 | 2 | 3 | 4 | 5 | done", await SameAsync(
            "Dim i As Long\nFor i = 1 To 5\nIf i = n Then Exit Sub\nDebug.Print i\nNext\nDebug.Print \"done\"", 2, 3, driver: "Run 3\nRun 9"));

    [TestMethod]
    public async Task SeveralExits_EachLeaveWhatTheyLeftInline()
        => Assert.AreEqual("1 | after | 1 | 2 | 3", await SameAsync(
            "Dim i As Long\nFor i = 1 To 9\nIf i = n Then Exit For\nIf i = 4 Then Exit Sub\nDebug.Print i\nNext\nDebug.Print \"after\"", 2, 4, driver: "Run 2\nRun 7"));

    [TestMethod]
    public async Task AGoToOutOfTheSelection_LandsOnItsLabel()
        => Assert.AreEqual("1 | done", await SameAsync(
            "Dim i As Long\nFor i = 1 To 5\nIf i = n Then GoTo Done\nDebug.Print i\nNext\nDone:\nDebug.Print \"done\"", 2, 3, driver: "Run 2"));

    [TestMethod]
    public async Task ResumeNext_ResumesInsideTheExtractedCode()
        => Assert.AreEqual("next | 2", await SameAsync(
            "Dim x As Long\nOn Error Resume Next\nx = 1 / 0\nDebug.Print \"next\"\nx = 2\nDebug.Print x", 2, 4));

    // the error a resumed statement raised is still the Err the source procedure reads after the selection.
    [TestMethod]
    public async Task ResumeNext_LeavesTheErrorForTheSourceProcedureToRead()
        => Assert.AreEqual("11", await SameAsync(
            "Dim x As Long\nOn Error Resume Next\nx = 1 / 0\nx = 2\nDebug.Print Err.Number", 2, 3));

    // what the plan says changes does: the On Error Resume Next the new procedure starts with clears the error raised before the selection, which it reads.
    [TestMethod]
    public async Task ResumeNext_ClearsTheEarlierErrorTheSelectionReads_AsThePlanSays()
    {
        var (before, after, _, plan) = await ExtractionRuns.BeforeAndAfterAsync("Dim x As Long\nOn Error Resume Next\nx = 1 / 0\nDebug.Print Err.Number", 3, 3);

        Assert.IsTrue(plan.BehaviourChanges.Any(change => change.Kind == ExtractionBehaviourChangeKind.ErrClearedOnEntry && !change.IsNote));
        Assert.AreEqual("11", before.Output);
        Assert.AreEqual("0", after.Output);
    }

    [TestMethod]
    public async Task ASelectionThatRestoresTheHandler_EndsAsItDidInline()
        => Assert.AreEqual("0", await SameAsync(
            "Dim x As Long\nOn Error Resume Next\nx = 1 / 0\nOn Error GoTo 0\nDebug.Print x", 1, 3));

    [TestMethod]
    public async Task AHandlerThatDoesNotResume_CatchesTheErrorAtTheCall_AndThePlanSaysOnlyErlChanges()
    {
        var (before, after, module, plan) = await ExtractionRuns.BeforeAndAfterAsync(
            "On Error GoTo Handler\nDim x As Long\nx = n\nx = x / 0\nDebug.Print \"unreached\"\nExit Sub\nHandler:\nDebug.Print \"caught \" & Err.Number", 2, 3);

        Assert.AreEqual("caught 11", before.Output);
        Assert.AreEqual(before, after, module);
        Assert.IsTrue(plan.BehaviourChanges.All(change => change.IsNote), string.Join(", ", plan.BehaviourChanges));
    }

    // what the plan says changes does: Resume Next resumes after the call, and the rest of the extracted code is skipped.
    [TestMethod]
    public async Task AHandlerThatResumesNext_SkipsTheRestOfTheExtractedCode_AsThePlanSays()
    {
        var (before, after, _, plan) = await ExtractionRuns.BeforeAndAfterAsync(
            "On Error GoTo Handler\nDim x As Long\nx = 1 / 0\nDebug.Print \"rest\"\nDebug.Print \"after\"\nExit Sub\nHandler:\nResume Next", 2, 3);

        Assert.IsTrue(plan.BehaviourChanges.Any(change => change.Kind == ExtractionBehaviourChangeKind.CallerHandlerResumes && !change.IsNote));
        Assert.AreEqual("rest | after", before.Output);
        Assert.AreEqual("after", after.Output);
    }

    [TestMethod]
    public async Task AWithTarget_IsTheRecordTheBlockWrites()
        => Assert.AreEqual("3 4", await SameAsync("Dim p As Point\nWith p\n.X = n\n.Y = .X + 1\nEnd With\nDebug.Print p.X & \" \" & p.Y", 2, 3));

    [TestMethod]
    public async Task AnArray_IsTheArrayTheSelectionWrites()
        => Assert.AreEqual("3 6", await SameAsync("Dim a(2) As Long\na(1) = n\na(2) = a(1) * 2\nDebug.Print a(1) & \" \" & a(2)", 1, 2));

    [TestMethod]
    public async Task ARecord_IsTheRecordTheSelectionWrites()
        => Assert.AreEqual("3 4", await SameAsync("Dim p As Point\np.X = n\np.Y = p.X + 1\nDebug.Print p.X & \" \" & p.Y", 1, 2));

    [TestMethod]
    public async Task AStaticLocal_KeepsItsValueFromCallToCall()
        => Assert.AreEqual("1 | 3", await SameAsync("Static total As Long\ntotal = total + n\nDebug.Print total", 1, 1, driver: "Run 1\nRun 2"));

    [TestMethod]
    public async Task LineNumberedCode_RunsAsItDidInline()
        => Assert.AreEqual("4", await SameAsync("10 Dim x As Long\n20 x = n\n30 x = x + 1\n40 Debug.Print x", 1, 2));

    [TestMethod]
    public async Task AConditionalBlock_RunsItsLiveBranch()
        => Assert.AreEqual("3", await SameAsync("Dim x As Long\n#If True Then\nx = n\n#Else\nx = -n\n#End If\nDebug.Print x", 1, 5));

    [TestMethod]
    public async Task StatementsSeparatedByColons_RunAsTheyDidInline()
        => Assert.AreEqual("4", await SameAsync("Dim x As Long\nx = n: x = x + 1\nDebug.Print x", 1, 1));

    [TestMethod]
    public async Task AStatementContinuedOverSeveralLines_RunsAsItDidInline()
        => Assert.AreEqual("4", await SameAsync("Dim x As Long\nx = n + _\n1\nDebug.Print x", 1, 2));
}
