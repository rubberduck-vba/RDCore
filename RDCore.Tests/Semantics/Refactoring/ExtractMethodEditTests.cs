using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.SDK.Formatting;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The extraction as it is written in the module: the new procedure, the call that replaces the selection, the declarations that move, and the routing of the
/// exits.
/// </summary>
[TestClass]
public sealed class ExtractMethodEditTests
{
    // the source procedure and what follows it: the module's other declarations come before it.
    private static string FromRun(string module) => module[module.IndexOf("Public ", StringComparison.Ordinal)..];

    private static string Lines(string text) => text.ReplaceLineEndings("\r\n");

    // the loop of AVariableTheLoopReadsAgain_IsPassedByRef: ByVal, the write is lost and the loop never ends.
    // the run starts after x = n on its line: that statement stays, the call takes the run's place after it, and the moved lines are indented alike.
    [TestMethod]
    public void AStatementAfterAnotherOfItsLine_MovesWithoutIt_AndTheCallTakesItsPlace()
    {
        var (module, _) = ExtractionFixture.Extract("Dim x As Long\nx = n: x = x + 1\nx = x * 2\nDebug.Print x", 0, 0, range: new SourceRange(1, 7, 2, 9));

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Dim x As Long
            x = n: Extracted x
            Debug.Print x
            End Sub

            Private Sub Extracted(ByRef x As Long)
                x = x + 1
                x = x * 2
            End Sub

            """), FromRun(module));
    }

    // with a statement after the call on its line, the If that routes an exit is a block: the statement is not in its Then.
    [TestMethod]
    public void AnExitRoutedBeforeAnotherStatementOfTheLine_IsABlockIf()
    {
        var (_, edit) = ExtractionFixture.Extract("Dim x As Long\nx = n\nIf x > 2 Then\nExit Sub\nEnd If: Debug.Print x", 0, 0, range: new SourceRange(2, 0, 4, 6));

        Assert.AreEqual(Lines("If Extracted(x) Then\n    Exit Sub\nEnd If"), edit.Call);
    }

    [TestMethod]
    public void AVariableTheLoopReadsAgain_IsPassedByRef_ByACallWithoutParentheses()
    {
        var (module, _) = ExtractionFixture.Extract("Dim x As Long\nDo While x < 10\nx = x + 1\nLoop", 2, 2);

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Dim x As Long
            Do While x < 10
            Extracted x
            Loop
            End Sub

            Private Sub Extracted(ByRef x As Long)
                x = x + 1
            End Sub

            """), FromRun(module));
    }

    [TestMethod]
    public void AReturnValueChosen_IsTheFunctionsResult_AssignedAtTheCall()
    {
        var (module, _) = ExtractionFixture.Extract("Dim y As Long\ny = n * 2\nDebug.Print y", 1, 1, returnValue: "y");

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Dim y As Long
            y = Extracted(n)
            Debug.Print y
            End Sub

            Private Function Extracted(ByVal n As Long) As Long
                Dim y As Long
                y = n * 2
                Extracted = y
            End Function

            """), FromRun(module));
    }

    [TestMethod]
    public void NoReturnValueChosen_GivesTheOutputBack_ByRef()
    {
        var (module, _) = ExtractionFixture.Extract("Dim y As Long\ny = n * 2\nDebug.Print y", 1, 1);

        StringAssert.Contains(module, Lines("Dim y As Long\nExtracted n, y\nDebug.Print y"));
        StringAssert.Contains(module, "Private Sub Extracted(ByVal n As Long, ByRef y As Long)");
    }

    [TestMethod]
    public void AnObjectReturned_IsAssignedWithSet()
    {
        var (module, _) = ExtractionFixture.Extract("Dim c As Collection\nSet c = New Collection\nDebug.Print c.Count", 1, 1, returnValue: "c");

        StringAssert.Contains(module, Lines("Set c = Extracted()\n"));
        StringAssert.Contains(module, Lines("""
            Private Function Extracted() As Collection
                Dim c As Collection
                Set c = New Collection
                Set Extracted = c
            End Function
            """));
    }

    // `Dim a As Long, b As Long` in the selection: b moves with it, a is used after and stays; the statement keeps it alone.
    [TestMethod]
    public void ADeclarationOfSeveral_KeepsInTheSourceWhatIsUsedAfter_AndMovesTheRest()
    {
        var (module, _) = ExtractionFixture.Extract("Dim a As Long, b As Long\na = n\nb = n\nDebug.Print a + b", 0, 2);

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Dim a As Long, b As Long
            Extracted n, a, b
            Debug.Print a + b
            End Sub

            Private Sub Extracted(ByVal n As Long, ByRef a As Long, ByRef b As Long)
                a = n
                b = n
            End Sub

            """), FromRun(module));
    }

    [TestMethod]
    public void ADeclarationOfSeveral_MovesIntoTheNewProcedure_WhatOnlyTheSelectionUses()
    {
        var (module, _) = ExtractionFixture.Extract("Dim a As Long, b As Long\na = n\nb = n\nDebug.Print a", 0, 2);

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Dim a As Long
            Extracted n, a
            Debug.Print a
            End Sub

            Private Sub Extracted(ByVal n As Long, ByRef a As Long)
                Dim b As Long
                a = n
                b = n
            End Sub

            """), FromRun(module));
    }

    // a declaration before the selection of a variable only the selection uses: the line goes, the new procedure declares it.
    [TestMethod]
    public void ADeclarationBeforeTheSelection_OfAVariableOnlyItUses_Moves()
    {
        var (module, _) = ExtractionFixture.Extract("Dim t As Long\nt = n\nDebug.Print t", 1, 2);

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Extracted n
            End Sub

            Private Sub Extracted(ByVal n As Long)
                Dim t As Long
                t = n
                Debug.Print t
            End Sub

            """), FromRun(module));
    }

    // `Dim t As Long, _ / u As Long` before the selection: t moves into the new procedure, and the statement keeps u.
    [TestMethod]
    public void ADeclarationOverSeveralLines_KeepsWhatStays()
    {
        var (module, edit) = ExtractionFixture.Extract("Dim t As Long, _\n    u As Long\nu = n\nt = n\nDebug.Print t + u", 3, 4);

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Dim u As Long
            u = n
            Extracted n, u
            End Sub

            Private Sub Extracted(ByVal n As Long, ByVal u As Long)
                Dim t As Long
                t = n
                Debug.Print t + u
            End Sub

            """), FromRun(module));
        Assert.AreEqual(ExtractionTextEditKind.Declaration, edit.Edits[0].Kind);
    }

    // the declaration over several lines is in the selection: what is used after it stays before the call, on one line.
    [TestMethod]
    public void ADeclarationOverSeveralLinesInTheSelection_LeavesWhatIsUsedAfter()
    {
        var (module, _) = ExtractionFixture.Extract("Dim a As Long, _\n    b As Long\na = n\nb = n\nDebug.Print a", 0, 3);

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Dim a As Long
            Extracted n, a
            Debug.Print a
            End Sub

            Private Sub Extracted(ByVal n As Long, ByRef a As Long)
                Dim b As Long
                a = n
                b = n
            End Sub

            """), FromRun(module));
    }

    [TestMethod]
    public void AConstantTheSelectionReads_IsDeclaredAgain()
    {
        var (module, _) = ExtractionFixture.Extract("Const k As Long = 3\nDebug.Print k + n", 1, 1);

        StringAssert.Contains(module, Lines("""
            Private Sub Extracted(ByVal n As Long)
                Const k As Long = 3
                Debug.Print k + n
            End Sub
            """));
        StringAssert.Contains(module, Lines("Const k As Long = 3\nExtracted n\n"), "the source keeps its own");
    }

    // a function's name cannot be passed: its result goes as a copy, and comes back after the call.
    [TestMethod]
    public void TheSourcesResult_IsGivenAsACopy_AndWrittenBack()
    {
        var (module, _) = ExtractionFixture.Extract("Run = n\nRun = Run + 1", 1, 1, header: "Public Function Run(ByVal n As Long) As Long", end: "End Function");

        Assert.AreEqual(Lines("""
            Public Function Run(ByVal n As Long) As Long
            Run = n
            Dim RunResult As Long
            RunResult = Run
            Extracted RunResult
            Run = RunResult
            End Function

            Private Sub Extracted(ByRef RunResult As Long)
                RunResult = RunResult + 1
            End Sub

            """), FromRun(module));
    }

    [TestMethod]
    public void OneExit_IsABoolean_TheCallSiteTakes()
    {
        var (module, _) = ExtractionFixture.Extract("If n > 0 Then Exit Sub\nDebug.Print n", 0, 0);

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            If Extracted(n) Then Exit Sub
            Debug.Print n
            End Sub

            Private Function Extracted(ByVal n As Long) As Boolean
                If n > 0 Then Extracted = True: Exit Function
            End Function

            """), FromRun(module));
    }

    [TestMethod]
    public void SeveralExits_AreMembersOfAnEnumeration_TheCallSiteSelects()
    {
        var (module, edit) = ExtractionFixture.Extract("Dim i As Long\nDo While i < 10\nIf i = n Then Exit Do\nIf i > n Then Exit Sub\ni = i + 1\nLoop", 2, 4);

        Assert.AreEqual(Lines("""
            Private Enum ExtractedExit
                ExtractedExitNone
                ExtractedExitDo
                ExtractedExitSub
            End Enum
            """), edit.Enumeration);
        StringAssert.Contains(module, Lines($"{edit.Enumeration}\n\nPrivate Sub Bump"), "before the module's first procedure");
        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Dim i As Long
            Do While i < 10
            Select Case Extracted(i, n)
                Case ExtractedExitDo
                    Exit Do
                Case ExtractedExitSub
                    Exit Sub
            End Select
            Loop
            End Sub

            Private Function Extracted(ByRef i As Long, ByVal n As Long) As ExtractedExit
                If i = n Then Extracted = ExtractedExitDo: Exit Function
                If i > n Then Extracted = ExtractedExitSub: Exit Function
                i = i + 1
            End Function

            """), FromRun(module));
    }

    [TestMethod]
    public void AJumpToALabelOutside_IsAGoToAtTheCallSite()
    {
        var (module, _) = ExtractionFixture.Extract("If n = 0 Then GoTo Done\nDebug.Print n\nDone:\nDebug.Print 0", 0, 0);

        StringAssert.Contains(module, Lines("If Extracted(n) Then GoTo Done\nDebug.Print n\nDone:"));
    }

    [TestMethod]
    public void ASelectionInAWithBlock_IsGivenItsTarget_AndHasABlockOfItsOwn()
    {
        var (module, _) = ExtractionFixture.Extract("Dim p As Point\nWith p\n.X = n\n.Y = n\nEnd With\nDebug.Print p.X", 2, 3);

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Dim p As Point
            With p
            Extracted n, p
            End With
            Debug.Print p.X
            End Sub

            Private Sub Extracted(ByVal n As Long, ByRef p As Point)
                With p
                    .X = n
                    .Y = n
                End With
            End Sub

            """), FromRun(module));
    }

    [TestMethod]
    public void ASelectionUnderOnErrorResumeNext_StartsWithIt()
    {
        var (module, _) = ExtractionFixture.Extract("On Error Resume Next\nDebug.Print 1 / n\nDebug.Print n", 1, 2);

        StringAssert.Contains(module, Lines("""
            Private Sub Extracted(ByVal n As Long)
                On Error Resume Next
                Debug.Print 1 / n
                Debug.Print n
            End Sub
            """));
    }

    // the selection's own indentation stays at the call site, and its body is dedented into the new procedure.
    [TestMethod]
    public void AnIndentedSelection_IsCalledAtItsIndentation_AndDedentedInTheNewProcedure()
    {
        var (module, _) = ExtractionFixture.Extract("Dim i As Long\nFor i = 1 To n\n    Debug.Print i\n    If i > 2 Then\n        Debug.Print n\n    End If\nNext", 2, 5);

        StringAssert.Contains(module, Lines("For i = 1 To n\n    Extracted i, n\nNext"));
        StringAssert.Contains(module, Lines("""
            Private Sub Extracted(ByVal i As Long, ByVal n As Long)
                Debug.Print i
                If i > 2 Then
                    Debug.Print n
                End If
            End Sub
            """));
    }

    [TestMethod]
    public void ATypeDeclarationCharacter_IsWrittenAsTheParametersType()
    {
        var (module, _) = ExtractionFixture.Extract("Dim s$\ns = \"a\"\nDebug.Print s & n", 2, 2);

        StringAssert.Contains(module, "Private Sub Extracted(ByVal s$, ByVal n As Long)");
    }

    [TestMethod]
    public void AnArray_IsPassedAsOne_ByRef()
    {
        var (module, _) = ExtractionFixture.Extract("Dim a(1 To 3) As Long\na(1) = n\nDebug.Print a(1)", 2, 2);

        StringAssert.Contains(module, "Private Sub Extracted(ByRef a() As Long)");
    }

    [TestMethod]
    public void AnUnextractableSelection_IsNotWritten()
    {
        var (module, edit) = ExtractionFixture.Extract("If n > 0 Then GoTo L\nL:\nDebug.Print 1", 1, 2);

        Assert.AreEqual(ExtractionEditRefusalKind.NotExtractable, edit.Refusal);
        Assert.IsEmpty(edit.Edits);
        StringAssert.DoesNotMatch(module, new System.Text.RegularExpressions.Regex("Extracted"));
    }

    [TestMethod]
    public void AReturnValueThatIsNoCandidate_IsRefused()
        => Assert.AreEqual(ExtractionEditRefusalKind.NotAReturnCandidate, ExtractionFixture.Extract("Debug.Print n", 0, 0, returnValue: "n").Edit.Refusal);

    [TestMethod]
    public void AnExitByAJumpTable_IsRefused()
        => Assert.AreEqual(ExtractionEditRefusalKind.ExitByJumpTable,
            ExtractionFixture.Extract("On n GoTo A, B\nDebug.Print 0\nA:\nDebug.Print 1\nB:\nDebug.Print 2", 0, 1).Edit.Refusal);

    [TestMethod]
    public void AnExit_IsRouted_UnlessExitRoutingIsOff()
    {
        Assert.IsNull(ExtractionFixture.Extract("If n > 0 Then Exit Sub\nDebug.Print n", 0, 0).Edit.Refusal, "on by default");
        Assert.AreEqual(ExtractionEditRefusalKind.ExitRoutingDeclined, ExtractionFixture.Extract("If n > 0 Then Exit Sub\nDebug.Print n", 0, 0, routeExits: false).Edit.Refusal);
        Assert.IsNull(ExtractionFixture.Extract("Debug.Print n", 0, 0, routeExits: false).Edit.Refusal, "nothing to route");
    }

    [TestMethod]
    public void TheNewProcedure_IsRightAfterTheSourceProcedure()
    {
        var (module, _) = ExtractionFixture.Extract("Debug.Print n", 0, 0);

        var source = module.IndexOf("Public Sub Run", StringComparison.Ordinal);
        var extracted = module.IndexOf("Private Sub Extracted", StringComparison.Ordinal);
        Assert.IsGreaterThan(source, extracted);
        Assert.AreEqual(Lines("End Sub\n\nPrivate Sub Extracted"), module[module.IndexOf("End Sub", source, StringComparison.Ordinal)..extracted] + "Private Sub Extracted");
        Assert.IsTrue(module.TrimEnd().EndsWith("End Sub", StringComparison.Ordinal), "the source procedure is the module's last: the new one follows it");
    }

    // the formatter indents what the extraction writes, by the settings: two spaces a level here. The lines it does not write stay as they are.
    [TestMethod]
    public void TheFormatter_IndentsTheNewProcedureAndTheCall_AndNothingElse()
    {
        var options = CodeFormattingOptions.Default with { IndentSpaces = 2 };
        var (module, edit) = ExtractionFixture.Extract("Dim i As Long\nFor i = 1 To n\nIf i > 2 Then\nDebug.Print i\nEnd If\nNext", 2, 4, formatting: options);

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Dim i As Long
            For i = 1 To n
                Extracted i
            Next
            End Sub

            Private Sub Extracted(ByVal i As Long)
              If i > 2 Then
                Debug.Print i
              End If
            End Sub

            """), FromRun(module));
        Assert.AreEqual(Lines("Private Sub Extracted(ByVal i As Long)\n  If i > 2 Then\n    Debug.Print i\n  End If\nEnd Sub"), edit.Procedure);
        Assert.AreEqual("    Extracted i", edit.Call);
    }

    // the Select Case of several exits is indented as the code around it is.
    [TestMethod]
    public void TheFormatter_IndentsTheDispatchOfSeveralExits()
    {
        var options = CodeFormattingOptions.Default with { IndentSpaces = 2, IndentCase = true };
        var (_, edit) = ExtractionFixture.Extract("Dim i As Long\nDo While i < 10\nIf i = n Then Exit Do\nIf i > n Then Exit Sub\ni = i + 1\nLoop", 2, 4, formatting: options);

        Assert.AreEqual(Lines("""
                Select Case Extracted(i, n)
                  Case ExtractedExitDo
                    Exit Do
                  Case ExtractedExitSub
                    Exit Sub
                End Select
            """).TrimEnd(), edit.Call);
        Assert.AreEqual(Lines("Private Enum ExtractedExit\n  ExtractedExitNone\n  ExtractedExitDo\n  ExtractedExitSub\nEnd Enum"), edit.Enumeration);
    }

    // b and a are put in each other's places, and renamed; a is passed by reference. The call passes the variables as the source names them.
    [TestMethod]
    public void TheParametersTheUserEdits_AreDeclaredAndNamedAsTheUserSaid_AndTheCallPassesThemInThatOrder()
    {
        var (module, _) = ExtractionFixture.Extract("Dim a As Long\nDim b As Long\nDim t As Long\na = n + 1\nt = a * 2\nb = t + n\nDebug.Print a; b", 4, 5,
            parameters: [new("b", Name: "sum"), new("a", Name: "x", Passing: "ByRef")]);

        Assert.AreEqual(Lines("""
            Public Sub Run(ByVal n As Long)
            Dim a As Long
            Dim b As Long
            a = n + 1
            Extracted b, n, a
            Debug.Print a; b
            End Sub

            Private Sub Extracted(ByRef sum As Long, ByVal n As Long, ByRef x As Long)
                Dim t As Long
                t = x * 2
                sum = t + n
            End Sub

            """), FromRun(module));
    }

    // its type-declaration character stays with the variable it is written after, whatever its name.
    [TestMethod]
    public void ARenamedParameterWithATypeDeclarationCharacter_KeepsIt()
    {
        var (module, _) = ExtractionFixture.Extract("Dim k%\nk = n\nDebug.Print k% + k\nDebug.Print k", 2, 2, parameters: [new("k", Name: "count")]);

        StringAssert.Contains(module, Lines("Private Sub Extracted(ByVal count%)\n    Debug.Print count% + count\nEnd Sub"));
        StringAssert.Contains(module, Lines("Extracted k\nDebug.Print k"));
    }

    // the variable returned is the new procedure's own: the user's name for it is its local's.
    [TestMethod]
    public void ARenamedReturnValue_IsTheNewProceduresLocal_UnderItsNewName()
    {
        var (module, _) = ExtractionFixture.Extract("Dim a As Long\nDim b As Long\na = n\nb = a + 1\nDebug.Print b", 3, 3, returnValue: "b",
            parameters: [new("b", Name: "total")]);

        StringAssert.Contains(module, Lines("""
            b = Extracted(a)
            Debug.Print b
            End Sub

            Private Function Extracted(ByVal a As Long) As Long
                Dim total As Long
                total = a + 1
                Extracted = total
            End Function
            """));
    }
}
