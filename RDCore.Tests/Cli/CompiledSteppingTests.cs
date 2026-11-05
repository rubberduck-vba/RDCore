using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// Instructions compiled code does not do, in a compiled procedure (IR lowering S2.5a): the interpreter runs each alone, and the activation goes on compiled
/// where it leaves it. What compiled code holds nothing of - <c>Me</c>, an object, a <c>String</c>, a <c>Variant</c>, a <c>Static</c> - is the frame's,
/// and the instructions that read or write it the interpreter's. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledSteppingTests
{
    [TestMethod]
    public async Task AStringBuiltInALoop_IsTheInterpreters_AndTheLoopIsCompiled()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Built(3)",
            "End Sub",
            "Private Function Built(ByVal N As Long) As Long",
            "    Dim I As Long, S As String",
            "    For I = 1 To N",
            "        S = S & \"x\"",
            "    Next",
            "    Built = Len(S)",
            "End Function"), "Built", recoveries: 4);

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

    // a member of a class: its Me is in its frame, and so are the fields of the object it is a call on.
    [TestMethod]
    public async Task AMemberOfAClass_IsCompiled_AndItsFieldsAreTheInterpreters()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New Counter",
            "    Debug.Print C.AddUpTo(3)",
            "    Debug.Print C.AddUpTo(4)",
            "End Sub"), "AddUpTo", recoveries: 4, classes: [("Counter", ModuleWorkspace.ClassModule("Counter",
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

    [TestMethod]
    public async Task APropertyLet_IsCompiled()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim B As New Box",
            "    B.Value = 4",
            "    Debug.Print B.Total",
            "End Sub"), "Value", recoveries: 1, classes: [("Box", ModuleWorkspace.ClassModule("Box",
                "Option Explicit",
                "Private mValue As Long",
                "Public Property Let Value(ByVal V As Long)",
                "    Dim I As Long, T As Long",
                "    For I = 1 To V",
                "        T = T + I",
                "    Next",
                "    mValue = T",
                "End Property",
                "Public Function Total() As Long",
                "    Total = mValue",
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
            "    Dim I As Long",
            "    Calls = Calls + 1",
            "    For I = 1 To 3",
            "        Counted = Counted + Calls",
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

    [TestMethod]
    public async Task AForWithAVariantCounter_IsTheInterpreters_AroundACompiledBody()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counting()",
            "End Sub",
            "Private Function Counting() As Long",
            "    Dim V As Variant",
            "    For V = 1 To 3",
            "        Counting = Counting + 1",
            "    Next",
            "End Function"), "Counting", recoveries: 4);

    // what the interpreter runs alone is said where it is: a Next, which has no statement of its own, at its For.
    [TestMethod]
    public async Task WhatTheInterpreterRunsAlone_IsSaidWhereItIs_ANextAtItsFor()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counting()",
            "End Sub",
            "Private Function Counting() As Long",
            "    Dim V As Variant",
            "    For V = 1 To 3",
            "        Counting = Counting + 1",
            "    Next",
            "End Function"));

        var stepped = compiled["Counting"].Stepped;
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

        Assert.IsNull(compiled["Joined"].Body);
        Assert.IsNotEmpty(compiled["Joined"].Reasons);
        Assert.IsEmpty(compiled["Joined"].Stepped);
    }
}
