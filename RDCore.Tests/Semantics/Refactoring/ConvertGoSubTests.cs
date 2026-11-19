using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A <c>GoSub</c> routine converted to a procedure: each <c>GoSub</c> of it a call, each <c>Return</c> an <c>Exit Sub</c>, and the program run before and after
/// doing what it did.
/// </summary>
[TestClass]
public sealed class ConvertGoSubTests
{
    // Twice doubles total, which the code after each GoSub reads.
    private const string Doubling =
        "    Dim total As Long\n" +
        "    total = n\n" +
        "    GoSub Twice\n" +
        "    GoSub Twice\n" +
        "    Debug.Print total\n" +
        "    Exit Sub\n" +
        "Twice:\n" +
        "    total = total * 2\n" +
        "    Return";

    [TestMethod]
    public async Task EachGoSub_IsACall_AndTheRoutineAProcedure_ThatDoesWhatItDid()
    {
        var (before, after, module) = await ExtractionRuns.ConvertedBeforeAndAfterAsync(Doubling, "Twice");

        StringAssert.Contains(module, "    total = n\r\n    Extracted total\r\n    Extracted total\r\n    Debug.Print total\r\n    Exit Sub\r\nEnd Sub", module);
        StringAssert.Contains(module, "Private Sub Extracted(ByRef total As Long)\r\n    total = total * 2\r\nEnd Sub", module);
        Assert.IsFalse(module.Contains("Twice", StringComparison.Ordinal), module);
        Assert.AreEqual("12", before.Output);
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task AReturnBeforeTheEnd_LeavesTheProcedure()
    {
        const string body =
            "    Dim total As Long\n" +
            "    total = n\n" +
            "    GoSub Twice\n" +
            "    GoSub Twice\n" +
            "    Debug.Print total\n" +
            "    Exit Sub\n" +
            "Twice:\n" +
            "    If total > 4 Then Return\n" +
            "    total = total * 2\n" +
            "    Return";

        var (before, after, module) = await ExtractionRuns.ConvertedBeforeAndAfterAsync(body, "Twice");

        StringAssert.Contains(module, "    If total > 4 Then Exit Sub\r\n    total = total * 2\r\nEnd Sub", module);
        Assert.AreEqual("6", before.Output);
        Assert.AreEqual(before, after);
    }

    // the Exit Sub of the routine leaves the source procedure: the call does, when the procedure says so.
    [TestMethod]
    public async Task AnExitOfTheSourceProcedure_InTheRoutine_IsRoutedThroughTheCall()
    {
        const string body =
            "    Dim total As Long\n" +
            "    total = n\n" +
            "    GoSub Twice\n" +
            "    Debug.Print total\n" +
            "    Exit Sub\n" +
            "Twice:\n" +
            "    If total > 2 Then Exit Sub\n" +
            "    total = total * 2\n" +
            "    Return";

        var (before, after, module) = await ExtractionRuns.ConvertedBeforeAndAfterAsync(body, "Twice", driver: "Run 3\nRun 1");

        StringAssert.Contains(module, "    If Extracted(total) Then Exit Sub\r\n", module);
        Assert.AreEqual("2", before.Output);
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task ALineNumberedRoutine_IsConverted_ItsLineNumbersWithIt()
    {
        const string body =
            "    Dim a As Long\n" +
            "10  a = n\n" +
            "20  GoSub 100\n" +
            "30  Debug.Print a\n" +
            "40  Exit Sub\n" +
            "100 a = a + 1\n" +
            "110 Return";

        var (before, after, module) = await ExtractionRuns.ConvertedBeforeAndAfterAsync(body, "100");

        StringAssert.Contains(module, "20  Extracted a\r\n", module);
        StringAssert.Contains(module, "Private Sub Extracted(ByRef a As Long)\r\n    a = a + 1\r\n110 Exit Sub\r\nEnd Sub", module);
        Assert.AreEqual("4", before.Output);
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    [DataRow("Dim total As Long\nGoSub Twice\ntotal = n\nTwice:\ntotal = 1\nReturn", nameof(ExtractionRefusalKind.FallsIntoTheRoutine), DisplayName = "the statement before it goes on to it")]
    [DataRow("Dim total As Long\nGoSub Twice\nTwice:\ntotal = 1\nReturn", nameof(ExtractionRefusalKind.FallsIntoTheRoutine), DisplayName = "the GoSub before it returns into it")]
    [DataRow("Dim total As Long\nIf n > 0 Then On n GoSub Twice\nExit Sub\nTwice:\ntotal = 1\nReturn", nameof(ExtractionRefusalKind.OnGoSubInASingleLineIf),
        DisplayName = "an On…GoSub in a single-line If runs it")]
    [DataRow("Dim total As Long\nGoSub Twice\nGoTo Inside\nTwice:\ntotal = 1\nInside:\ntotal = 2\nReturn", nameof(ExtractionRefusalKind.JumpIntoTheSelection), DisplayName = "a GoTo lands in it")]
    public void ARoutineEnteredOtherwiseThanByItsGoSubs_IsRefused(string body, string kind)
        => CollectionAssert.Contains(ExtractionFixture.RoutinePlan(body, "Twice").Refusals.Select(refusal => refusal.Kind.ToString()).ToArray(), kind);

    [TestMethod]
    [DataRow("Dim total As Long\nGoSub Twice\nExit Sub\nTwice:\ntotal = 1\nReturn", "Nowhere", nameof(GoSubRoutineRefusal.NoLabel), DisplayName = "no such label")]
    [DataRow("Dim total As Long\nGoTo Twice\nExit Sub\nTwice:\ntotal = 1\nExit Sub", "Twice", nameof(GoSubRoutineRefusal.NoGoSub), DisplayName = "no GoSub of it")]
    [DataRow("Dim total As Long\nGoSub Twice\nExit Sub\nBack:\ntotal = 2\nReturn\nTwice:\ntotal = 1\nGoTo Back", "Twice", nameof(GoSubRoutineRefusal.NotContiguous),
        DisplayName = "it goes back before its label")]
    public void ALabelThatStartsNoRoutine_IsRefused(string body, string label, string refusal)
        => Assert.AreEqual(refusal, ExtractionFixture.RoutineRefusal(body, label)?.ToString());

    [TestMethod]
    public void ARoutineThatReadsTheResult_IsRefused()
    {
        const string body = "GoSub Twice\nExit Function\nTwice:\nRun = Run * 2\nReturn";

        var converted = ExtractionFixture.Converted(body, "Twice", header: "Public Function Run(ByVal n As Long) As Long", end: "End Function");

        Assert.AreEqual(ExtractionEditRefusalKind.RoutineUsesTheResult, converted.Edit.Refusal);
    }

    [TestMethod]
    public void ARoutineThatRunsItself_IsRefused()
    {
        const string body = "Dim total As Long\nGoSub Twice\nExit Sub\nTwice:\ntotal = total + 1\nIf total < 3 Then GoSub Twice\nReturn";

        Assert.AreEqual(ExtractionEditRefusalKind.RoutineRunsItself, ExtractionFixture.Converted(body, "Twice").Edit.Refusal);
    }

    [TestMethod]
    public async Task AGoSubInASingleLineIf_IsACallInIt()
    {
        const string body =
            "    Dim total As Long\n" +
            "    total = n\n" +
            "    If n > 1 Then GoSub Twice\n" +
            "    Debug.Print total\n" +
            "    Exit Sub\n" +
            "Twice:\n" +
            "    total = total * 2\n" +
            "    Return";

        var (before, after, module) = await ExtractionRuns.ConvertedBeforeAndAfterAsync(body, "Twice", driver: "Run 3\nRun 1");

        StringAssert.Contains(module, "    If n > 1 Then Extracted total\r\n", module);
        Assert.AreEqual("6 | 1", before.Output);
        Assert.AreEqual(before, after);
    }

    // a variable only the routine uses is the new procedure's own, declared there.
    [TestMethod]
    public async Task AVariableOnlyTheRoutineUses_MovesWithIt()
    {
        const string body =
            "    Dim total As Long\n" +
            "    Dim doubled As Long\n" +
            "    total = n\n" +
            "    GoSub Twice\n" +
            "    Debug.Print total\n" +
            "    Exit Sub\n" +
            "Twice:\n" +
            "    doubled = total * 2\n" +
            "    total = doubled\n" +
            "    Return";

        var (before, after, module) = await ExtractionRuns.ConvertedBeforeAndAfterAsync(body, "Twice");

        StringAssert.Contains(module, "Private Sub Extracted(ByRef total As Long)\r\n    Dim doubled As Long\r\n    doubled = total * 2\r\n", module);
        Assert.IsFalse(module.Contains("    Dim doubled As Long\r\n    total = n", StringComparison.Ordinal), module);
        Assert.AreEqual(before, after);
    }

    // an On…GoSub of the routine and of another: a Select Case of the selector, which the driver runs in range, at 0, past the last label, and negative.
    private const string Choosing =
        "    Dim total As Long\n" +
        "    On Error GoTo Failed\n" +
        "    total = 1\n" +
        "    On n GoSub Twice, Thrice, Twice\n" +
        "    Debug.Print total\n" +
        "    Exit Sub\n" +
        "Failed:\n" +
        "    Debug.Print \"error\"; Err.Number\n" +
        "    Exit Sub\n" +
        "Twice:\n" +
        "    total = total * 2\n" +
        "    Return\n" +
        "Thrice:\n" +
        "    total = total * 3\n" +
        "    Return";

    [TestMethod]
    public async Task AnOnGoSub_IsASelectCase_ThatDoesWhatItDid()
    {
        var (before, after, module) = await ExtractionRuns.ConvertedBeforeAndAfterAsync(Choosing, "Twice", driver: "Run 1\nRun 2\nRun 3\nRun 0\nRun 4\nRun 1.6\nRun -1");

        StringAssert.Contains(module,
            "    Select Case CInt(n)\r\n" +
            "    Case 1, 3\r\n" +
            "        Extracted total\r\n" +
            "    Case 2\r\n" +
            "        GoSub Thrice\r\n" +
            "    Case Is < 0\r\n" +
            "        Err.Raise 5\r\n" +
            "    End Select\r\n" +
            "    Debug.Print total\r\n", module);
        StringAssert.Contains(module, "Thrice:\r\n    total = total * 3\r\n    Return\r\nEnd Sub", module);
        Assert.AreEqual("2 | 3 | 2 | 1 | 1 | 3 | error 5", before.Output);
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public void AnOnGoSub_IsVerified_ByItsCall()
        => Assert.IsNull(ExtractionFixture.RoutineVerified(Choosing, "Twice"));

    // the call in the Select Case is the one verified, where the formatter has indented it.
    [TestMethod]
    public void TheCallOfAnOnGoSub_ThatDoesNotGiveBackWhatIsReadAfterIt_IsNotVerified()
    {
        var failure = ExtractionFixture.RoutineVerified(Choosing, "Twice",
            edit => ExtractionFixture.Replacing(edit, ExtractionTextEditKind.Procedure, "ByRef total", "ByVal total"));

        Assert.AreEqual(new ExtractionVerificationFailure(ExtractionVerificationFailureKind.DataFlowDiffers, "total"), failure);
    }

    // a module that declares its own Err raises run-time error 5 with the library's.
    [TestMethod]
    public async Task AnOnGoSub_WhereTheModuleDeclaresErr_RaisesErrorFiveWithTheLibrarysErr()
    {
        const string body =
            "    Dim total As Long\n" +
            "    total = 1\n" +
            "    On n GoSub Twice\n" +
            "    Debug.Print total; Err\n" +
            "    Exit Sub\n" +
            "Twice:\n" +
            "    total = total * 2\n" +
            "    Return";

        var (before, after, module) = await ExtractionRuns.ConvertedBeforeAndAfterAsync(body, "Twice", driver: "Run 1\nRun -1", declarations: "Private Err As Long");

        StringAssert.Contains(module, "        VBA.Err.Raise 5\r\n", module);
        Assert.AreNotEqual(ExecutionOutcome.Completed, before.Outcome);
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task AnOnGoSubAndAGoSub_AreBothCalls()
    {
        const string body =
            "    Dim total As Long\n" +
            "    total = n\n" +
            "    GoSub Twice\n" +
            "    On n GoSub Twice\n" +
            "    Debug.Print total\n" +
            "    Exit Sub\n" +
            "Twice:\n" +
            "    total = total * 2\n" +
            "    Return";

        var (before, after, module) = await ExtractionRuns.ConvertedBeforeAndAfterAsync(body, "Twice", driver: "Run 1\nRun 2");

        StringAssert.Contains(module, "    Extracted total\r\n    Select Case CInt(n)\r\n    Case 1\r\n        Extracted total\r\n", module);
        Assert.AreEqual("4 | 4", before.Output);
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public void TheConversion_IsVerified_CallByCall()
        => Assert.IsNull(ExtractionFixture.RoutineVerified(Doubling, "Twice"));

    // a call that gives nothing back, where the code after it reads what the routine wrote.
    [TestMethod]
    public void ACallThatDoesNotGiveBackWhatIsReadAfterIt_IsNotVerified()
    {
        var failure = ExtractionFixture.RoutineVerified(Doubling, "Twice",
            edit => ExtractionFixture.Replacing(edit, ExtractionTextEditKind.Procedure, "ByRef total", "ByVal total"));

        Assert.AreEqual(new ExtractionVerificationFailure(ExtractionVerificationFailureKind.DataFlowDiffers, "total"), failure);
    }

    [TestMethod]
    public async Task ARoutineRunUnderResumeNext_StartsWithIt()
    {
        const string body =
            "    Dim total As Long\n" +
            "    On Error Resume Next\n" +
            "    total = n\n" +
            "    GoSub Twice\n" +
            "    Debug.Print total\n" +
            "    Exit Sub\n" +
            "Twice:\n" +
            "    total = total \\ 0\n" +
            "    total = total * 2\n" +
            "    Return";

        var (before, after, module) = await ExtractionRuns.ConvertedBeforeAndAfterAsync(body, "Twice");

        StringAssert.Contains(module, "Private Sub Extracted(ByRef total As Long)\r\n    On Error Resume Next\r\n", module);
        Assert.AreEqual(ExecutionOutcome.Completed, before.Outcome);
        Assert.AreEqual(before, after);
    }
}
