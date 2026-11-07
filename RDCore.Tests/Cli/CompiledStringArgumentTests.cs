using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>String</c> given to a procedure of the workspace by compiled code, as the interpreter gives it (MS-VBAL §5.3.1.11): the text of Strings given by
/// value, of which the callee makes a <c>String</c> of its own; the cell of a <c>String</c> variable given by reference, which the parameter is a second name
/// for. The call is made through the invoker, which makes the callee its frame. What compiled code gives nothing of - a <c>Variant</c>, a number, a
/// field, an element - the interpreter gives, making the call. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledStringArgumentTests
{
    // the calls the caller made through the invoker, and none directly: a callee given a String has a frame, which the invoker makes it.
    private static void AssertCalledThroughInvoker(CompilationResult result, long indirect)
    {
        if (CompiledExecution.HandsOverAtRandom)
        {
            return;
        }

        Assert.AreEqual((0L, indirect), (result.Body!.DirectCalls, result.Body.IndirectCalls), "the caller's calls made directly, and through the invoker");
    }

    // the lines of the program that make the call written as `call` are each the interpreter's to run, with the call.
    private static void AssertCallsAreStepped(string program, CompilationResult result, string call)
    {
        var stepped = result.Stepped.Concat(result.Reasons).Select(reason => reason.Location!.Value.Range.Start.Line).ToHashSet();
        var calls = program.Split("\r\n").Select((line, number) => (line, number))
            .Where(each => each.line.Contains(call, StringComparison.Ordinal))
            .Select(each => each.number).ToArray();
        Assert.IsNotEmpty(calls);
        foreach (var line in calls)
        {
            Assert.Contains(line, stepped, $"line {line}, a call compiled code gives no argument of, is no longer stepped: the row pins what the interpreter gives.");
        }
    }

    // Strings given by value: the callee's own, which it assigns without the caller seeing it - the next call is given what the caller's variable holds.
    // Every statement of Built is compiled: 0.
    [TestMethod]
    public async Task AStringGivenByValue_IsTheCalleesOwn()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Built()",
            "End Sub",
            "Private Function Built() As Long",
            "    Dim S As String",
            "    Dim I As Long",
            "    Dim N As Long",
            "    S = \"x\"",
            "    For I = 1 To 4",
            "        N = Scored(S)",
            "        Built = Built * 10 + N",
            "        S = S & \"y\"",
            "    Next",
            "End Function",
            "Private Function Scored(ByVal S As String) As Long",
            "    S = S & S",
            "    If S = \"xyxy\" Then Scored = 1 Else Scored = 2",
            "End Function"), "Built", recoveries: 0);

        AssertCalledThroughInvoker(result, 4);
    }

    // a String variable given by reference: the callee writes the caller's variable, which the caller reads after the call. Built = S, a Let of the
    // function's String result, is the interpreter's: 1.
    [TestMethod]
    public async Task AStringGivenByReference_IsTheCallersVariable()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Built()",
            "End Sub",
            "Private Function Built() As String",
            "    Dim S As String",
            "    Dim I As Long",
            "    For I = 1 To 4",
            "        Appended S",
            "        S = S & \"a\"",
            "    Next",
            "    Built = S",
            "End Function",
            "Private Sub Appended(ByRef T As String)",
            "    T = T & \"!\"",
            "End Sub"), "Built", recoveries: 1);

        AssertCalledThroughInvoker(result, 4);
    }

    // one String variable given to two parameters by reference: both are second names for it, and each write is seen through the other, as the interpreter
    // aliases them. Built = S, a Let of the function's String result, is the interpreter's: 1.
    [TestMethod]
    public async Task OneStringGivenTwiceByReference_IsBothParameters()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Built()",
            "End Sub",
            "Private Function Built() As String",
            "    Dim S As String",
            "    S = \"x\"",
            "    Twice S, S",
            "    Built = S",
            "End Function",
            "Private Sub Twice(ByRef A As String, ByRef B As String)",
            "    A = A & \"a\"",
            "    B = B & \"b\"",
            "    A = A & B",
            "End Sub"), "Built", recoveries: 1);

        AssertCalledThroughInvoker(result, 1);
    }

    // a String given by reference to a callee that gives it on by reference: the second callee writes the first caller's variable. Built = S, a Let of
    // the function's String result, is the interpreter's: 1.
    [TestMethod]
    public async Task AStringGivenOnByReference_IsTheFirstCallersVariable()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Built()",
            "End Sub",
            "Private Function Built() As String",
            "    Dim S As String",
            "    S = \"x\"",
            "    Outer S",
            "    Built = S",
            "End Function",
            "Private Sub Outer(ByRef T As String)",
            "    Appended T",
            "    T = T & \"?\"",
            "End Sub",
            "Private Sub Appended(ByRef U As String)",
            "    U = U & \"!\"",
            "End Sub"), "Built", recoveries: 1);

        AssertCalledThroughInvoker(result, 1);
    }

    // a String given by reference to a callee that assigns it, then raises an error, which the caller's handler handles: the caller's variable was assigned
    // before the error, as the interpreter sees it, and Resume Next goes on after the call. The call that raised, whose error the interpreter gives the
    // handler; the Resume Next, always the interpreter's; and Caught = S, a Let of the function's String result: 3.
    [TestMethod]
    public async Task AStringAssignedByACalleeThatRaises_IsAssignedWhenTheHandlerRuns()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Caught()",
            "End Sub",
            "Private Function Caught() As String",
            "    Dim S As String",
            "    On Error GoTo Handled",
            "    S = \"x\"",
            "    Failing S",
            "    Caught = S",
            "    Exit Function",
            "Handled:",
            "    S = S & \"?\"",
            "    Resume Next",
            "End Function",
            "Private Sub Failing(ByRef T As String)",
            "    T = T & \"!\"",
            "    Err.Raise 5",
            "End Sub"), "Caught", recoveries: 3);

    // a Variant holding a String, and a Long, given to a String parameter by value: each is let-coerced to a String, which the interpreter does, making the
    // call. V = "ab", a String assigned to a Variant, and the two calls are the interpreter's: 3.
    [TestMethod]
    public async Task AVariantAndALongGivenToAString_AreTheInterpretersToCoerce()
    {
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Given()",
            "End Sub",
            "Private Function Given() As Long",
            "    Dim V As Variant",
            "    Dim L As Long",
            "    Dim N As Long",
            "    V = \"ab\"",
            "    L = 42",
            "    N = Size(V)",
            "    Given = N",
            "    N = Size(L)",
            "    Given = Given * 10 + N",
            "End Function",
            "Private Function Size(ByVal S As String) As Long",
            "    If S = \"ab\" Then Size = 2 Else If S = \"42\" Then Size = 4",
            "End Function");
        var result = await AssertAlikeAsync(program, "Given", recoveries: 3);

        AssertCallsAreStepped(program, result, "= Size(");
    }

    // a String literal, and a String constant, given to a String parameter: the literal is given its text. A constant is the interpreter's to read, and
    // so to give, making the call: 1.
    [TestMethod]
    public async Task AStringLiteralAndAStringConstant_AreGivenTheirText()
    {
        var result = await AssertAlikeAsync(Program(
            "Private Const Named As String = \"cd\"",
            "Public Sub Main()",
            "    Debug.Print Given()",
            "End Sub",
            "Private Function Given() As Long",
            "    Dim N As Long",
            "    N = Size(\"ab\")",
            "    Given = N",
            "    N = Size(Named)",
            "    Given = Given * 10 + N",
            "End Function",
            "Private Function Size(ByVal S As String) As Long",
            "    If S = \"ab\" Then Size = 2 Else If S = \"cd\" Then Size = 4",
            "End Function"), "Given", recoveries: 1);

        AssertCalledThroughInvoker(result, 1);
    }

    // vbNullString given by value is given as itself, the null pointer it is, which the callee sees: S = vbNullString, a constant, is the interpreter's: 1.
    [TestMethod]
    public async Task VbNullStringGivenByValue_IsGivenAsItself()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Given()",
            "End Sub",
            "Private Function Given() As Long",
            "    Dim S As String",
            "    S = vbNullString",
            "    Given = Pointed(S)",
            "End Function",
            "Private Function Pointed(ByVal S As String) As Long",
            "    If StrPtr(S) = 0 Then Pointed = 1 Else Pointed = 2",
            "End Function"), "Given", recoveries: 1);

        AssertCalledThroughInvoker(result, 1);
    }

    // a String that ends with a lone byte, and a fixed-length String, given to a String parameter: compiled code reads no text of either, and the
    // interpreter makes each call - the first when compiled code finds the lone byte as it reads it, the second always (the call is stepped).
    // S = ChrB(65), a call of a function of a String; F = "abcdef", a Let of a fixed-length String; and the two calls: 4.
    [TestMethod]
    public async Task ALoneByteStringAndAFixedLengthString_AreTheInterpretersToGive()
    {
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Given()",
            "End Sub",
            "Private Function Given() As Long",
            "    Dim S As String",
            "    Dim F As String * 3",
            "    Dim N As Long",
            "    S = ChrB(65)",
            "    F = \"abcdef\"",
            "    N = Size(S)",
            "    Given = N",
            "    N = Size(F)",
            "    Given = Given * 10 + N",
            "End Function",
            "Private Function Size(ByVal S As String) As Long",
            "    Size = LenB(S)",
            "End Function");
        var result = await AssertAlikeAsync(program, "Given", recoveries: 4);

        AssertCallsAreStepped(program, result, "= Size(F)");
    }

    // a String field of the module, and an element of an array of Strings, given by reference: the interpreter gives them, making the call. The two Lets
    // of them, the two calls, and the Let of the function's String result are the interpreter's: 5.
    [TestMethod]
    public async Task AStringFieldAndAStringElementGivenByReference_AreTheInterpretersToGive()
    {
        var program = Program(
            "Private Field As String",
            "Public Sub Main()",
            "    Debug.Print Given()",
            "End Sub",
            "Private Function Given() As String",
            "    Dim A(1 To 2) As String",
            "    Dim N As Long",
            "    Field = \"f\"",
            "    A(1) = \"e\"",
            "    N = N + 1",
            "    Appended Field",
            "    Appended A(1)",
            "    N = N * 2",
            "    Given = Field & A(1) & N",
            "End Function",
            "Private Sub Appended(ByRef T As String)",
            "    T = T & \"!\"",
            "End Sub");
        var result = await AssertAlikeAsync(program, "Given", recoveries: 5);

        AssertCallsAreStepped(program, result, "    Appended ");
    }
}
