using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using static RDCore.Tests.Semantics.Refactoring.ExtractionFixture;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The edges of a selection, and the error handling it runs under: what is refused, where control leaves it for, and what extracting it changes (<strong>Extract
/// Method §3.1, §3.4</strong>).
/// </summary>
[TestClass]
public sealed class ExtractionControlFlowTests
{
    private static ExtractionRefusalKind[] RefusalsOf(ExtractionPlan plan) => [.. plan.Refusals.Select(refusal => refusal.Kind)];

    // §3.1, inside → inside: the labels move with the code.
    [TestMethod]
    public void AJumpToALabelInTheSelection_MovesWithIt()
    {
        var plan = Plan("Dim i As Long\nGoTo L\ni = 1\nL:\ni = 2", 1, 4);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
        Assert.IsEmpty(plan.Exits);
    }

    [TestMethod]
    public void ALabelOnTheSelectionsFirstLine_IsInIt()
    {
        var plan = Plan("Dim i As Long\nL:\ni = i + 1\nIf i < n Then GoTo L", 1, 3);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
        Assert.AreEqual(plan.Selection.Statements[0].SourceLocation.Range.Start.Line - 1, plan.Selection.Range.Start.Line, "the selection starts at the label");
        CollectionAssert.AreEqual(new[] { "L" }, plan.Selection.Labels.ToArray());
    }

    [TestMethod]
    public void AJumpFromOutside_ToALabelOnTheSelectionsFirstLine_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("If n > 0 Then GoTo L\nL:\nDebug.Print 1", 1, 2)), ExtractionRefusalKind.JumpIntoTheSelection);

    // 2b found it: the last branch of an If or a Select Case ends with a jump to the block's end, which has no line; left out of the selection, a block the
    // selection ends with branched past its end.
    [TestMethod]
    [DataRow("Dim i As Long\nIf n > 2 Then\ni = 1\nEnd If\nDebug.Print i", 1, 3, DisplayName = "If")]
    [DataRow("Dim i As Long\nIf n > 2 Then\ni = 1\nElseIf n > 1 Then\ni = 2\nElse\ni = 3\nEnd If\nDebug.Print i", 1, 7, DisplayName = "If, ElseIf, Else")]
    [DataRow("Dim i As Long\nSelect Case n\nCase 1\ni = 1\nCase 2\ni = 2\nEnd Select\nDebug.Print i", 1, 6, DisplayName = "Select Case")]
    [DataRow("Dim i As Long\nSelect Case n\nCase 1\ni = 1\nCase Else\ni = 2\nEnd Select\nDebug.Print i", 1, 6, DisplayName = "Select Case, Case Else")]
    [DataRow("Dim i As Long\nFor i = 1 To n\nDebug.Print i\nIf i > 2 Then\nDebug.Print n\nEnd If\nNext", 2, 5, DisplayName = "If last in a loop's body")]
    public void ABlockTheSelectionEndsWith_IsNoExit(string body, int first, int last)
    {
        var plan = Plan(body, first, last);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
        Assert.IsEmpty(plan.Exits);
    }

    [TestMethod]
    public void AGoToTheInstructionAfter_IsAnExitAllTheSame()
        => Assert.AreEqual("L", Plan("If n > 2 Then GoTo L\nL:\nDebug.Print 0", 0, 0).Exits.Single().Label);

    // the label stays where it is, and names the call.
    [TestMethod]
    public void AJumpFromOutside_ToALabelJustBeforeTheSelection_LandsOnTheCall()
    {
        var plan = Plan("If n > 0 Then GoTo L\nL:\nDebug.Print 1", 2, 2);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
    }

    // §3.1, outside → inside.
    [TestMethod]
    public void AJumpFromOutside_ToALabelInTheSelection_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("If n > 0 Then GoTo L\nDebug.Print 1\nL:\nDebug.Print 2", 1, 3)), ExtractionRefusalKind.JumpIntoTheSelection);

    [TestMethod]
    public void AnExitOfTheProcedure_IsAnExitTheCallerTakesToo()
    {
        var plan = Plan("If n > 0 Then Exit Sub\nDebug.Print n", 0, 0);

        Assert.IsTrue(plan.IsExtractable);
        Assert.AreEqual(ExtractionExitKind.Procedure, plan.Exits.Single().Kind);
    }

    [TestMethod]
    public void AnExitOfALoopTheSelectionIsInside_IsAnExit()
    {
        var plan = Plan("Dim i As Long\nFor i = 1 To 3\nIf i = n Then Exit For\nDebug.Print i\nNext", 2, 3);

        Assert.AreEqual(ExtractionExitKind.Loop, plan.Exits.Single().Kind);
    }

    [TestMethod]
    public void AnExitOfALoopTheSelectionHoldsWhole_IsNoExit()
        => Assert.IsEmpty(Plan("Dim i As Long\nFor i = 1 To 3\nIf i = n Then Exit For\nDebug.Print i\nNext\nDebug.Print n", 1, 4).Exits);

    [TestMethod]
    public void AJumpToALabelOutside_IsAnExitToIt()
    {
        var plan = Plan("If n = 0 Then GoTo Done\nDebug.Print n\nDone:\nDebug.Print 0", 0, 0);

        var exit = plan.Exits.Single();
        Assert.AreEqual(ExtractionExitKind.Label, exit.Kind);
        Assert.AreEqual("Done", exit.Label);
    }

    // an exit a literal condition never takes is still code the new procedure has, and an edit of the literal takes it.
    [TestMethod]
    [DataRow("If False Then Exit Sub\nDebug.Print n", 0, 0, (int)ExtractionExitKind.Procedure, DisplayName = "behind If False")]
    [DataRow("Dim i As Long\nDo\nIf False Then Exit Do\ni = i + 1\nLoop While True", 2, 3, (int)ExtractionExitKind.Loop, DisplayName = "out of a loop that never ends")]
    public void AnExitALiteralConditionNeverTakes_IsAnExitAllTheSame(string body, int first, int last, int kind)
        => Assert.AreEqual((ExtractionExitKind)kind, Plan(body, first, last).Exits.Single().Kind);

    [TestMethod]
    public void TwoExitsToOneTarget_AreOneExit()
        => Assert.HasCount(1, Plan("If n = 0 Then Exit Sub\nIf n = 1 Then Exit Sub\nDebug.Print n", 0, 1).Exits);

    [TestMethod]
    public void AGoSubToARoutineOutside_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("GoSub R\nExit Sub\nR:\nDebug.Print n\nReturn", 0, 0)), ExtractionRefusalKind.GoSubToOutside);

    [TestMethod]
    public void APartOfAGoSubRoutine_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("GoSub R\nExit Sub\nR:\nDebug.Print n\nReturn", 3, 4)), ExtractionRefusalKind.ReturnOfAGoSubOutside);

    [TestMethod]
    public void EndAndStop_AreExtracted()
    {
        var plan = Plan("Debug.Print n\nStop\nEnd", 0, 2);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
        Assert.IsEmpty(plan.Exits);
    }

    // §3.4, none: nothing changes.
    [TestMethod]
    public void ASelectionUnderNoOnError_ChangesNothing()
    {
        var plan = Plan("Debug.Print 1 / n", 0, 0);

        Assert.IsFalse(plan.StartsWithResumeNext);
        Assert.IsEmpty(plan.BehaviourChanges);
    }

    // §3.4, only On Error Resume Next: an error resumes inside the selection inline, and does in the new procedure too.
    [TestMethod]
    public void ASelectionUnderOnErrorResumeNext_StartsWithIt()
    {
        var plan = Plan("On Error Resume Next\nDebug.Print 1 / n\nDebug.Print n", 1, 2);

        Assert.IsTrue(plan.StartsWithResumeNext);
        Assert.IsEmpty(plan.BehaviourChanges);
    }

    [TestMethod]
    public void ASelectionUnderOnErrorResumeNextOnSomePaths_IsABehaviourChange()
    {
        var plan = Plan("If n > 0 Then On Error Resume Next\nDebug.Print 1 / n", 1, 1);

        Assert.IsFalse(plan.StartsWithResumeNext);
        Assert.AreEqual(ExtractionBehaviourChangeKind.ResumeNextOnSomePaths, plan.BehaviourChanges.Single().Kind);
    }

    // §3.4, On Error GoTo: a Resume Next of the handler would skip the rest of the extracted code.
    [TestMethod]
    public void AHandlerThatResumes_IsABehaviourChange_NamingItAndItsResume()
    {
        var plan = Plan("On Error GoTo H\nDebug.Print 1 / n\nDebug.Print n\nExit Sub\nH:\nResume Next", 1, 2);

        var change = plan.BehaviourChanges.Single();
        Assert.AreEqual(ExtractionBehaviourChangeKind.CallerHandlerResumes, change.Kind);
        CollectionAssert.AreEqual(new[] { "H" }, change.Handlers.ToArray());
        Assert.HasCount(1, change.Resumes);
        Assert.IsFalse(change.IsNote);
        Assert.IsTrue(plan.IsExtractable, "the user accepts it, or cancels");
    }

    [TestMethod]
    public void AHandlerThatDoesNotResume_ChangesOnlyErl_AsANote()
    {
        var change = Plan("On Error GoTo H\nDebug.Print 1 / n\nExit Sub\nH:\nDebug.Print Err.Number", 1, 1).BehaviourChanges.Single();

        Assert.AreEqual(ExtractionBehaviourChangeKind.ErlOnly, change.Kind);
        Assert.IsTrue(change.IsNote);
    }

    [TestMethod]
    public void LineNumbersUnderAHandler_AreABehaviourChangeOfErl()
        => CollectionAssert.Contains(
            Plan("On Error GoTo H\n10 Debug.Print 1 / n\nExit Sub\nH:\nDebug.Print Erl", 1, 1).BehaviourChanges.Select(change => change.Kind).ToArray(),
            ExtractionBehaviourChangeKind.ErlReportsTheCall);

    // §3.4: the On Error of the selection would be the new procedure's, and what follows the call would run as it did before the selection.
    [TestMethod]
    public void AnOnErrorThatChangesTheStateAfterTheSelection_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("On Error Resume Next\nDebug.Print 1 / n", 0, 0)), ExtractionRefusalKind.ErrorHandlingChanged);

    // §3.4: nothing runs under the state the selection leaves.
    [TestMethod]
    [DataRow("Debug.Print n\nOn Error Resume Next", 0, 1, DisplayName = "the procedure ends after it")]
    [DataRow("Dim x As Long\nOn Error Resume Next\nx = 1", 1, 1, DisplayName = "what follows cannot raise an error")]
    public void AnOnErrorWhoseStateNothingAfterTheSelectionRunsUnder_IsExtracted(string body, int first, int last)
    {
        var plan = Plan(body, first, last);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
    }

    // the jump after the selection cannot raise an error, but the selection it goes back to runs under the state it left.
    [TestMethod]
    public void AnOnErrorWhoseStateTheSelectionRunsUnderAgain_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("L:\nDebug.Print 1 / n\nOn Error Resume Next\nGoTo L", 0, 2)), ExtractionRefusalKind.ErrorHandlingChanged);

    [TestMethod]
    public void AnOnErrorTheSelectionRestores_IsExtracted()
    {
        var plan = Plan("On Error Resume Next\nDebug.Print 1 / n\nOn Error GoTo 0\nDebug.Print n", 0, 2);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
        Assert.IsFalse(plan.StartsWithResumeNext, "it sets its own");
    }

    [TestMethod]
    public void AnOnErrorGoToAHandlerOutside_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("On Error GoTo H\nDebug.Print 1 / n\nOn Error GoTo 0\nExit Sub\nH:\nResume Next", 0, 2)),
            ExtractionRefusalKind.HandlerOutsideTheSelection);

    [TestMethod]
    public void AResumeOfAnErrorRaisedBefore_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("On Error GoTo H\nDebug.Print 1 / n\nExit Sub\nH:\nDebug.Print Err.Number\nResume Next", 5, 5)),
            ExtractionRefusalKind.ResumeOfAnErrorBefore);

    // Err is global: reading it about an error raised before the selection is the same from the new procedure.
    [TestMethod]
    public void AReadOfErrInAHandler_IsExtracted()
    {
        var plan = Plan("On Error GoTo H\nDebug.Print 1 / n\nExit Sub\nH:\nDebug.Print Err.Number\nResume Next", 4, 4);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
    }

    // a handler that ends without a Resume leaves the error active inline; extracted, the call ends with its callee, and the error with it.
    [TestMethod]
    public void AHandlerOfItsOwnThatFallsThroughWithTheErrorActive_IsRefused()
        => CollectionAssert.Contains(RefusalsOf(Plan("Debug.Print n\nOn Error GoTo H\nDebug.Print 1 / n\nH:\nOn Error GoTo 0\nDebug.Print n", 1, 4)),
            ExtractionRefusalKind.ErrorHandlingChanged);

    [TestMethod]
    public void AHandlingOfItsOwnThatResumes_IsExtracted()
    {
        var plan = Plan("Debug.Print n\nOn Error GoTo H\nDebug.Print 1 / n\nGoTo Done\nH:\nResume Done\nDone:\nOn Error GoTo 0\nDebug.Print n", 1, 7);

        Assert.IsTrue(plan.IsExtractable, string.Join(", ", RefusalsOf(plan)));
    }

    // the On Error Resume Next the new procedure starts with clears what Err says of an error before the selection.
    [TestMethod]
    public void AReadOfErrUnderAnInheritedResumeNext_IsABehaviourChange()
    {
        var plan = Plan("On Error Resume Next\nDebug.Print 1 / n\nDebug.Print Err.Number", 2, 2);

        Assert.IsTrue(plan.StartsWithResumeNext);
        CollectionAssert.Contains(plan.BehaviourChanges.Select(change => change.Kind).ToArray(), ExtractionBehaviourChangeKind.ErrClearedOnEntry);
    }

    [TestMethod]
    public void NoReadOfErrUnderAnInheritedResumeNext_ChangesNothing()
        => Assert.IsEmpty(Plan("On Error Resume Next\nDebug.Print 1 / n\nDebug.Print 2 / n", 2, 2).BehaviourChanges);
}
