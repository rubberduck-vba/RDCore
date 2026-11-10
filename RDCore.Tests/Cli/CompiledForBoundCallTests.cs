using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call in the start, the end or the step of a <c>For</c> statement, made by compiled code: each is evaluated once, in that order, before the counter is
/// given the start (MS-VBAL §5.4.2.3). An error after a call is the interpreter's, given what compiled code evaluated: the call is not made again. A
/// program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledForBoundCallTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static readonly IReadOnlyList<(string Name, string Source)> Spans =
    [
        ("Span", ModuleWorkspace.ClassModule("Span", "Option Explicit",
            "Private mLeft As Long",
            "Private mWidth As Long",
            "Public Sub Init(ByVal L As Long, ByVal W As Long)",
            "    mLeft = L",
            "    mWidth = W",
            "End Sub",
            "Public Property Get Left() As Long",
            "    Calls = Calls + 1",
            "    Left = mLeft",
            "End Property",
            "Public Property Get Width() As Long",
            "    Calls = Calls + 1",
            "    Width = mWidth",
            "End Property")),
    ];

    // a function of a standard module that notes it was called, by its tag, and returns its value.
    private static readonly string[] Marks =
    [
        "Private Function Mark(ByVal Tag As Long, ByVal V As Long) As Long",
        "    Log = Log * 10 + Tag",
        "    Mark = V",
        "End Function",
    ];

    private static void AssertNoneStepped(CompilationResult result)
        => Assert.IsEmpty(result.Stepped, string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));

    // the start and the end of a For each a Property Get of an object, the end an operation of two: the loop is compiled, its bounds called once each time
    // the For runs.
    [TestMethod]
    public async Task ABoundThatCallsAMemberOfAnObject_IsCompiled_AndCalledOnce()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Calls As Long",
            "Public Sub Main()",
            "    Debug.Print Summed() & \";\" & Calls",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim S As Span",
            "    Dim I As Long",
            "    Dim K As Long",
            "    Set S = New Span",
            "    S.Init 3, 4",
            "    For K = 1 To 5",
            "        For I = S.Left To S.Left + S.Width - 1",
            "            Summed = Summed + I",
            "        Next",
            "    Next",
            "End Function"), "Summed", recoveries: 0, classes: Spans, expected: "90;15");
        AssertNoneStepped(result);
        Assert.AreEqual(16L, result.Body!.DirectCalls, "Init, and the three bounds five times");
    }

    // the start, the end and the step each a call of a function of a standard module: called in that order, once each time the For runs.
    [TestMethod]
    public async Task TheStartTheEndAndTheStep_AreCalledInThatOrder_OnceEach()
    {
        var result = await AssertAlikeAsync(Program([
            "Public Log As Long",
            "Public Sub Main()",
            "    Debug.Print Total() & \";\" & Log",
            "End Sub",
            "Private Function Total() As Long",
            "    Dim I As Long",
            "    Dim K As Long",
            "    For K = 1 To 2",
            "        For I = Mark(1, 1) To Mark(2, 7) Step Mark(3, 3)",
            "            Total = Total + I",
            "        Next",
            "    Next",
            "End Function",
            .. Marks]), "Total", recoveries: 0, expected: "24;123123");
        AssertNoneStepped(result);
    }

    // an error in the end, after the call in the start, under On Error GoTo: the interpreter is given the start compiled code called, and does not call it
    // again; the error goes to the handler.
    [TestMethod]
    public async Task AnErrorInTheEnd_AfterACallInTheStart_GoesToTheHandler_TheCallMadeOnce()
        => await AssertAlikeAsync(Program([
            "Public Log As Long",
            "Public Sub Main()",
            "    Debug.Print Guarded(0) & \";\" & Log",
            "End Sub",
            "Private Function Guarded(ByVal Zero As Long) As Long",
            "    Dim I As Long",
            "    On Error GoTo Failed",
            "    For I = Mark(1, 1) To Mark(2, 6) \\ Zero",
            "        Guarded = Guarded + I",
            "    Next",
            "    Exit Function",
            "Failed:",
            "    Guarded = Err.Number",
            "End Function",
            .. Marks]), "Guarded", recoveries: 1, expected: "11;12");

    // an error in the step, after the calls in the start and the end, with no handler: the activation is the interpreter's from the middle of the For, its
    // calls not made again, and the error is the caller's.
    [TestMethod]
    public async Task AnErrorInTheStep_WithNoHandler_IsTheCallers_TheCallsMadeOnce()
        => await AssertAlikeAsync(Program([
            "Public Log As Long",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Debug.Print Walked(0)",
            "    Debug.Print Err.Number & \";\" & Log",
            "End Sub",
            "Private Function Walked(ByVal Zero As Long) As Long",
            "    Dim I As Long",
            "    For I = Mark(1, 1) To Mark(2, 6) Step Mark(3, 1) \\ Zero",
            "        Walked = Walked + I",
            "    Next",
            "End Function",
            .. Marks]), "Walked", recoveries: 0, walkBacks: 1, expected: "11;123");

    // the start a variable a call in the end changes, by reference: the start is the value the variable had when it was evaluated, before the call
    // (MS-VBAL §5.4.2.3), which compiled code reads where it is written.
    [TestMethod]
    public async Task AStartTheEndsCallChanges_IsReadBeforeTheCall()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Total()",
            "End Sub",
            "Private Function Total() As Long",
            "    Dim A As Long",
            "    Dim I As Long",
            "    A = 1",
            "    For I = A To Bump(A)",
            "        Total = Total + I",
            "    Next",
            "    Total = Total * 100 + A",
            "End Function",
            "Private Function Bump(ByRef A As Long) As Long",
            "    A = A + 10",
            "    Bump = A",
            "End Function"), "Total", recoveries: 0, expected: "6611");
        AssertNoneStepped(result);
    }
}
