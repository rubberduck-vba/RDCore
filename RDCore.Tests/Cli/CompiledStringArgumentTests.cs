using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>String</c> given to a procedure of the workspace by compiled code, as the interpreter gives it (MS-VBAL §5.3.1.11): the text of Strings given by
/// value, of which the callee makes a <c>String</c> of its own; a <c>String</c> variable given by reference, which the parameter is a second name for, with
/// the variable's one address. A compiled callee is called directly, through its typed entry: its activation holds the cells of its Strings, and its frame
/// is given them once something is to see it. What compiled code gives nothing of (a <c>Variant</c>, a number, a field, an element) the interpreter gives,
/// making the call. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledStringArgumentTests
{
    // the calls the caller made directly, and none through the invoker: a callee given Strings is given their text, or the cell of the variable given by
    // reference.
    private static void AssertCalledDirectly(CompilationResult result, long direct)
    {
        if (CompiledExecution.HandsOverAtRandom)
        {
            return;
        }

        Assert.AreEqual((direct, 0L), (result.Body!.DirectCalls, result.Body.IndirectCalls), "the caller's calls made directly, and through the invoker");
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
            Assert.Contains(line, stepped, $"line {line}, a call compiled code gives no argument of, is no longer stepped (stepped: {string.Join(", ", stepped.Order())}): the row pins what the interpreter gives.");
        }
    }

    // the lines of the program that make the call statement written as `call` are each run by the interpreter as a support call, which steps nothing:
    // the call is made through the invoker, and the variables are read back after it.
    private static void AssertCallsAreSupportCalls(string program, CompilationResult result, string call)
    {
        var stepped = result.Stepped.Concat(result.Reasons).Select(reason => reason.Location!.Value.Range.Start.Line).ToHashSet();
        var calls = program.Split("\r\n").Select((line, number) => (line, number))
            .Where(each => each.line.Contains(call, StringComparison.Ordinal))
            .Select(each => each.number).ToArray();
        Assert.IsNotEmpty(calls);
        foreach (var line in calls)
        {
            Assert.DoesNotContain(line, stepped, $"line {line}, a call statement compiled code gives no argument of, is stepped: it is a support call.");
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

        AssertCalledDirectly(result, 4);
    }

    // a String variable given by reference: the callee writes the caller's variable, which the caller reads after the call. Built = S, a Let of the
    // function's String result, is compiled code's too: none.
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
            "End Sub"), "Built", recoveries: 0);

        AssertCalledDirectly(result, 4);
    }

    // one String variable given to two parameters by reference: both are second names for it, and each write is seen through the other, as the interpreter
    // aliases them. Built = S, a Let of the function's String result, is compiled code's too: none.
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
            "End Sub"), "Built", recoveries: 0);

        AssertCalledDirectly(result, 1);
    }

    // a String given by reference to a callee that gives it on by reference: the second callee writes the first caller's variable. Built = S, a Let of
    // the function's String result, is compiled code's too: none.
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
            "End Sub"), "Built", recoveries: 0);

        AssertCalledDirectly(result, 1);
    }

    // a String given by reference to a callee that assigns it, then raises an error, which the caller's handler handles: the caller's variable was assigned
    // before the error, as the interpreter sees it, and Resume Next goes on after the call. The call that raised, whose error the interpreter gives the
    // handler; and the Resume Next, always the interpreter's: 2. Caught = S, a Let of the function's String result, is compiled code's.
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
            "End Sub"), "Caught", recoveries: 2);

    // a Boolean given to a String parameter by value is let-coerced to a String, which the interpreter does, making the call: 1. A Variant holding a String
    // is given its text by compiled code (CompiledVariantTextTests), and so is V = "ab", a String assigned to a Variant (CompiledVariantTextLetTests); a whole
    // number is given its digits (CompiledStringResultTests).
    [TestMethod]
    public async Task ABooleanGivenToAString_IsTheInterpretersToCoerce()
    {
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Given()",
            "End Sub",
            "Private Function Given() As Long",
            "    Dim V As Variant",
            "    Dim L As Boolean",
            "    Dim N As Long",
            "    V = \"ab\"",
            "    L = True",
            "    N = Size(V)",
            "    Given = N",
            "    N = Size(L)",
            "    Given = Given * 10 + N",
            "End Function",
            "Private Function Size(ByVal S As String) As Long",
            "    If S = \"ab\" Then Size = 2 Else If S = \"True\" Then Size = 4",
            "End Function");
        var result = await AssertAlikeAsync(program, "Given", recoveries: 1);

        AssertCallsAreStepped(program, result, "= Size(L)");
    }

    // a String literal, and a String constant, given to a String parameter: each is given its text, and no call is the interpreter's.
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
            "End Function"), "Given", recoveries: 0);

        AssertCalledDirectly(result, 2);
    }

    // vbNullString given by value is given as itself, the null pointer it is, which the callee sees; S = vbNullString, a constant, is compiled code's.
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
            "End Function"), "Given", recoveries: 0);

        AssertCalledDirectly(result, 1);
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

    // a String field of the module, and an element of an array of Strings, given by reference: the interpreter gives them, making the call, as a support
    // call. The two Lets of them, and the Let of the function's String result, are stepped: 3.
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
        var result = await AssertAlikeAsync(program, "Given", recoveries: 3);

        AssertCallsAreSupportCalls(program, result, "    Appended ");
    }

    // a String variable given by reference has one address, whichever of its names it is asked for by, as MS-VBA gives it (VarPtr of a ByRef parameter is
    // the variable's): at each of 3 calls, the same; through a callee that gives its parameter on; to both parameters of a call given it twice; and to
    // the caller after the calls. Its text is where the callee read it (StrPtr). Each digit is 1 when the address is the one first seen. The two Ifs of
    // Probed that call VarPtr and StrPtr are the interpreter's: 2.
    [TestMethod]
    public async Task AStringGivenByReference_HasTheAddressOfTheCallersVariable()
    {
        var result = await AssertAlikeAsync(Program(
            "Private First As LongPtr",
            "Private Text As LongPtr",
            "Public Sub Main()",
            "    Debug.Print Probed()",
            "End Sub",
            "Private Function Probed() As Long",
            "    Dim S As String",
            "    Dim I As Long",
            "    Dim N As Long",
            "    S = \"abc\"",
            "    For I = 1 To 3",
            "        N = Probe(S)",
            "        Probed = Probed * 10 + N",
            "    Next",
            "    N = Passed(S)",
            "    Probed = Probed * 10 + N",
            "    N = Twice(S, S)",
            "    Probed = Probed * 10 + N",
            "    If VarPtr(S) = First Then Probed = Probed * 10 + 1 Else Probed = Probed * 10 + 2",
            "    If StrPtr(S) = Text Then Probed = Probed * 10 + 1 Else Probed = Probed * 10 + 2",
            "End Function",
            "Private Function Probe(ByRef T As String) As Long",
            "    If First = 0 Then First = VarPtr(T): Text = StrPtr(T)",
            "    If VarPtr(T) = First Then Probe = 1 Else Probe = 2",
            "End Function",
            "Private Function Passed(ByRef T As String) As Long",
            "    Passed = Probe(T)",
            "End Function",
            "Private Function Twice(ByRef A As String, ByRef B As String) As Long",
            "    If VarPtr(A) = VarPtr(B) And VarPtr(A) = First Then Twice = 1 Else Twice = 2",
            "End Function"), "Probed", recoveries: 2, expected: " 1111111 ");

        AssertCalledDirectly(result, 5);
    }

    // a String and a Long given by reference to one call: each the caller's variable, which the callee writes. N goes 1, 2, 3, 6, 7, 14, and S is "xxx":
    // 143. Compiled code calls Len(S), so the interpreter recovers nothing: 0 recoveries.
    [TestMethod]
    public async Task AStringAndALongGivenByReference_AreBothTheCallersVariables()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Dim S As String, N As Long, I As Long",
            "    For I = 1 To 3",
            "        Append S, N",
            "        N = N * 2",
            "    Next",
            "    Counted = N * 10 + Len(S)",
            "End Function",
            "Private Sub Append(ByRef S As String, ByRef N As Long)",
            "    S = S & \"x\"",
            "    N = N + 1",
            "End Sub"), "Counted", recoveries: 0, expected: " 143 ");

        AssertCalledDirectly(result, 3);
    }

    // a String variable in parentheses, and a String joined to "", given to a parameter passed by reference: values, of which the callee makes a String of
    // its own (MS-VBAL §5.3.1.11), so the caller's variable is as it was. Every statement of Kept is compiled: 0.
    [TestMethod]
    public async Task AParenthesizedStringAndAJoinedOne_AreGivenAsValues()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Kept()",
            "End Sub",
            "Private Function Kept() As Long",
            "    Dim S As String, I As Long",
            "    S = \"ab\"",
            "    For I = 1 To 2",
            "        Grow (S)",
            "        Grow S & \"\"",
            "    Next",
            "    If S = \"ab\" Then Kept = 1 Else Kept = 2",
            "End Function",
            "Private Sub Grow(ByRef T As String)",
            "    T = T & \"x\"",
            "End Sub"), "Kept", recoveries: 0, expected: " 1 ");

        AssertCalledDirectly(result, 4);
    }

    // Strings given by name, in another order than their parameters, and an Optional String omitted: the callee is given each where its parameter is, and
    // its default when it is omitted. S is "y", "yd", "ydy", then "ydyd". Compiled code makes the call given names, at each of 2 passes; it gives no
    // argument of an omitted parameter, so the interpreter makes that call, as a support call, at each of 2 passes, stepping nothing: 0.
    [TestMethod]
    public async Task StringsGivenByName_AreGivenWhereTheirParametersAre_AndAnOmittedOneIsTheInterpretersToGive()
    {
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Tagged()",
            "End Sub",
            "Private Function Tagged() As Long",
            "    Dim S As String, I As Long",
            "    For I = 1 To 2",
            "        Tag Suffix:=\"y\", T:=S",
            "        Call Tag(S)",
            "    Next",
            "    If S = \"ydyd\" Then Tagged = 1 Else Tagged = 2",
            "End Function",
            "Private Sub Tag(ByRef T As String, Optional ByVal Suffix As String = \"d\")",
            "    T = T & Suffix",
            "End Sub");
        var result = await AssertAlikeAsync(program, "Tagged", recoveries: 0, expected: " 1 ");

        AssertCallsAreSupportCalls(program, result, "Call Tag(S)");
        AssertCalledDirectly(result, 2);
    }

    // how many activations of the procedure named were given a frame after they were entered: by the interpreter running a statement of theirs, or
    // something looking at the stack. Under the random walk-back any activation may be handed to the interpreter.
    private static void AssertFramesMade(IReadOnlyDictionary<string, CompilationResult> compiled, string procedure, long frames)
    {
        if (CompiledExecution.HandsOverAtRandom)
        {
            return;
        }

        Assert.AreEqual(frames, compiled[procedure].Body!.FramesMade, $"the activations of {procedure} given a frame after they were entered");
    }

    // a frame made in the middle of a call, for the interpreter to run a statement compiled code gives it (StrPtr), holds the Strings the activation was
    // given and its own: the parameter by reference as the caller's variable, which the interpreter writes and compiled code then goes on with. Each of the
    // 3 calls appends "yzy." to S.
    [TestMethod]
    public async Task AFrameMadeInTheMiddleOfACall_HoldsTheStringsOfTheActivation()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Built()",
            "End Sub",
            "Private Function Built() As String",
            "    Dim S As String, I As Long",
            "    S = \"x\"",
            "    For I = 1 To 3",
            "        Probed S, \"y\"",
            "    Next",
            "    Built = S",
            "End Function",
            "Private Sub Probed(ByRef T As String, ByVal A As String)",
            "    Dim L As String",
            "    L = A & \"z\"",
            "    If StrPtr(L) <> 0 Then T = T & L & A",
            "    T = T & \".\"",
            "End Sub"), expected: "xyzy.yzy.yzy.");

        AssertCalledDirectly(compiled["Built"], 3);
        AssertFramesMade(compiled, "Probed", 3);
    }

    // the String of an activation is vbNullString when the activation begins, at each call (Kept, its activation made again for the next call), and each
    // activation of a procedure that calls itself holds its own (Nested): its String after the call returns is the one it assigned before it. No activation
    // is given a frame.
    [TestMethod]
    public async Task TheStringsOfAnActivation_AreItsOwn_AtEachCallAndUnderRecursion()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Built()",
            "End Sub",
            "Private Function Built() As String",
            "    Dim S As String, I As Long",
            "    For I = 1 To 3",
            "        Kept S, \"k\"",
            "    Next",
            "    For I = 1 To 2",
            "        Nested S, 2",
            "    Next",
            "    Built = S",
            "End Function",
            "Private Sub Kept(ByRef T As String, ByVal A As String)",
            "    Dim L As String",
            "    T = T & \"[\" & L & \"]\"",
            "    L = A",
            "    T = T & L",
            "End Sub",
            "Private Sub Nested(ByRef T As String, ByVal N As Long)",
            "    Dim L As String",
            "    T = T & \"(\" & L",
            "    L = \"n\" & N",
            "    If N > 0 Then Nested T, N - 1",
            "    T = T & L & \")\"",
            "End Sub"), expected: "[]k[]k[]k(((n0)n1)n2)(((n0)n1)n2)");

        AssertCalledDirectly(compiled["Built"], 5);
        AssertCalledDirectly(compiled["Nested"], 4);
        AssertFramesMade(compiled, "Kept", 0);
        AssertFramesMade(compiled, "Nested", 0);
    }

    // a String comparison in a member entered without a frame is made as its own module says (MS-VBAL §5.2.1.1): Text, in a class a module comparing
    // Binary calls. Same is 1 at each of 2 calls, and "abc" = "ABC" in the module is False: 112.
    [TestMethod]
    public async Task AStringComparisonInAnActivationWithoutAFrame_IsMadeAsItsModuleSays()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Compared()",
            "End Sub",
            "Private Function Compared() As Long",
            "    Dim C As Comparer, I As Long, S As String",
            "    Set C = New Comparer",
            "    For I = 1 To 2",
            "        Compared = Compared * 10 + C.Same(1)",
            "    Next",
            "    S = \"abc\"",
            "    If S = \"ABC\" Then Compared = Compared * 10 + 1 Else Compared = Compared * 10 + 2",
            "End Function"),
            [("Comparer", ModuleWorkspace.ClassModule("Comparer", "Option Compare Text",
                "Public Function Same(ByVal N As Long) As Long",
                "    Dim S As String",
                "    S = \"abc\"",
                "    If S = \"ABC\" Then Same = N",
                "End Function"))], expected: " 112 ");

        AssertCalledDirectly(compiled["Compared"], 2);
        AssertFramesMade(compiled, "Same", 0);
    }

    // a String the interpreter gives by reference to a compiled callee, which gives it on: a field, which compiled code holds in a cell of its own, is
    // given on directly; an element of an array, which it does not, through the invoker, by its address. Either way the parameter is the variable, at the
    // address the first callee saw (VarPtr), and the field's own: 111.
    [TestMethod]
    public async Task AStringGivenOnByReference_IsTheVariable_WhereverItIsHeld()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Private Field As String",
            "Private Seen As LongPtr",
            "Private Hits As Long",
            "Public Sub Main()",
            "    Debug.Print Given()",
            "End Sub",
            "Private Function Given() As String",
            "    Dim A(1 To 2) As String",
            "    Field = \"f\"",
            "    A(1) = \"e\"",
            "    Passed Field",
            "    If Seen = VarPtr(Field) Then Hits = Hits * 10 + 1 Else Hits = Hits * 10 + 2",
            "    Passed A(1)",
            "    Given = Field & A(1) & Hits",
            "End Function",
            "Private Sub Passed(ByRef T As String)",
            "    Seen = VarPtr(T)",
            "    Appended T",
            "End Sub",
            "Private Sub Appended(ByRef T As String)",
            "    T = T & \"!\"",
            "    If VarPtr(T) = Seen Then Hits = Hits * 10 + 1 Else Hits = Hits * 10 + 2",
            "End Sub"), expected: "f!e!111");

        if (!CompiledExecution.HandsOverAtRandom)
        {
            var passed = compiled["Passed"].Body!;
            Assert.AreEqual((1L, 1L), (passed.DirectCalls, passed.IndirectCalls), "Passed's calls made directly, and through the invoker");
        }
    }
}
