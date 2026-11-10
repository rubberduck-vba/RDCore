using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A member of what a call returns (<c>S.Pos.Y</c>, <c>S.Pos.Twice</c>, <c>Make().Pos.Y</c>), made by compiled code: the call that returns the object made
/// once, the object held by the statement's temporaries, and let go of when the statement is over, as the interpreter lets go of it, which is where its
/// <c>Class_Terminate</c> runs (MS-VBAL §5.3.1.10). An error after the call is the interpreter's, given the object: the call is not made again, and the
/// object is let go of before the error is handled. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledMemberChainTests
{
    // the log, and the count of Pos reads, which every program declares before its procedures.
    private static string Program(params string[] lines) => string.Join("\r\n", ["Public Log As String", "Public Calls As Long", .. lines]) + "\r\n";

    // a ship whose Pos is a new object each time it is read, which says when it is made and when it is destroyed; its Kept is one it holds.
    private static readonly IReadOnlyList<(string Name, string Source)> Ships =
    [
        ("Pos", ModuleWorkspace.ClassModule("Pos", "Option Explicit",
            "Public Y As Long",
            "Private Sub Class_Terminate()",
            "    Log = Log & \"T\" & Y & \" \"",
            "End Sub",
            "Public Property Get Twice() As Long",
            "    Twice = Y * 2",
            "End Property",
            "Public Property Get Fails() As Long",
            "    Err.Raise 5",
            "End Property",
            "Public Sub Bump()",
            "    Y = Y + 1",
            "End Sub")),
        ("Ship", ModuleWorkspace.ClassModule("Ship", "Option Explicit",
            "Public Id As Long",
            "Private mKept As Pos",
            "Public Property Get Pos() As Pos",
            "    Dim P As Pos",
            "    Calls = Calls + 1",
            "    Log = Log & \"P\" & Id & \" \"",
            "    Set P = New Pos",
            "    P.Y = Id",
            "    Set Pos = P",
            "End Property",
            "Public Property Get Kept() As Pos",
            "    If mKept Is Nothing Then Set mKept = New Pos",
            "    mKept.Y = Id",
            "    Set Kept = mKept",
            "End Property",
            "Public Property Get NoPos() As Pos",
            "    Calls = Calls + 1",
            "End Property")),
    ];

    // what every program has after its own procedures: a mark the program puts in the log, and a ship.
    private static readonly string[] Shared =
    [
        "Public Sub Mark(ByVal S As String)",
        "    Log = Log & S & \" \"",
        "End Sub",
        "Private Function NewShip(ByVal Id As Long) As Ship",
        "    Dim S As Ship",
        "    Set S = New Ship",
        "    S.Id = Id",
        "    Set NewShip = S",
        "End Function",
    ];

    private static void AssertNoneStepped(CompilationResult result)
        => Assert.IsEmpty(result.Stepped, string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));

    // a variable, and a Property Get, of what a call returns: the object is destroyed once the statement is over, before the next runs.
    [TestMethod]
    public async Task AMemberOfWhatACallReturns_IsLetGoOfWhenTheStatementIsOver()
    {
        var result = await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Read(NewShip(3)) & \";\" & Log & \";\" & Calls",
            "End Sub",
            "Private Function Read(ByVal S As Ship) As Long",
            "    Read = S.Pos.Y",
            "    Mark \"a\"",
            "    Read = Read + S.Pos.Twice",
            "    Mark \"b\"",
            "End Function",
            .. Shared]), "Read", recoveries: 0, classes: Ships, expected: "9;P3 T3 a P3 T3 b ;2");
        AssertNoneStepped(result);
    }

    // a member of what a call returns in a condition, a loop's condition and a For's bounds: let go of before the branch, the loop and the body.
    [TestMethod]
    public async Task AMemberOfWhatACallReturns_InAConditionALoopAndABound_IsLetGoOfBeforeWhatFollows()
    {
        var result = await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Looped(NewShip(2)) & \";\" & Log & \";\" & Calls",
            "End Sub",
            "Private Function Looped(ByVal S As Ship) As Long",
            "    Dim I As Long",
            "    If S.Pos.Y = 2 Then Mark \"y\" Else Mark \"n\"",
            "    Do While S.Pos.Y > Looped",
            "        Looped = Looped + 1",
            "    Loop",
            "    For I = S.Pos.Y To S.Pos.Twice",
            "        Mark \"f\"",
            "    Next",
            "End Function",
            .. Shared]), "Looped", recoveries: 0, classes: Ships,
            expected: "2;P2 T2 y P2 T2 P2 T2 P2 T2 P2 P2 T2 T2 f f f ;6");
        AssertNoneStepped(result);
    }

    // a Sub of what a call returns, called as a statement: the object is destroyed after the call.
    [TestMethod]
    public async Task ASubOfWhatACallReturns_IsCalled_ThenLetGoOf()
    {
        var result = await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Bumped NewShip(4)",
            "    Debug.Print Log & \";\" & Calls",
            "End Sub",
            "Private Sub Bumped(ByVal S As Ship)",
            "    S.Pos.Bump",
            "    Mark \"a\"",
            "End Sub",
            .. Shared]), "Bumped", recoveries: 0, classes: Ships, expected: "P4 T5 a ;1");
        AssertNoneStepped(result);
    }

    // an error after the call, under On Error GoTo: the interpreter is given the object, does not read Pos again, and the object is destroyed before the
    // handler's first statement.
    [TestMethod]
    public async Task AnErrorAfterTheCall_GoesToTheHandler_TheObjectLetGoOfBeforeIt()
        => await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Guarded(NewShip(3), 0) & \";\" & Log & \";\" & Calls",
            "End Sub",
            "Private Function Guarded(ByVal S As Ship, ByVal Zero As Long) As Long",
            "    On Error GoTo Failed",
            "    Guarded = S.Pos.Y \\ Zero",
            "    Exit Function",
            "Failed:",
            "    Mark \"h\"",
            "    Guarded = Err.Number",
            "End Function",
            .. Shared]), "Guarded", recoveries: 1, classes: Ships, expected: "11;P3 T3 h ;1");

    // the handler Resumes the statement: it runs again from its start, which reads Pos again (MS-VBA evaluates it anew); the first object is destroyed
    // before the handler runs.
    [TestMethod]
    public async Task AStatementResumedAfterTheCall_CallsAgain_TheFirstObjectLetGoOfBeforeTheHandler()
        => await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Retried(NewShip(3)) & \";\" & Log & \";\" & Calls",
            "End Sub",
            "Private Function Retried(ByVal S As Ship) As Long",
            "    Dim Zero As Long",
            "    On Error GoTo Failed",
            "    Retried = S.Pos.Y \\ Zero",
            "    Exit Function",
            "Failed:",
            "    Mark \"h\"",
            "    Zero = 1",
            "    Resume",
            "End Function",
            .. Shared]), "Retried", recoveries: 2, classes: Ships, expected: "3;P3 T3 h P3 T3 ;2");

    // an error after the call, with no handler: the error is the caller's, and the object is destroyed before the caller's next statement.
    [TestMethod]
    public async Task AnErrorAfterTheCall_WithNoHandler_IsTheCallers_TheObjectLetGoOfBeforeItsNextStatement()
        => await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Debug.Print Walked(NewShip(3), 0)",
            "    Mark \"c\"",
            "    Debug.Print Err.Number & \";\" & Log & \";\" & Calls",
            "End Sub",
            "Private Function Walked(ByVal S As Ship, ByVal Zero As Long) As Long",
            "    Walked = S.Pos.Y \\ Zero",
            "End Function",
            .. Shared]), "Walked", recoveries: 0, walkBacks: 1, classes: Ships, expected: "11;P3 T3 c ;1");

    // the same, the caller under On Error GoTo: the object is destroyed as the callee unwinds, before the caller's handler's first statement.
    [TestMethod]
    public async Task AnErrorAfterTheCall_WithNoHandler_TheObjectLetGoOfBeforeTheCallersHandler()
        => await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Outer(NewShip(3)) & \";\" & Log & \";\" & Calls",
            "End Sub",
            "Private Function Outer(ByVal S As Ship) As Long",
            "    On Error GoTo Failed",
            "    Outer = Walked(S, 0)",
            "    Exit Function",
            "Failed:",
            "    Mark \"c\"",
            "    Outer = Err.Number",
            "End Function",
            "Private Function Walked(ByVal S As Ship, ByVal Zero As Long) As Long",
            "    Walked = S.Pos.Y \\ Zero",
            "End Function",
            .. Shared]), "Walked", recoveries: 0, walkBacks: 1, classes: Ships, expected: "11;P3 T3 c ;1");

    // a Property Get of what a call returns raises an error: the interpreter is given the object, which it does not read Pos for again; the object is
    // destroyed before On Error Resume Next goes on.
    [TestMethod]
    public async Task AMemberThatRaises_OfWhatACallReturns_IsGivenTheObject_NotCalledAgain()
        => await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Failing(NewShip(3)) & \";\" & Log & \";\" & Calls",
            "End Sub",
            "Private Function Failing(ByVal S As Ship) As Long",
            "    On Error Resume Next",
            "    Failing = S.Pos.Fails + 1",
            "    Mark \"r\"",
            "    Failing = Failing * 100 + Err.Number",
            "End Function",
            .. Shared]), "Failing", recoveries: 1, classes: Ships, expected: "5;P3 T3 r ;1");

    // a call that returns Nothing: error 91, at the member, the call made once.
    [TestMethod]
    public async Task AMemberOfNothingACallReturns_RaisesError91_TheCallMadeOnce()
        => await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Missing(NewShip(3)) & \";\" & Calls",
            "End Sub",
            "Private Function Missing(ByVal S As Ship) As Long",
            "    On Error Resume Next",
            "    Missing = S.NoPos.Y",
            "    Missing = Err.Number",
            "End Function",
            .. Shared]), "Missing", recoveries: 1, classes: Ships, expected: "91;1");

    // what a call returns, given to an object parameter by value: the call made once, where the argument is evaluated; the object outlives the parameter,
    // and is destroyed when the statement is over.
    [TestMethod]
    public async Task WhatACallReturns_GivenToAnObjectParameter_IsLetGoOfWhenTheStatementIsOver()
    {
        var result = await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Given(NewShip(5)) & \";\" & Log & \";\" & Calls",
            "End Sub",
            "Private Function Given(ByVal S As Ship) As Long",
            "    Given = Doubled(S.Pos) + 1",
            "    Mark \"g\"",
            "End Function",
            "Private Function Doubled(ByVal P As Pos) As Long",
            "    Mark \"d\"",
            "    Doubled = P.Y * 2",
            "End Function",
            .. Shared]), "Given", recoveries: 0, classes: Ships, expected: "11;P5 d T5 g ;1");
        AssertNoneStepped(result);
    }

    // a call that returns an object something else holds: the statement lets go of its temporary, and the object lives on.
    [TestMethod]
    public async Task AMemberOfAnObjectAFieldHolds_IsNotDestroyed()
    {
        var result = await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Dim S As Ship",
            "    Set S = NewShip(6)",
            "    Debug.Print Held(S) & \";\" & Log",
            "End Sub",
            "Private Function Held(ByVal S As Ship) As Long",
            "    Held = S.Kept.Y",
            "    Mark \"a\"",
            "    Held = Held + S.Kept.Twice",
            "    Mark \"b\"",
            "End Function",
            .. Shared]), "Held", recoveries: 0, classes: Ships, expected: "18;a b ");
        AssertNoneStepped(result);
    }

    // a callee that hands its own statement to the interpreter in the middle of it, while its caller holds an object of the caller's statement: the callee's
    // object is destroyed when the callee's statement is over, the caller's when the caller's is.
    [TestMethod]
    public async Task ACalleesStatementHandedOver_LetsGoOfItsObject_TheCallersKeptToItsStatementsEnd()
    {
        var result = await AssertAlikeAsync(Program([
            "Public Zero As Long",
            "Public Sub Main()",
            "    Debug.Print Total(NewShip(1)) & \";\" & Log & \";\" & Calls",
            "End Sub",
            "Private Function Total(ByVal A As Ship) As Long",
            "    Total = A.Pos.Y + Make().Pos.Y",
            "    Mark \"|\"",
            "End Function",
            "Private Function Make() As Ship",
            "    Dim S As Ship",
            "    Dim K As Long",
            "    Set S = NewShip(2)",
            "    On Error Resume Next",
            "    K = S.Pos.Y \\ Zero",
            "    Mark \"m\"",
            "    Set Make = S",
            "End Function",
            .. Shared]), "Total", recoveries: 0, classes: Ships, expected: "3;P1 P2 T2 m P2 T2 T1 | ;3");
        AssertNoneStepped(result);
    }
}
