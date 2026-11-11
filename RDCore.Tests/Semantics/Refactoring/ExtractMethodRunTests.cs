using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// Each program is run in RDCore's runtime before the extraction and after it. Where the plan reports no behaviour change, both runs print the same, end the
/// same and raise the same error; where it reports one, the runs show that change.
/// </summary>
[TestClass]
public sealed class ExtractMethodRunTests
{
    // the runs before and after the extraction, which must print the same and end the same; what they printed.
    private static async Task<string> SameAsync(
        string body, int first, int last, string driver = "Run 3", string? returnValue = null, string header = "Public Sub Run(ByVal n As Long)",
        string end = "End Sub", IReadOnlyList<(string Name, string Source)>? classes = null, bool classModule = false)
    {
        var (before, after, module, _) = await ExtractionRuns.BeforeAndAfterAsync(body, first, last, driver, returnValue, header, end, classes: classes, classModule: classModule);
        Assert.AreEqual(before, after, module);
        return before.Output;
    }

    // x is the loop's counter: the new procedure must take it ByRef, or the loop never ends.
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

    // the change the plan reports happens: the On Error Resume Next the new procedure starts with clears the error raised before the selection, which it reads.
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

    // the change the plan reports happens: Resume Next resumes after the call, and the rest of the extracted code is skipped.
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

    // Worker's Run, in a class module: the new member is the class's, called on the same object, so the field it writes and the members it calls are that
    // object's.
    [TestMethod]
    public async Task InAClassModule_TheFieldsAndMembers_AreThoseOfTheSameObject()
        => Assert.AreEqual("44 | 40", await SameAsync("field = n\nBump field\nDebug.Print Me.Total + field", 0, 2,
            driver: "Dim w As New Worker\nw.Run 3\nDebug.Print w.Total", end: "End Sub\nPublic Function Total() As Long\nTotal = field * 10\nEnd Function",
            classModule: true));

    // each object has its field: the new member writes the one of the object whose Run calls it.
    [TestMethod]
    public async Task InAClassModule_EachObject_KeepsItsOwnField()
        => Assert.AreEqual("4 | 2", await SameAsync("field = field + n", 0, 0,
            driver: "Dim a As New Worker\nDim b As New Worker\na.Run 1\nb.Run 2\na.Run 3\nDebug.Print a.Total\nDebug.Print b.Total",
            end: "End Sub\nPublic Function Total() As Long\nTotal = field\nEnd Function", classModule: true));

    // Me, given to a variable the selection declares and the code after it reads: the new member gives back the same object.
    [TestMethod]
    public async Task InAClassModule_MeGivenToAVariable_IsTheSameObject()
        => Assert.AreEqual("True | 30", await SameAsync("Dim w As Object\nSet w = Me\nfield = n * 10\nDebug.Print w Is Me\nDebug.Print w.Total", 0, 2,
            driver: "Dim w As New Worker\nw.Run 3", end: "End Sub\nPublic Function Total() As Long\nTotal = field\nEnd Function", classModule: true));

    [TestMethod]
    public async Task APropertyGet_AssignsItsValueAsItDidInline()
        => Assert.AreEqual("7", await SameAsync("Run = 3 * 2\nRun = Run + 1", 0, 1, driver: "Debug.Print Run",
            header: "Public Property Get Run() As Long", end: "End Property"));

    [TestMethod]
    public async Task APropertyGet_OfAClassModule_IsThatObjectsValue()
        => Assert.AreEqual("9", await SameAsync("field = 4\nRun = field * 2\nRun = Run + 1", 0, 2, driver: "Dim w As New Worker\nDebug.Print w.Run",
            header: "Public Property Get Run() As Long", end: "End Property", classModule: true));

    [TestMethod]
    public async Task APropertyLet_WritesWhatItWroteInline()
        => Assert.AreEqual("13", await SameAsync("Dim t As Long\nt = v * 3\nfield = t + 1", 0, 2, driver: "Dim w As New Worker\nw.Run = 4\nDebug.Print w.Run",
            header: "Public Property Let Run(ByVal v As Long)", end: "End Property\nPublic Property Get Run() As Long\nRun = field\nEnd Property", classModule: true));

    // the change the plan reports happens: the object the moved variable holds terminates when the new procedure ends, before what the source procedure
    // prints after the call.
    [TestMethod]
    public async Task AnObjectWhoseVariableMovesIn_TerminatesWhenTheNewProcedureEnds_AsThePlanSays()
    {
        var (before, after, _, plan) = await ExtractionRuns.BeforeAndAfterAsync(
            "Dim t As Thing\nSet t = New Thing\nDebug.Print \"made\"\nDebug.Print \"after\"", 0, 2,
            classes: [("Thing", RDCore.Tests.Cli.ModuleWorkspace.ClassModule("Thing", "Private Sub Class_Terminate()", "Debug.Print \"terminate\"", "End Sub"))]);

        var change = plan.BehaviourChanges.Single(change => change.Kind == ExtractionBehaviourChangeKind.ObjectReleasedEarlier);
        CollectionAssert.AreEqual(new[] { "t" }, change.Variables.ToArray());
        Assert.AreEqual("made | after | terminate", before.Output);
        Assert.AreEqual("made | terminate | after", after.Output);
    }
}
