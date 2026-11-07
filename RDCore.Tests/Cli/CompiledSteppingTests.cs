using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// Instructions compiled code does not do, in a compiled procedure: the interpreter runs each alone, and the activation goes on compiled where it leaves
/// it. What compiled code holds nothing of (a <c>Static</c>, a variable of the module) is the frame's, and the instructions that read or write it the
/// interpreter's; so is a call of a member bound late. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledSteppingTests
{
    // a class whose members are called late-bound, on P, a variable of the module declared As Object: compiled code does not call a member it binds no
    // declaration of (rd-vbal.3.5.6), so a statement that calls one is the interpreter's by design. It is what the rows below that need a statement
    // compiled code does not do are given.
    private static readonly (string Name, string Source)[] Pieces =
    [
        ("Piece", ModuleWorkspace.ClassModule("Piece", "Option Explicit",
            "Public Function Text() As String", "    Text = \"x\"", "End Function",
            "Public Function Size(ByVal S As String) As Long", "    Size = Len(S)", "End Function")),
    ];

    // the program, with P declared and set to a Piece first thing in Main, whose other statements are the lines given up to its End Sub.
    private static string LateBound(params string[] lines) => Program(["Public P As Object", "Public Sub Main()", "    Set P = New Piece", .. lines]);

    // the lines of the program, counted from 0, that call a member of P.
    private static int[] LateBoundLines(string program)
        => program.Split("\r\n").Select((line, number) => (line, number)).Where(each => each.line.Contains("P.", StringComparison.Ordinal))
            .Select(each => each.number).ToArray();

    // each late-bound call is a statement the interpreter runs: stepped, or a reason the body is the interpreter's.
    private static void AssertLateBoundIsStepped(string program, CompilationResult result)
    {
        var stepped = result.Stepped.Concat(result.Reasons).Select(reason => reason.Location!.Value.Range.Start.Line).ToHashSet();
        foreach (var line in LateBoundLines(program))
        {
            Assert.Contains(line, stepped,
                $"line {line}, a late-bound call, is no longer stepped: the row needs a statement compiled code does not do; give it another.");
        }
    }

    // the interpreter runs the statement of each of 3 passes that calls P, and the one after the loop: 4.
    [TestMethod]
    public async Task ALateBoundCallInALoop_IsTheInterpreters_AndTheLoopIsCompiled()
    {
        var program = LateBound(
            "    Debug.Print Built(3)",
            "End Sub",
            "Private Function Built(ByVal N As Long) As Long",
            "    Dim I As Long, S As String",
            "    For I = 1 To N",
            "        S = S & P.Text",
            "    Next",
            "    Built = P.Size(S)",
            "End Function");

        AssertLateBoundIsStepped(program, await AssertAlikeAsync(program, "Built", recoveries: 4, classes: Pieces));
    }

    [TestMethod]
    public async Task AGoTo_GoesWhereTheInterpreterSendsIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Jumped(1); Jumped(-1)",
            "End Sub",
            "Private Function Jumped(ByVal N As Long) As Long",
            "    If N > 0 Then GoTo Positive",
            "    Jumped = -1",
            "    Exit Function",
            "Positive:",
            "    Jumped = 1",
            "End Function"), "Jumped", recoveries: 1);

    // a member of a class: its Me is in its frame, and a scalar field of the object it is a call on is in the cell the object holds it in, which compiled
    // code reads and writes.
    [TestMethod]
    public async Task AMemberOfAClass_IsCompiled_WithTheFieldsOfMe()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New Counter",
            "    Debug.Print C.AddUpTo(3)",
            "    Debug.Print C.AddUpTo(4)",
            "End Sub"), "AddUpTo", recoveries: 0, classes: [("Counter", ModuleWorkspace.ClassModule("Counter",
                "Option Explicit",
                "Private mTotal As Long",
                "Public Function AddUpTo(ByVal N As Long) As Long",
                "    Dim I As Long, T As Long",
                "    For I = 1 To N",
                "        T = T + I",
                "    Next",
                "    mTotal = mTotal + T",
                "    AddUpTo = mTotal",
                "End Function"))]);

    // a Property Get and its Let are two procedures of one name, each compiled.
    [TestMethod]
    public async Task APropertyLet_IsCompiled_AndItsGet()
    {
        var program = Program(
            "Public Sub Main()",
            "    Dim B As New Box",
            "    B.Value = 4",
            "    Debug.Print B.Value",
            "End Sub");
        (string, string)[] box = [("Box", ModuleWorkspace.ClassModule("Box",
            "Option Explicit",
            "Private mValue As Long",
            "Public Property Let Value(ByVal V As Long)",
            "    Dim I As Long, T As Long",
            "    For I = 1 To V",
            "        T = T + I",
            "    Next",
            "    mValue = T",
            "End Property",
            "Public Property Get Value() As Long",
            "    Dim I As Long, T As Long",
            "    For I = 1 To 2",
            "        T = T + I",
            "    Next",
            "    Value = mValue + T",
            "End Property"))];

        await AssertAlikeAsync(program, "Let Value", recoveries: 0, classes: box);
        await AssertAlikeAsync(program, "Value", recoveries: 0, classes: box);
    }

    // a Property Set: its object parameter is the frame's, and what it does with it the interpreter's.
    [TestMethod]
    public async Task APropertySet_IsCompiled()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim H As New Holder",
            "    Set H.Items = New Collection",
            "    Debug.Print H.Total",
            "End Sub"), "Set Items", recoveries: 4, classes: [("Holder", ModuleWorkspace.ClassModule("Holder",
                "Option Explicit",
                "Private mItems As Collection",
                "Private mTotal As Long",
                "Public Property Set Items(ByVal Value As Collection)",
                "    Dim I As Long, T As Long",
                "    Set mItems = Value",
                "    For I = 1 To 3",
                "        mItems.Add I",
                "        T = T + I",
                "    Next",
                "    mTotal = T",
                "End Property",
                "Public Function Total() As Long",
                "    Total = mItems.Count + mTotal",
                "End Function"))]);

    // a Static outlives the activation: the frame's, from one call to the next.
    [TestMethod]
    public async Task AStatic_IsTheInterpreters_FromOneCallToTheNext()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted(); Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Static Calls As Long",
            "    Dim I As Long, T As Long",
            "    Calls = Calls + 1",
            "    For I = 1 To 3",
            "        T = T + I",
            "        Counted = Counted + Calls + T",
            "    Next",
            "End Function"), "Counted", recoveries: 8);

    // a For whose end is not compiled code's: the interpreter opens the loop and steps it, and the body is compiled.
    [TestMethod]
    public async Task AForTheInterpreterOpens_IsSteppedByItToo_AroundACompiledBody()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed(\"abcd\")",
            "End Sub",
            "Private Function Summed(ByVal S As String) As Long",
            "    Dim I As Long",
            "    For I = 1 To Len(S)",
            "        Summed = Summed + I",
            "    Next",
            "End Function"), "Summed", recoveries: 5);

    // the For statement of a Variant counter decides the type its loop counts in, and is the interpreter's; each Next is compiled code's.
    [TestMethod]
    public async Task AForWithAVariantCounter_IsSteppedByTheInterpreter_AndItsNextIsCompiled()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counting()",
            "End Sub",
            "Private Function Counting() As Long",
            "    Dim V As Variant",
            "    For V = 1 To 3",
            "        Counting = Counting + 1",
            "    Next",
            "End Function"), "Counting", recoveries: 1);

    // what the interpreter runs alone is said where it is: a Next, which has no statement of its own, at its For - one of a counter that is a variable of
    // the module, which compiled code holds nothing of.
    [TestMethod]
    public async Task WhatTheInterpreterRunsAlone_IsSaidWhereItIs_ANextAtItsFor()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Private M As Long",
            "Public Sub Main()",
            "    Debug.Print Counting()",
            "End Sub",
            "Private Function Counting() As Long",
            "    For M = 1 To 3",
            "        Counting = Counting + 1",
            "    Next",
            "End Function"), "Counting", recoveries: 4);

        var stepped = compiled.Stepped;
        Assert.HasCount(2, stepped, string.Join("; ", stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
        CollectionAssert.AreEqual(new[] { 5, 5 }, stepped.Select(reason => reason.Location!.Value.Range.Start.Line).ToArray(), "the For, counted from 0");
    }

    // an error the interpreter raises in a statement it runs alone, with no handler: the caller's handler catches it, as it does the interpreter's.
    [TestMethod]
    public async Task AnErrorInAStatementTheInterpreterRuns_GoesToTheCaller()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    On Error GoTo Caught",
            "    Debug.Print Failing(\"x\")",
            "    Exit Sub",
            "Caught:",
            "    Debug.Print \"caught \" & Err.Number",
            "End Sub",
            "Private Function Failing(ByVal S As String) As Long",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Failing = Failing + I",
            "    Next",
            "    Failing = CLng(S)",
            "End Function"), "Failing", recoveries: 0);

    [TestMethod]
    public async Task AnErrorInAStatementTheInterpreterRuns_GonePast_GoesOnCompiled()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Tolerant(\"x\")",
            "End Sub",
            "Private Function Tolerant(ByVal S As String) As Long",
            "    On Error Resume Next",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        Tolerant = Tolerant + I",
            "        Tolerant = Tolerant + CLng(S)",
            "    Next",
            "    Tolerant = Tolerant + 100",
            "End Function"), "Tolerant", recoveries: 3);

    // a procedure whose frame holds a String calls itself through the invoker, which makes it its frame - not through its own entry, which makes none.
    [TestMethod]
    public async Task AProcedureWithAString_CallsItselfWithAFrame()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Fact(5)",
            "End Sub",
            "Private Function Fact(ByVal N As Long) As Long",
            "    Dim Label As String",
            "    Label = \"n\" & N",
            "    If N <= 1 Then",
            "        Fact = 1",
            "    Else",
            "        Fact = Fact(N - 1)",
            "        Fact = Fact * N",
            "    End If",
            "End Function"), "Fact", recoveries: 5);

    // a compiled caller calls a callee whose frame holds a String through the invoker, which makes it its frame.
    [TestMethod]
    public async Task ACalleeWithAString_IsCalledWithAFrame()
    {
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Outer()",
            "End Sub",
            "Private Function Outer() As Long",
            "    Dim I As Long, T As Long",
            "    For I = 1 To 3",
            "        T = Labelled(I)",
            "        Outer = Outer + T",
            "    Next",
            "End Function",
            "Private Function Labelled(ByVal N As Long) As Long",
            "    Dim S As String",
            "    S = \"n\" & N",
            "    Labelled = Len(S) + N",
            "    Labelled = Labelled * 2",
            "End Function");

        await AssertAlikeAsync(program, "Outer", recoveries: 0);
        await AssertAlikeAsync(program, "Labelled", recoveries: 6);
    }

    // a variable a statement the interpreter runs gives an address - its VarPtr, an argument passed by reference to a Declare - is read back and written
    // where the frame holds it from then on: the cell bound at its address.
    [TestMethod]
    public async Task AVariableTheInterpreterGivesAnAddress_IsReadWhereItIs()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Touched()",
            "End Sub",
            "Private Function Touched() As Long",
            "    Dim N As Long, I As Long, P As LongPtr",
            "    N = 5",
            "    For I = 1 To 2",
            "        P = VarPtr(N)",
            "        Touched = Touched + N",
            "        N = N + 1",
            "    Next",
            "End Function"), "Touched", recoveries: 2);

    // a callee compiled code calls through its typed entry has no frame until the interpreter runs a statement of it alone, which is given one then.
    [TestMethod]
    public async Task ACalleeCalledWithoutAFrame_IsGivenOne_ForAStatementTheInterpreterRuns()
    {
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Outer()",
            "End Sub",
            "Private Function Outer() As Long",
            "    Dim I As Long, T As Long",
            "    For I = 1 To 3",
            "        T = Shown(I)",
            "        Outer = Outer + T",
            "    Next",
            "End Function",
            "Private Function Shown(ByVal N As Long) As Long",
            "    Dim T As Long",
            "    T = N * 2",
            "    Debug.Print T",
            "    Shown = T + 1",
            "End Function");

        var outer = await AssertAlikeAsync(program, "Outer", recoveries: 0);
        await AssertAlikeAsync(program, "Shown", recoveries: 3);
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(3, outer.Body!.DirectCalls, "Outer's direct calls");
        }
    }

    // a compiled variable passed by reference in a call the interpreter makes is its cell in the frame, which the callee writes: read back after the call.
    // The call is the interpreter's because it also gives a late-bound call's result; the interpreter runs it at each of 3 passes, and the Len after the
    // loop: 4.
    [TestMethod]
    public async Task AVariablePassedByReferenceInACallTheInterpreterMakes_IsWhatTheCalleeWrote()
    {
        var program = LateBound(
            "    Debug.Print Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Dim S As String, N As Long, I As Long",
            "    For I = 1 To 3",
            "        Append S, N, P.Text",
            "        N = N * 2",
            "    Next",
            "    Counted = N * 10 + Len(S)",
            "End Function",
            "Private Sub Append(ByRef S As String, ByRef N As Long, ByVal Tag As String)",
            "    S = S & Tag",
            "    N = N + 1",
            "End Sub");

        AssertLateBoundIsStepped(program, await AssertAlikeAsync(program, "Counted", recoveries: 4, classes: Pieces));
    }

    // an object the interpreter makes and calls: the frame's, from one statement to the next.
    [TestMethod]
    public async Task AnObject_IsTheFrames_FromOneStatementTheInterpreterRunsToTheNext()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Dim C As Collection, N As Long, I As Long",
            "    Set C = New Collection",
            "    For I = 1 To 3",
            "        C.Add I",
            "        N = N + I",
            "    Next",
            "    N = N + C.Count",
            "    Counted = N * 2",
            "End Function"), "Counted", recoveries: 5);

    // an error in a statement the interpreter runs, under On Error GoTo: the handler is gone to, and goes on compiled.
    [TestMethod]
    public async Task AnErrorInAStatementTheInterpreterRuns_GoesToTheHandler()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Guarded(\"x\")",
            "End Sub",
            "Private Function Guarded(ByVal S As String) As Long",
            "    On Error GoTo Failed",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Guarded = Guarded + I",
            "    Next",
            "    Guarded = Guarded + CLng(S)",
            "    Exit Function",
            "Failed:",
            "    Guarded = Guarded * 100 + Err.Number",
            "End Function"), "Guarded", recoveries: 2);

    // a GoSub to a statement the interpreter runs, and a Return to one: at each of 2 passes, the statement GoSub lands on and the one Return lands on, 4.
    [TestMethod]
    public async Task AGoSubAndItsReturn_LandOnStatementsTheInterpreterRuns()
    {
        var program = LateBound(
            "    Debug.Print Routed()",
            "End Sub",
            "Private Function Routed() As Long",
            "    Dim S As String, I As Long, T As Long",
            "    For I = 1 To 2",
            "        GoSub Build",
            "        Routed = Routed + P.Size(S)",
            "        T = T + I",
            "    Next",
            "    Routed = Routed * 10 + T",
            "    Exit Function",
            "Build:",
            "    S = S & P.Text & P.Text",
            "    Return",
            "End Function");

        AssertLateBoundIsStepped(program, await AssertAlikeAsync(program, "Routed", recoveries: 4, classes: Pieces));
    }

    // an End the interpreter runs alone ends the program, there.
    [TestMethod]
    public async Task AnEnd_EndsTheProgram()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print \"a\"",
            "    Ended",
            "    Debug.Print \"never\"",
            "End Sub",
            "Private Sub Ended()",
            "    Dim I As Long, T As Long",
            "    For I = 1 To 3",
            "        T = T + I",
            "    Next",
            "    Debug.Print T",
            "    End",
            "End Sub"), "Ended", recoveries: 1);

    // a member calls another member of the object it is a call on, Me: each call is the interpreter's, and what the callee assigns the field is what
    // compiled code reads of it after.
    [TestMethod]
    public async Task AMemberCallingAnotherMemberOfMe_IsCompiled()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New Counter",
            "    Debug.Print C.AddUpTo(3)",
            "End Sub"), "AddUpTo", recoveries: 3, classes: [("Counter", ModuleWorkspace.ClassModule("Counter",
                "Option Explicit",
                "Private mTotal As Long",
                "Public Function AddUpTo(ByVal N As Long) As Long",
                "    Dim I As Long, T As Long",
                "    For I = 1 To N",
                "        Bump I",
                "        T = T + I",
                "    Next",
                "    AddUpTo = mTotal + T",
                "End Function",
                "Private Sub Bump(ByVal N As Long)",
                "    mTotal = mTotal + N",
                "End Sub"))]);

    // a body whose loop does nothing compiled - each statement of it the interpreter's - is the interpreter's whole, though compiled code would do a statement
    // outside it: around each statement of the loop it would only write its variables into the frame and read them back, which costs more than it saves.
    [TestMethod]
    public async Task ABodyWhoseLoopIsTheInterpreters_IsInterpreted_WithItsReasons()
    {
        var program = LateBound(
            "    Debug.Print Cheap()",
            "End Sub",
            "Private Function Cheap() As Long",
            "    Dim N As Long, S As String",
            "    N = 7",
            "    Do",
            "        S = S & P.Text",
            "    Loop Until P.Size(S) >= 5",
            "    Cheap = N + P.Size(S)",
            "End Function");
        var compiled = await AssertAlikeAsync(program, Pieces);

        // the reasons are the statements the interpreter would have run alone: the three that call P.
        var cheap = compiled["Cheap"];
        Assert.IsNull(cheap.Body);
        Assert.IsEmpty(cheap.Stepped);
        CollectionAssert.AreEqual(LateBoundLines(program), cheap.Reasons.Select(reason => reason.Location!.Value.Range.Start.Line).ToArray(),
            string.Join("; ", cheap.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }

    // what each loop gains is its own: a loop whose statements are all the interpreter's keeps the body interpreted, whatever a loop beside it or around it
    // does compiled; one that does something compiled at each pass, beside it or inside it, does not.
    [TestMethod]
    [DataRow("For I = 1 To 3|    T = T + I|Next|Do|    S = S & P.Text|Loop Until P.Size(S) >= 10", false, DisplayName = "a compiled loop beside one that is all stepped")]
    [DataRow("For I = 1 To 3|    T = T + I|Next|Do|    S = S & P.Text|    T = T + 1|Loop Until P.Size(S) >= 10", true,
        DisplayName = "two loops that each do something compiled")]
    [DataRow("For I = 1 To 3|    T = T + I|    S = \"\"|    Do|        S = S & P.Text|    Loop Until P.Size(S) >= 10|Next", false,
        DisplayName = "a compiled loop around one that is all stepped")]
    [DataRow("For I = 1 To 3|    S = \"\"|    Do|        S = S & P.Text|        T = T + 1|    Loop Until P.Size(S) >= 10|Next", true,
        DisplayName = "a loop around one that does something compiled")]
    [DataRow("Do|    S = S & P.Text|    Select Case T|        Case 1|    End Select|Loop Until P.Size(S) >= 10", true,
        DisplayName = "a loop whose Select Case compares compiled")]
    [DataRow("Do|    Select Case True|        Case P.Size(S) = 0|            S = P.Text|        Case Else|            S = S & P.Text|    End Select|Loop Until P.Size(S) >= 10",
        false, DisplayName = "a loop whose Select Case compares nothing compiled")]
    public async Task EachLoop_GainsForItself(string loops, bool compiles)
    {
        var program = LateBound([
            "    Debug.Print Pad()",
            "End Sub",
            "Private Function Pad() As Long",
            "    Dim S As String, I As Long, T As Long",
            .. loops.Split('|').Select(line => "    " + line),
            "    Pad = P.Size(S) + T",
            "End Function"]);
        var compiled = await AssertAlikeAsync(program, Pieces);

        var pad = compiled["Pad"];
        Assert.AreEqual(compiles, pad.Body is not null, string.Join("; ", pad.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
        AssertLateBoundIsStepped(program, pad);
    }

    // a body that would do nothing of its own compiled - every statement that does something the interpreter's - is the interpreter's whole, and says why.
    [TestMethod]
    public async Task ABodyEveryStatementOfWhichIsTheInterpreters_IsInterpreted_WithItsReasons()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Joined(\"a\", \"b\")",
            "End Sub",
            "Private Function Joined(ByVal A As String, ByVal B As String) As String",
            "    Joined = A & B",
            "End Function"));

        // the reasons are what the interpreter would have run alone: its one statement, which assigns a String.
        var joined = compiled["Joined"];
        Assert.IsNull(joined.Body);
        Assert.IsEmpty(joined.Stepped);
        var reason = Assert.ContainsSingle(joined.Reasons);
        Assert.AreEqual((IneligibilityKind.AssignmentTarget, "Joined", 4), (reason.Kind, reason.Detail, reason.Location!.Value.Range.Start.Line),
            $"{reason.Kind} {reason.Detail}");
    }
}
