using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Abstract.Execution;

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

    // the change the plan reports happens: counting line labels as MS-VBA does, Erl reports the call's line. The selection's line numbers move with it, and
    // the call has none: it is the last line number the source procedure reached before the call.
    [TestMethod]
    [DataRow("10 Debug.Print \"a\"\n20 Debug.Print \"b\"\n30 Debug.Print 1 / 0", DisplayName = "the selection starts with a line number")]
    [DataRow("10 Debug.Print \"a\"\nDebug.Print \"b\"\n30 Debug.Print 1 / 0", DisplayName = "the selection starts with an unnumbered line")]
    public async Task AnErrorRaisedInTheNewProcedure_IsAtTheCallsLineForErl_AsThePlanSays(string numbered)
    {
        var (before, after, module, plan) = await ExtractionRuns.BeforeAndAfterAsync(
            $"On Error GoTo H\n{numbered}\nExit Sub\nH:\nDebug.Print \"Erl=\" & Erl", 2, 3, erl: VBErlLineNumbering.LineLabel);

        Assert.IsTrue(plan.BehaviourChanges.Any(change => change.Kind == ExtractionBehaviourChangeKind.ErlReportsTheCall && !change.IsNote));
        Assert.AreEqual("a | b | Erl=30", before.Output);
        Assert.AreEqual("a | b | Erl=10", after.Output, module);
    }

    // the argument missing in the source procedure is missing in the new one: its parameter is Optional, after the required one.
    [TestMethod]
    public async Task AnOptionalParameterTestedWithIsMissing_IsMissingInTheNewProcedure()
    {
        var (before, after, module, _) = await ExtractionRuns.BeforeAndAfterAsync(
            "Debug.Print IsMissing(v) & \" \" & n", 0, 0, driver: "Run 3\nRun 3, 5", header: "Public Sub Run(ByVal n As Long, Optional ByVal v As Variant)");

        Assert.AreEqual("True 3 | False 3", before.Output);
        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Private Sub Extracted(ByVal n As Long, Optional ByVal v As Variant)");
        StringAssert.Contains(module, "Extracted n, v");
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
    public async Task AConditionalBlock_WhoseLiveBranchIsTheLast_RunsIt()
        => Assert.AreEqual("3", await SameAsync("Dim x As Long\n#If False Then\nx = 1\n#Else\nx = n\n#End If\nDebug.Print x", 1, 5));

    // the dead branch's write is an alternative to the live branch's read, not a write before it: x is still given to the new procedure.
    [TestMethod]
    public async Task AWriteInADeadBranch_DoesNotHideAReadInTheLiveOne()
        => Assert.AreEqual("3", await SameAsync("Dim x As Long\nx = n\n#If False Then\nx = 1\n#Else\nDebug.Print x\n#End If", 2, 6));

    // the dead branch is compiled when its condition changes: the variable only it reads is given to the new procedure.
    [TestMethod]
    public async Task AVariableReadInADeadBranchOnly_IsGivenToTheNewProcedure()
    {
        var (before, after, module, plan) = await ExtractionRuns.BeforeAndAfterAsync(
            "Dim x As Long\nx = n\n#If False Then\nDebug.Print x\n#Else\nDebug.Print \"live\"\n#End If", 2, 6);

        Assert.AreEqual("live", before.Output);
        Assert.AreEqual(before, after, module);
        Assert.IsTrue(plan.Parameters.Any(parameter => parameter.Name == "x"), module);
    }

    // the body starts in the block: the Exit Sub of its dead branch is not on the way to the selection.
    [TestMethod]
    public async Task AnExitInADeadBranch_AtTheStartOfTheBody_DoesNotEndTheProcedure()
        => Assert.AreEqual("3 | 4", await SameAsync("#If False Then\nExit Sub\n#End If\nDebug.Print n\nDebug.Print n + 1", 3, 3));

    // the block may compile no branch: x keeps the value it had, so the new procedure is given it, and does not return it.
    [TestMethod]
    public async Task AWriteInTheOnlyBranchOfABlock_KeepsTheValueBefore()
    {
        var (before, after, module, plan) = await ExtractionRuns.BeforeAndAfterAsync("Dim x As Long\nx = n\n#If False Then\nx = 1\n#End If\nDebug.Print x", 2, 4);

        Assert.AreEqual("3", before.Output);
        Assert.AreEqual(before, after, module);
        Assert.IsEmpty(plan.ReturnCandidates, module);
    }

    [TestMethod]
    public async Task ANestedConditionalBlock_RunsItsLiveBranch()
        => Assert.AreEqual("3", await SameAsync("Dim x As Long\nx = n\n#If True Then\n#If False Then\nx = 1\n#Else\nDebug.Print x\n#End If\n#End If", 2, 8));

    // the Exit Sub is routed as any exit of the selection; it does not run.
    [TestMethod]
    public async Task AnExitInADeadBranch_OfTheSelection_IsRoutedAndDoesNotRun()
        => Assert.AreEqual("3", await SameAsync("Dim x As Long\nx = n\n#If False Then\nExit Sub\n#End If\nDebug.Print x", 2, 5));

    [TestMethod]
    public async Task AnOnErrorInADeadBranch_OfTheSelection_DoesNotRun()
        => Assert.AreEqual("3", await SameAsync("Dim x As Long\nx = n\n#If False Then\nOn Error Resume Next\n#End If\nDebug.Print x", 2, 5));

    [TestMethod]
    public async Task StatementsSeparatedByColons_RunAsTheyDidInline()
        => Assert.AreEqual("4", await SameAsync("Dim x As Long\nx = n: x = x + 1\nDebug.Print x", 1, 1));

    [TestMethod]
    public async Task AStatementBetweenTwoOthersOfItsLine_MovesAlone_AndRunsAsItDidInline()
    {
        var (before, after, module, _) = await ExtractionRuns.BeforeAndAfterAsync("Dim x As Long\nx = n: x = x + 1: Debug.Print x", 0, 0, range: new SourceRange(1, 7, 1, 16));

        Assert.AreEqual(before, after, module);
        Assert.AreEqual("4", before.Output);
        StringAssert.Contains(module, "x = n: Extracted x: Debug.Print x");
    }

    // the statement after the block on its line runs only when the block falls through: in the Then of a single-line If, it would run only when it exits.
    [TestMethod]
    public async Task AnExitBeforeAnotherStatementOfTheLine_LeavesThatStatementOutOfTheIfThatRoutesIt()
    {
        var (before, after, module, _) = await ExtractionRuns.BeforeAndAfterAsync(
            "Dim x As Long\nx = n\nIf x > 2 Then\nExit Sub\nEnd If: Debug.Print x", 0, 0, driver: "Run 1\nRun 3", range: new SourceRange(2, 0, 4, 6));

        Assert.AreEqual(before, after, module);
        Assert.AreEqual("1", before.Output);
    }

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

    // b and a in each other's places, renamed, and a passed by reference: the program prints what it did inline.
    [TestMethod]
    public async Task ParametersRenamedReorderedAndPassedByReference_RunAsTheSelectionDidInline()
    {
        var (before, after, module, _) = await ExtractionRuns.BeforeAndAfterAsync("Dim a As Long\nDim b As Long\nDim t As Long\na = n + 1\nt = a * 2\nb = t + n\nDebug.Print a; b",
            4, 5, parameters: [new("b", Name: "sum"), new("a", Name: "x", Passing: "ByRef")]);

        Assert.AreEqual(before, after, module);
        StringAssert.Contains(module, "Private Sub Extracted(ByRef sum As Long, ByVal n As Long, ByRef x As Long)");
    }

    // a and n take each other's names: a is 4 and n is 3, and b is 2 * a + n as before, not 2 * n + a.
    [TestMethod]
    public async Task TwoParametersThatTakeEachOthersNames_RunAsTheSelectionDidInline()
    {
        var (before, after, module, _) = await ExtractionRuns.BeforeAndAfterAsync("Dim a As Long\nDim b As Long\nDim t As Long\na = n + 1\nt = a * 2\nb = t + n\nDebug.Print b",
            4, 5, parameters: [new("a", Name: "n"), new("n", Name: "a")]);

        Assert.AreEqual(before, after, module);
        Assert.AreEqual("11", before.Output);
        StringAssert.Contains(module, "Private Sub Extracted(ByVal n As Long, ByVal a As Long, ByRef b As Long)");
    }

    // A and B are outside the selection, C in it: 1 and 2 leave it, 3 stays in it, 0 and 4 fall through, and -1 raises error 5 as before.
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(-1)]
    public async Task AnOnGoToThatLeavesTheSelection_RunsAsItDidInline(int selector)
    {
        var (before, after, module, _) = await ExtractionRuns.BeforeAndAfterAsync(
            "On n GoTo A, B, C\nDebug.Print 0\nC:\nDebug.Print 3\nExit Sub\nA:\nDebug.Print 1\nExit Sub\nB:\nDebug.Print 2", 0, 3, driver: $"Run {selector}");

        Assert.AreEqual(before, after, module);
    }

    // B is the label right after the selection: going there is where the call goes on to anyway.
    [TestMethod]
    public async Task AnOnGoToTheLabelAfterTheSelection_RunsAsItDidInline()
    {
        var (before, after, module, _) = await ExtractionRuns.BeforeAndAfterAsync(
            "Debug.Print 0\nOn n GoTo B\nDebug.Print 9\nB:\nDebug.Print 1", 0, 2, driver: "Run 0\nRun 1");

        Assert.AreEqual(before, after, module);
        Assert.AreEqual("0 | 9 | 1 | 0 | 1", before.Output);
    }
}
