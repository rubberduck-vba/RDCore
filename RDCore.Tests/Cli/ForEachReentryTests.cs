using RDCore.Runtime.Compiled;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// What a <c>For Each</c> holds of what it walks, an array or a <c>Collection</c> of objects (observed in Excel, 64-bit, en-US). When the program leaves the
/// loop and comes back (a <c>GoSub</c> to a subroutine past it and its <c>Return</c>, or an error handler past it and its <c>Resume Next</c>), the loop goes
/// on over the same array. What a function returned is held until the procedure ends, however the loop ends, and is let go of before the procedure's
/// variables, or when the loop starts again; a collection in a variable is not held past the loop. Both engines do the same.
/// </summary>
[TestClass]
public sealed class ForEachReentryTests
{
    private static readonly (string Name, string Source)[] Noisy =
    [
        ("Noisy", ModuleWorkspace.ClassModule("Noisy",
            "Option Explicit",
            "Public Id As Long",
            "Private Sub Class_Terminate()",
            "    Debug.Print \"gone \" & Id",
            "End Sub")),
    ];

    // the case's body, in a procedure of its own that Main calls, then "end": what is let go of when the procedure ends is said before it.
    private static string ProgramOf(params string[] body)
        => string.Join("\r\n", [
            "Option Explicit",
            "Public Sub Main()",
            "    Walk",
            "    Debug.Print \"end\"",
            "End Sub",
            "Private Sub P(ByVal x As Variant)",
            "    Debug.Print x",
            "End Sub",
            "Private Function Made() As Variant",
            "    Dim A(1 To 2) As Variant",
            "    Set A(1) = New Noisy",
            "    A(1).Id = 1",
            "    Set A(2) = New Noisy",
            "    A(2).Id = 2",
            "    Made = A",
            "End Function",
            "Private Function MadeC() As Collection",
            "    Dim N As Noisy",
            "    Set MadeC = New Collection",
            "    Set N = New Noisy",
            "    N.Id = 1",
            "    MadeC.Add N",
            "    Set N = New Noisy",
            "    N.Id = 2",
            "    MadeC.Add N",
            "End Function",
            "Private Sub Walk()",
            .. body,
            "End Sub",
            ""]);

    private static async Task<string> PrintedAsync(string program, VBExecutionEngine engine)
    {
        string? printed = null;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(Noisy, program, (_, run, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, run.Outcome, $"{run.ErrorNumber} {run.ErrorMessage}");
            printed = string.Join("|", run.Output.Select(line => line.Trim()));
            return Task.CompletedTask;
        }, engine: engine);
        return printed!;
    }

    private static async Task AssertPrintsAsync(string expected, params string[] body)
    {
        var program = ProgramOf(body);
        Assert.AreEqual(expected, await PrintedAsync(program, VBExecutionEngine.Interpreter), "interpreted");
        Assert.AreEqual(expected, await PrintedAsync(program, VBExecutionEngine.Compiled), "compiled");
    }

    [TestMethod]
    public async Task ALoopThatRunsOut_LetsGoOfTheArray_WhenTheProcedureEnds()
        => await AssertPrintsAsync("at 1|at 2|after|gone 1|gone 2|end",
            "Dim V As Variant",
            "For Each V In Made()",
            "    P \"at \" & V.Id",
            "Next",
            "P \"after\"");

    [TestMethod]
    public async Task AGoSubOutOfTheLoop_AndItsReturn_GoOnOverTheSameArray()
        => await AssertPrintsAsync("at 1|at 2|after|gone 1|gone 2|end",
            "Dim V As Variant",
            "For Each V In Made()",
            "    GoSub Show",
            "Next",
            "P \"after\"",
            "Exit Sub",
            "Show:",
            "P \"at \" & V.Id",
            "Return");

    [TestMethod]
    public async Task AGoSubOutOfTheLoop_ThenTheRestOfTheBody_GoOnOverTheSameArray()
        => await AssertPrintsAsync("at 1|back 1|at 2|back 2|after|gone 1|gone 2|end",
            "Dim V As Variant",
            "For Each V In Made()",
            "    GoSub Show",
            "    P \"back \" & V.Id",
            "Next",
            "P \"after\"",
            "Exit Sub",
            "Show:",
            "P \"at \" & V.Id",
            "Return");

    [TestMethod]
    public async Task AnErrorHandlerPastTheLoop_ThatResumesNext_GoesOnOverTheSameArray()
        => await AssertPrintsAsync("at 1|handler 11|next 1|at 2|handler 11|next 2|after|gone 1|gone 2|end",
            "Dim V As Variant",
            "On Error GoTo H",
            "For Each V In Made()",
            "    P \"at \" & V.Id",
            "    Debug.Print 1 / 0",
            "    P \"next \" & V.Id",
            "Next",
            "P \"after\"",
            "Exit Sub",
            "H:",
            "P \"handler \" & Err.Number",
            "Resume Next");

    [TestMethod]
    public async Task AnErrorRaisedInTheLoop_HandledPastIt_ThatResumesNext_GoesOnOverTheSameArray()
        => await AssertPrintsAsync("handler 5|at 1|at 2|after|gone 1|gone 2|end",
            "Dim V As Variant, N As Long",
            "On Error GoTo H",
            "For Each V In Made()",
            "    N = N + 1",
            "    If N = 1 Then Err.Raise 5",
            "    P \"at \" & V.Id",
            "Next",
            "P \"after\"",
            "Exit Sub",
            "H:",
            "P \"handler \" & Err.Number",
            "Resume Next");

    // the loop left by Exit For: the array is let go of when the procedure ends, then the control variable, which holds the first object still.
    [TestMethod]
    public async Task ALoopLeftByExitFor_AfterAGoSub_LetsGoOfTheArray_WhenTheProcedureEnds()
        => await AssertPrintsAsync("at 1|after|gone 2|gone 1|end",
            "Dim V As Variant",
            "For Each V In Made()",
            "    GoSub Show",
            "    Exit For",
            "Next",
            "P \"after\"",
            "Exit Sub",
            "Show:",
            "P \"at \" & V.Id",
            "Return");

    [TestMethod]
    public async Task ACollectionAFunctionReturned_ThatALoopRunsOut_IsLetGoOf_WhenTheProcedureEnds()
        => await AssertPrintsAsync("at 1|at 2|after|gone 1|gone 2|end",
            "Dim V As Variant",
            "For Each V In MadeC()",
            "    P \"at \" & V.Id",
            "Next",
            "P \"after\"");

    [TestMethod]
    public async Task ACollectionAFunctionReturned_LeftByExitFor_IsLetGoOf_WhenTheProcedureEnds()
        => await AssertPrintsAsync("at 1|after|gone 1|gone 2|end",
            "Dim V As Variant",
            "For Each V In MadeC()",
            "    P \"at \" & V.Id",
            "    Exit For",
            "Next",
            "V = Empty",
            "P \"after\"");

    [TestMethod]
    public async Task ACollectionInAVariable_IsNotHeldPastTheLoop()
        => await AssertPrintsAsync("at 1|at 2|gone 1|gone 2|after|end",
            "Dim C As New Collection, V As Variant",
            "Dim X As Noisy",
            "Set X = New Noisy",
            "X.Id = 1",
            "C.Add X",
            "Set X = New Noisy",
            "X.Id = 2",
            "C.Add X",
            "Set X = Nothing",
            "For Each V In C",
            "    P \"at \" & V.Id",
            "Next",
            "Set C = Nothing",
            "V = Empty",
            "P \"after\"");

    [TestMethod]
    [DataRow("Dim W As Variant, V As Variant", DisplayName = "a variable declared before the loop's")]
    [DataRow("Dim V As Variant, W As Variant", DisplayName = "a variable declared after the loop's")]
    public async Task WhatAFunctionReturnedToALoop_IsLetGoOf_BeforeTheProceduresVariables(string declarations)
        => await AssertPrintsAsync("after|gone 1|gone 2|gone 9|end",
            declarations,
            "Set W = New Noisy",
            "W.Id = 9",
            "For Each V In Made()",
            "    Exit For",
            "Next",
            "V = Empty",
            "P \"after\"");

    [TestMethod]
    [DataRow("Made()", DisplayName = "an array")]
    [DataRow("MadeC()", DisplayName = "a collection")]
    public async Task ALoopStartedAgain_LetsGoOfWhatAFunctionReturnedToIt_Before(string source)
        => await AssertPrintsAsync("at 1|at 2|round 1|gone 1|gone 2|at 1|at 2|round 2|after|gone 1|gone 2|end",
            "Dim V As Variant, N As Long",
            "For N = 1 To 2",
            "    For Each V In " + source,
            "        P \"at \" & V.Id",
            "    Next",
            "    V = Empty",
            "    P \"round \" & N",
            "Next",
            "P \"after\"");

    // MS-VBA lets go of the collection there, which is destroyed with the members the loop has not reached, and the loop ends (observed in Excel:
    // "gone 2|at 1|gone 1|after|end"). RD-VBA leaves the variable as it is while the loop runs, and says so (CollectionSetToNothingInItsOwnLoop).
    [TestMethod]
    [DataRow("Collection", "    Set C = Nothing", DisplayName = "a Collection")]
    [DataRow("Variant", "    Set C = Nothing", DisplayName = "a Variant")]
    [DataRow("Object", "    Set C = Nothing", DisplayName = "an Object")]
    [DataRow("Collection", "    If True Then Set C = Nothing", DisplayName = "in a block of the loop")]
    public async Task ACollectionSetToNothingInItsOwnLoop_IsLeftAsItIs_UntilTheLoopIsOver(string declared, string set)
        => await AssertPrintsAsync("at 1|at 2|nothing False|gone 1|gone 2|after|end",
            $"Dim C As {declared}, V As Variant",
            "Set C = MadeC()",
            "For Each V In C",
            set,
            "    P \"at \" & V.Id",
            "Next",
            "P \"nothing \" & (C Is Nothing)",
            "V = Empty",
            "Set C = Nothing",
            "P \"after\"");

    [TestMethod]
    public async Task AnotherVariableSetToNothingInTheLoop_IsSetToNothing()
        => await AssertPrintsAsync("at 1|gone 9|at 2|after|gone 1|gone 2|end",
            "Dim C As Collection, W As Noisy, V As Variant",
            "Set C = MadeC()",
            "Set W = New Noisy",
            "W.Id = 9",
            "For Each V In C",
            "    P \"at \" & V.Id",
            "    Set W = Nothing",
            "Next",
            "V = Empty",
            "P \"after\"");
}
