using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call in the selector of a <c>Select Case</c>, or in a value of a <c>Case</c>, made by compiled code: the selector is evaluated once, and the values of
/// a <c>Case</c> as its clauses are compared, in order, until one matches (MS-VBAL §5.4.2.10). An error after a call is the interpreter's, given what
/// compiled code evaluated: the call is not made again. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledSelectCaseCallTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Gauges =
    [
        ("Gauge", ModuleWorkspace.ClassModule("Gauge", "Option Explicit",
            "Private mSize As Long",
            "Public Sub Grow()",
            "    mSize = mSize + 1",
            "End Sub",
            "Public Property Get Size() As Long",
            "    Calls = Calls + 1",
            "    Size = mSize",
            "End Property")),
    ];

    // a function of a standard module that notes it was called, by its tag, and returns its value.
    private static readonly string[] Marks =
    [
        "Private Function Mark(ByVal Tag As Long, ByVal V As Long) As Long",
        "    Trail = Trail * 10 + Tag",
        "    Mark = V",
        "End Function",
    ];

    private static void AssertNoneStepped(CompilationResult result)
        => Assert.IsEmpty(result.Stepped, string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));

    // the selector a call in an operation: compiled, and the call made once each time the Select runs.
    [TestMethod]
    public async Task ASelectorThatCallsAFunction_IsCompiled_AndCalledOnce()
    {
        var result = await AssertAlikeAsync(Program([
            "Public Trail As Long",
            "Public Sub Main()",
            "    Debug.Print Sorted() & \";\" & Trail",
            "End Sub",
            "Private Function Sorted() As Long",
            "    Dim I As Long",
            "    For I = 1 To 4",
            "        Select Case Mark(1, I) + 1",
            "            Case 2",
            "                Sorted = Sorted + 1",
            "            Case 3, 4",
            "                Sorted = Sorted + 10",
            "            Case Else",
            "                Sorted = Sorted + 100",
            "        End Select",
            "    Next",
            "End Function",
            .. Marks]), "Sorted", recoveries: 0, expected: "121;1111");
        AssertNoneStepped(result);
    }

    // the selector a Property Get of an object: compiled, and called once each time the Select runs.
    [TestMethod]
    public async Task ASelectorThatCallsAMemberOfAnObject_IsCompiled_AndCalledOnce()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Calls As Long",
            "Public Sub Main()",
            "    Debug.Print Measured() & \";\" & Calls",
            "End Sub",
            "Private Function Measured() As Long",
            "    Dim G As Gauge",
            "    Dim I As Long",
            "    Set G = New Gauge",
            "    For I = 1 To 3",
            "        G.Grow",
            "        Select Case G.Size",
            "            Case 1",
            "                Measured = Measured + 1",
            "            Case Is >= 2",
            "                Measured = Measured + 10",
            "        End Select",
            "    Next",
            "End Function"), "Measured", recoveries: 0, classes: Gauges, expected: "21;3");
        AssertNoneStepped(result);
    }

    // the values of a Case each a call: a clause is compared only until one matches, so that a call of a later one is not made; both ends of a To range
    // are called.
    [TestMethod]
    [Ignore("a Case that calls is the interpreter's: the values of its range clauses are not among the nodes of its statement (CaseExpressionStatementNode.Children), which name what compiled code evaluated of it")]
    public async Task ACaseValueThatCallsAFunction_IsCalledOnlyWhenItsClauseIsCompared()
    {
        var result = await AssertAlikeAsync(Program([
            "Public Trail As Long",
            "Public Sub Main()",
            "    Debug.Print Matched(2) & \";\" & Matched(7) & \";\" & Trail",
            "End Sub",
            "Private Function Matched(ByVal N As Long) As Long",
            "    Select Case N",
            "        Case Mark(1, 0)",
            "            Matched = 1",
            "        Case Mark(2, 1), Mark(3, 2), Mark(4, 3)",
            "            Matched = 2",
            "        Case Mark(5, 5) To Mark(6, 8)",
            "            Matched = 3",
            "        Case Else",
            "            Matched = 9",
            "    End Select",
            "End Function",
            .. Marks]), "Matched", recoveries: 0, expected: "2;3;123123456");
        AssertNoneStepped(result);
    }

    // an error in a value of a Case, after a call in it, under On Error GoTo: the call is made once, and the error goes to the handler.
    [TestMethod]
    public async Task AnErrorInACaseValue_AfterACall_GoesToTheHandler_TheCallMadeOnce()
        => await AssertAlikeAsync(Program([
            "Public Trail As Long",
            "Public Sub Main()",
            "    Debug.Print Guarded(0) & \";\" & Trail",
            "End Sub",
            "Private Function Guarded(ByVal Zero As Long) As Long",
            "    On Error GoTo Failed",
            "    Select Case 3",
            "        Case Mark(1, 1) To Mark(2, 6) \\ Zero",
            "            Guarded = 1",
            "    End Select",
            "    Exit Function",
            "Failed:",
            "    Guarded = Err.Number",
            "End Function",
            .. Marks]), "Guarded", recoveries: 1, expected: "11;12");

    // an error in the selector, after the call in it, with no handler: the activation is the interpreter's from the middle of the Select, the call not
    // made again, and the error is the caller's.
    [TestMethod]
    public async Task AnErrorInTheSelector_AfterACall_WithNoHandler_IsTheCallers_TheCallMadeOnce()
        => await AssertAlikeAsync(Program([
            "Public Trail As Long",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Debug.Print Walked(0)",
            "    Debug.Print Err.Number & \";\" & Trail",
            "End Sub",
            "Private Function Walked(ByVal Zero As Long) As Long",
            "    Select Case Mark(1, 6) \\ Zero",
            "        Case 1",
            "            Walked = 1",
            "    End Select",
            "End Function",
            .. Marks]), "Walked", recoveries: 0, walkBacks: 1, expected: "11;1");

    // a variable of the selector that a call right of it changes, by reference: the selector is what the variable was when it was read, before the call,
    // which compiled code reads where it is written.
    [TestMethod]
    public async Task AVariableOfTheSelectorItsCallChanges_IsReadBeforeTheCall()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Picked()",
            "End Sub",
            "Private Function Picked() As Long",
            "    Dim A As Long",
            "    A = 1",
            "    Select Case A + Bump(A)",
            "        Case 12",
            "            Picked = 1",
            "        Case 22",
            "            Picked = 2",
            "    End Select",
            "    Picked = Picked * 100 + A",
            "End Function",
            "Private Function Bump(ByRef A As Long) As Long",
            "    A = A + 10",
            "    Bump = A",
            "End Function"), "Picked", recoveries: 0, expected: "111");
        AssertNoneStepped(result);
    }
}
