using RDCore.Runtime.Compiled;
using RDCore.Runtime.LanguageCore.Instructions;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>For Each</c> in compiled code: its statement and its <c>Next</c> run by the interpreter for compiled code, as a support call is, with the state the
/// frame holds of the loop. A procedure whose loop is a <c>For Each</c> is compiled code's, and nothing of the loop is stepped. And a call given an object
/// inside a condition, and a member called on a record's object field, made by compiled code: what the program does is what it does in the interpreter.
/// </summary>
[TestClass]
public sealed class CompiledForEachTests
{
    // a class whose objects say when they go, and compare by their Id.
    private static readonly (string Name, string Source)[] Boxes =
    [
        ("Box", ModuleWorkspace.ClassModule("Box",
            "Option Explicit",
            "Public Id As Long",
            "Public Function Hit(ByVal other As Box) As Boolean",
            "    Hit = other.Id = Id",
            "End Function",
            "Private Sub Class_Terminate()",
            "    If Id > 0 Then Debug.Print \"gone \" & Id",
            "End Sub")),
        ("Fleet", ModuleWorkspace.ClassModule("Fleet",
            "Option Explicit",
            "Private Type TState",
            "    Ships As Collection",
            "    Index As Long",
            "End Type",
            "Private this As TState",
            "Private Sub Class_Initialize()",
            "    Set this.Ships = New Collection",
            "End Sub",
            "Public Sub Add(ByVal Ship As Box)",
            "    this.Ships.Add Ship",
            "End Sub",
            "Public Property Get Count() As Long",
            "    Count = this.Ships.Count",
            "End Property",
            "Public Function Counted() As Long",
            "    Counted = this.Ships.Count",
            "End Function",
            "Public Function Fewer(ByVal Most As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To Most",
            "        If this.Ships.Count < I Then Fewer = Fewer + 1",
            "    Next",
            "End Function")),
    ];

    private static readonly string[] Made =
    [
        "Private Function Made(ByVal Count As Long) As Collection",
        "    Dim C As New Collection, I As Long, B As Box",
        "    For I = 1 To Count",
        "        Set B = New Box",
        "        B.Id = I",
        "        C.Add B",
        "    Next",
        "    Set Made = C",
        "End Function",
    ];

    private static string WithMade(params string[] lines) => Program([.. lines, .. Made]);

    private static void AssertNothingStepped(CompilationResult compiled)
        => Assert.IsEmpty(compiled.Stepped, string.Join("; ", compiled.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));

    private static void AssertForEachNotStepped(CompilationResult compiled)
        => Assert.IsFalse(compiled.Stepped.Any(reason => reason.Detail is nameof(InstructionKind.ForEachOpener) or nameof(InstructionKind.ForEachNext)),
            string.Join("; ", compiled.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));

    // a procedure that does nothing but walk a collection into another is compiled code's: its loop is its For Each, of which nothing is stepped.
    [TestMethod]
    public async Task AForEachThatCopiesACollection_IsCompiledCodes_AndNothingOfItIsStepped()
        => AssertNothingStepped(await AssertAlikeAsync(WithMade(
            "Public Sub Main()",
            "    Debug.Print Copied(Made(3)).Count",
            "End Sub",
            "Private Function Copied(ByVal Source As Collection) As Collection",
            "    Dim Result As Collection",
            "    Set Result = New Collection",
            "    Dim Each1 As Box",
            "    For Each Each1 In Source",
            "        Result.Add Each1",
            "    Next",
            "    Set Copied = Result",
            "End Function"), "Copied", recoveries: 0, classes: Boxes));

    // an array of Variants: each element is assigned the control variable, and the body adds it up in compiled code.
    [TestMethod]
    public async Task AForEachOverAnArray_AssignsEachElement_AndTheBodyRunsCompiled()
        => AssertNothingStepped(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim A(1 To 4) As Variant, V As Variant, I As Long",
            "    For I = 1 To 4",
            "        A(I) = I * 10",
            "    Next",
            "    For Each V In A",
            "        Summed = Summed + V",
            "    Next",
            "    Summed = Summed + I",
            "End Function"), "Summed", recoveries: 0, expected: "105"));

    // MS-VBAL §5.4.2.4: a collection with no member skips the body, and the control variable is emptied (RD-VBAL §5.4.2.4), as when a loop runs out:
    // Empty adds nothing where 7 would add 7. The addition after the loop is the interpreter's, once per call: compiled code holds no Empty Variant.
    [TestMethod]
    public async Task AForEachOverAnEmptyCollection_SkipsItsBody_AndEmptiesItsControlVariable()
        => AssertNothingStepped(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New Collection",
            "    Debug.Print Walked(C)",
            "    C.Add 1",
            "    C.Add 2",
            "    Debug.Print Walked(C)",
            "End Sub",
            "Private Function Walked(ByVal Source As Collection) As Long",
            "    Dim V As Variant",
            "    V = 7",
            "    For Each V In Source",
            "        Walked = Walked + V",
            "    Next",
            "    Walked = Walked + V + 100",
            "End Function"), "Walked", recoveries: 2, expected: "100"));

    // a For Each of Nothing raises error 91 at its statement, which the procedure's handler is given, as the interpreter gives it.
    [TestMethod]
    public async Task AForEachOfNothing_RaisesError91_ToTheProceduresHandler()
        => AssertForEachNotStepped(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Caught()",
            "End Sub",
            "Private Function Caught() As Long",
            "    Dim C As Collection, V As Variant",
            "    On Error GoTo Handler",
            "    For Each V In C",
            "        Caught = Caught + 10",
            "    Next",
            "    Exit Function",
            "Handler:",
            "    Caught = Err.Number",
            "End Function"), "Caught", recoveries: 2, expected: "91"));

    // with no handler, the error of the For Each statement goes to the caller's handler, after what compiled code did before it.
    [TestMethod]
    public async Task AForEachOfAScalar_RaisesTypeMismatch_ToTheCaller()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Debug.Print Walked(3)",
            "    Debug.Print Err.Number",
            "End Sub",
            "Private Function Walked(ByVal Count As Long) As Long",
            "    Dim V As Variant, S As Variant, I As Long",
            "    For I = 1 To Count",
            "        Walked = Walked + I",
            "    Next",
            "    S = Walked",
            "    For Each V In S",
            "        Walked = Walked + 1",
            "    Next",
            "End Function"), "Walked", recoveries: 0, walkBacks: 1, expected: "13");

    // the objects the collection held go when the loop is over and nothing else holds them: at the Next that ends it, before the call compiled code makes next.
    [TestMethod]
    public async Task WhatAForEachHeld_IsLetGoOfAtTheNextThatEndsIt()
        => await AssertAlikeAsync(WithMade(
            "Public Sub Main()",
            "    Debug.Print Ended()",
            "End Sub",
            "Private Sub Report(ByVal N As Long)",
            "    Debug.Print \"at \" & N",
            "End Sub",
            "Private Function Ended() As Long",
            "    Dim B As Box",
            "    For Each B In Made(2)",
            "        If B.Hit(B) Then Ended = Ended + 1",
            "    Next",
            "    Report Ended",
            "End Function"), "Ended", recoveries: 0, classes: Boxes);

    // a For Each in a With block is evaluated against the block's object, which the interpreter steps; its Next is compiled code's.
    [TestMethod]
    public async Task AForEachInAWithBlock_IsSteppedByTheInterpreter_AndItsNextIsCompiledCodes()
    {
        var compiled = await AssertAlikeAsync(WithMade(
            "Public Sub Main()",
            "    Debug.Print Walked()",
            "End Sub",
            "Private Function Walked() As Long",
            "    Dim B As Box",
            "    With Made(3)",
            "        For Each B In Made(.Count)",
            "            If B.Hit(B) Then Walked = Walked + 1",
            "        Next",
            "    End With",
            "End Function"), "Walked", recoveries: 3, classes: Boxes);

        Assert.IsTrue(compiled.Stepped.Any(reason => reason is { Kind: IneligibilityKind.Instruction, Detail: nameof(InstructionKind.ForEachOpener) }),
            string.Join("; ", compiled.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
        Assert.IsFalse(compiled.Stepped.Any(reason => reason is { Detail: nameof(InstructionKind.ForEachNext) }),
            string.Join("; ", compiled.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }

    // a member of an object called in an If condition, given an object: compiled code makes the call, and the loop around it is its own.
    [TestMethod]
    public async Task ACallGivenAnObject_InAnIfCondition_IsMadeByCompiledCode()
        => AssertNothingStepped(await AssertAlikeAsync(WithMade(
            "Public Sub Main()",
            "    Debug.Print Found(Made(4))",
            "End Sub",
            "Private Function Found(ByVal Source As Collection) As Long",
            "    Dim B As Box, Target As Box",
            "    Set Target = Source(3)",
            "    For Each B In Source",
            "        If B.Hit(Target) Then",
            "            Found = Found + 10",
            "            Exit Function",
            "        End If",
            "        Found = Found + 1",
            "    Next",
            "End Function"), "Found", recoveries: 0, classes: Boxes, expected: "12"));

    // and in the condition of a Loop Until, after a comparison: the call is made each pass, until the condition holds.
    [TestMethod]
    public async Task ACallGivenAnObject_InALoopUntilCondition_IsMadeByCompiledCode()
        => AssertNothingStepped(await AssertAlikeAsync(WithMade(
            "Public Sub Main()",
            "    Debug.Print Passes(Made(4))",
            "End Sub",
            "Private Function Passes(ByVal Source As Collection) As Long",
            "    Dim B As Box, Target As Box",
            "    Set B = Source(1)",
            "    Set Target = Source(4)",
            "    Do",
            "        Passes = Passes + 1",
            "    Loop Until Passes > 5 Or B.Hit(Target)",
            "End Function"), "Passes", recoveries: 0, classes: Boxes, expected: "6"));

    // an object given to a call compiled code cannot give (one not of the parameter's class) is the interpreter's to give, after the call left of it in the
    // condition, which is made once: the interpreter is handed the instruction with that call's value, and raises the error at the call it makes.
    [TestMethod]
    public async Task AnObjectTheInterpreterGives_AfterACallInTheSameCondition_DoesNotMakeThatCallAgain()
        => AssertNothingStepped(await AssertAlikeAsync(WithMade(
            "Private Calls As Long",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Debug.Print Mismatched(Made(2))",
            "    Debug.Print Err.Number; Calls",
            "End Sub",
            "Private Function Counted(ByVal N As Long) As Long",
            "    Calls = Calls + 1",
            "    Counted = N",
            "End Function",
            "Private Function Mismatched(ByVal Source As Collection) As Long",
            "    Dim B As Box, Other As Object",
            "    Set B = Source(1)",
            "    Set Other = Source",
            "    If Counted(1) = 1 And B.Hit(Other) Then Mismatched = 1",
            "End Function"), "Mismatched", recoveries: 0, classes: Boxes, expected: "13  1"));

    // a member called on a record's object field (its Count) is a call compiled code makes, assigned or compared in a loop's condition.
    [TestMethod]
    public async Task AMemberOfARecordsObjectField_IsCalledByCompiledCode()
    {
        var program = WithMade(
            "Public Sub Main()",
            "    Dim F As New Fleet, B As Box",
            "    For Each B In Made(2)",
            "        F.Add B",
            "    Next",
            "    Debug.Print F.Counted(); F.Fewer(4)",
            "End Sub");

        AssertNothingStepped(await AssertAlikeAsync(program, "Counted", recoveries: 0, classes: Boxes, expected: "2  2"));
        AssertNothingStepped(await AssertAlikeAsync(program, "Fewer", recoveries: 0, classes: Boxes));
    }
}
