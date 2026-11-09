using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The blocks whose state the frame holds, stepped by the interpreter inside compiled code (IR lowering S2.5b): a <c>With</c> and its <c>End With</c>, a
/// <c>For Each</c> and its <c>Next</c>, and a <c>Select Case</c> or a <c>Case</c> compiled code does not compare. What a block holds is let go of where
/// the interpreter lets go of it, so a <c>Class_Terminate</c> runs at the same place either way.
/// </summary>
[TestClass]
public sealed class CompiledBlockTests
{
    // an object whose Class_Terminate says so, and a procedure that says where the program is: called by compiled code, it runs before anything the caller
    // steps would let go of what a block held.
    private static readonly (string Name, string Source)[] Noisy =
        [("Noisy", ModuleWorkspace.ClassModule("Noisy", "Public Id As Long", "Private Sub Class_Terminate()", "    Debug.Print \"gone \" & Id", "End Sub"))];

    private static readonly string[] Helpers =
    [
        "Private Sub Report(ByVal N As Long)",
        "    Debug.Print \"at \" & N",
        "End Sub",
        "Private Function Made() As Variant",
        "    Dim A(1 To 2) As Variant",
        "    Set A(1) = New Noisy",
        "    A(1).Id = 1",
        "    Set A(2) = New Noisy",
        "    A(2).Id = 2",
        "    Made = A",
        "End Function",
    ];

    private static string WithHelpers(params string[] lines) => Program([.. lines, .. Helpers]);

    // the With and its End With, and the statements that use its object, are the interpreter's; the For loop inside the block is compiled code's.
    [TestMethod]
    public async Task AWith_IsSteppedByTheInterpreter_AroundACompiledLoop()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Filled()",
            "End Sub",
            "Private Function Filled() As Long",
            "    Dim C As Collection, I As Long",
            "    Set C = New Collection",
            "    With C",
            "        For I = 1 To 3",
            "            .Add I",
            "            Filled = Filled + I",
            "        Next",
            "        Filled = Filled + .Count",
            "    End With",
            "End Function"), "Filled", recoveries: 6);

        Assert.IsTrue(compiled.Stepped.Any(reason => reason is { Kind: IneligibilityKind.Instruction, Detail: nameof(RDCore.Runtime.LanguageCore.Instructions.InstructionKind.With) }),
            string.Join("; ", compiled.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }

    // MS-VBAL §5.4.2.21: End With lets go of the block's object, whose Class_Terminate runs there, before what compiled code calls next.
    [TestMethod]
    public async Task TheObjectOfAWith_IsLetGoOfAtItsEndWith()
        => await AssertAlikeAsync(WithHelpers(
            "Public Sub Main()",
            "    Debug.Print Held()",
            "End Sub",
            "Private Function Held() As Long",
            "    Dim I As Long",
            "    With New Noisy",
            "        .Id = 7",
            "        For I = 1 To 2",
            "            Held = Held + I",
            "        Next",
            "    End With",
            "    Report Held",
            "End Function"), "Held", recoveries: 2, classes: Noisy);

    // the element a For Each assigns V each pass is in V's cell, which compiled code reads: the statements of the body are its own. The Add of the
    // Collection is a support call.
    [TestMethod]
    public async Task AForEach_IsSteppedByTheInterpreter_AroundACompiledBody()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim C As Collection, V As Variant, I As Long",
            "    Set C = New Collection",
            "    For I = 1 To 4",
            "        C.Add I * 10",
            "    Next",
            "    For Each V In C",
            "        Summed = Summed + V",
            "        I = I + 1",
            "    Next",
            "    Summed = Summed + I",
            "End Function"), "Summed", recoveries: 5);

    // the array a For Each reads is let go of when the loop ends, at its Next: the element V does not hold is gone before the call compiled code makes next.
    [TestMethod]
    public async Task AForEachThatEnds_LetsGoOfWhatItRead_BeforeCompiledCodeGoesOn()
        => await AssertAlikeAsync(WithHelpers(
            "Public Sub Main()",
            "    Debug.Print Ended()",
            "End Sub",
            "Private Function Ended() As Long",
            "    Dim V As Variant, I As Long",
            "    For Each V In Made()",
            "        I = I + 1",
            "    Next",
            "    Report I",
            "    Ended = I",
            "End Function"), "Ended", recoveries: 3, classes: Noisy);

    // an Exit For is a jump of compiled code out of the block: where it lands, what the block held is let go of before the call compiled code makes there.
    [TestMethod]
    public async Task AnExitForOutOfAForEach_LetsGoOfWhatItRead_WhereItLands()
        => await AssertAlikeAsync(WithHelpers(
            "Public Sub Main()",
            "    Debug.Print Left1()",
            "End Sub",
            "Private Function Left1() As Long",
            "    Dim V As Variant, I As Long",
            "    For Each V In Made()",
            "        I = I + 1",
            "        If I = 1 Then Exit For",
            "    Next",
            "    Report I",
            "    Left1 = I",
            "End Function"), "Left1", recoveries: 1, classes: Noisy);

    // a Return from inside a For Each to a GoSub outside it leaves the block too.
    [TestMethod]
    public async Task AReturnOutOfAForEach_LetsGoOfWhatItRead_WhereItReturns()
        => await AssertAlikeAsync(WithHelpers(
            "Public Sub Main()",
            "    Debug.Print Back()",
            "End Sub",
            "Private Function Back() As Long",
            "    Dim V As Variant",
            "    GoSub Walk",
            "    Report Back",
            "    Exit Function",
            "Walk:",
            "    For Each V In Made()",
            "        Back = Back + 1",
            "        If Back = 1 Then Return",
            "    Next",
            "    Return",
            "End Function"), "Back", recoveries: 1, classes: Noisy);

    // a GoTo out of the block is a jump of compiled code too: the label it lands on lets go of what the block held, before the call compiled code makes there.
    [TestMethod]
    public async Task AGoToOutOfAForEach_LetsGoOfWhatItRead_WhereItLands()
        => await AssertAlikeAsync(WithHelpers(
            "Public Sub Main()",
            "    Debug.Print Jumped()",
            "End Sub",
            "Private Function Jumped() As Long",
            "    Dim V As Variant, I As Long",
            "    For Each V In Made()",
            "        I = I + 1",
            "        If I = 1 Then GoTo Out",
            "    Next",
            "Out:",
            "    Report I",
            "    Jumped = I",
            "End Function"), "Jumped", recoveries: 2, classes: Noisy);

    // an error in the loop goes to a handler outside it: the interpreter runs the instruction that fails, and what the block held is let go of before
    // compiled code goes on in the handler.
    [TestMethod]
    public async Task AnErrorToAHandlerOutsideAForEach_LetsGoOfWhatItRead_BeforeTheHandler()
        => await AssertAlikeAsync(WithHelpers(
            "Public Sub Main()",
            "    Debug.Print Caught()",
            "End Sub",
            "Private Function Caught() As Long",
            "    Dim V As Variant, I As Long",
            "    On Error GoTo Handler",
            "    For Each V In Made()",
            "        I = I + 1",
            "        If I = 1 Then I = I \\ 0",
            "    Next",
            "    Exit Function",
            "Handler:",
            "    Report I",
            "    Caught = I",
            "End Function"), "Caught", recoveries: 2, classes: Noisy);

    // an End in the loop ends the program there, in either engine, with what the block held.
    [TestMethod]
    public async Task AnEndInAForEach_EndsTheProgramThere()
        => await AssertAlikeAsync(WithHelpers(
            "Public Sub Main()",
            "    Ended",
            "    Debug.Print \"not reached\"",
            "End Sub",
            "Private Sub Ended()",
            "    Dim V As Variant, I As Long",
            "    For Each V In Made()",
            "        I = I + 1",
            "        Report I",
            "        If I = 1 Then End",
            "    Next",
            "End Sub"), "Ended", recoveries: 1, classes: Noisy);

    // the target of an inner With is evaluated against the outer one's object; each End With lets go of its own, the Collection's two objects with the outer.
    [TestMethod]
    public async Task AnInnerWith_IsEvaluatedAgainstTheOuter_AndEachEndWithLetsGoOfItsOwn()
        => await AssertAlikeAsync(WithHelpers(
            "Public Sub Main()",
            "    Debug.Print Nested()",
            "End Sub",
            "Private Function Nested() As Long",
            "    Dim I As Long",
            "    With New Collection",
            "        .Add New Noisy",
            "        .Add New Noisy",
            "        With .Item(2)",
            "            .Id = 5",
            "            For I = 1 To 2",
            "                Nested = Nested + I",
            "            Next",
            "            Nested = Nested + .Id",
            "        End With",
            "        Report Nested",
            "    End With",
            "    Report Nested + 1",
            "End Function"), "Nested", recoveries: 8, classes: Noisy);

    // an Exit For out of a With inside the loop leaves the With without its End With: compiled code lets its object go where the interpreter does.
    [TestMethod]
    public async Task AnExitForOutOfAWith_LetsGoOfItsObject_WhereTheInterpreterDoes()
        => await AssertAlikeAsync(WithHelpers(
            "Public Sub Main()",
            "    Debug.Print Quit()",
            "End Sub",
            "Private Function Quit() As Long",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        With New Noisy",
            "            .Id = I",
            "            Quit = Quit + I",
            "            If I = 2 Then Exit For",
            "        End With",
            "    Next",
            "    Report Quit",
            "End Function"), "Quit", recoveries: 3, classes: Noisy);

    // a Select Case of a String selector, and its Cases, are the interpreter's; the loop around it is compiled code's.
    [TestMethod]
    public async Task ASelectOfAString_IsSteppedByTheInterpreter()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Named()",
            "End Sub",
            "Private Function Named() As Long",
            "    Dim S As String, I As Long",
            "    For I = 1 To 3",
            "        S = Chr$(96 + I)",
            "        Select Case S",
            "            Case \"a\"",
            "                Named = Named + 1",
            "            Case \"b\", \"c\"",
            "                Named = Named + 10",
            "        End Select",
            "    Next",
            "End Function"), "Named", recoveries: 8);

    // a Case of a value that is no scalar is compared by the interpreter, with the selector compiled code gave the frame; the other Cases are compiled code's.
    [TestMethod]
    public async Task ACaseOfAString_IsSteppedByTheInterpreter_WithTheSelector()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Mixed()",
            "End Sub",
            "Private Function Mixed() As Long",
            "    Dim I As Long",
            "    For I = 1 To 4",
            "        Select Case I",
            "            Case 1",
            "                Mixed = Mixed + 1",
            "            Case \"2\"",
            "                Mixed = Mixed + 10",
            "            Case 3",
            "                Mixed = Mixed + 100",
            "        End Select",
            "    Next",
            "End Function"), "Mixed", recoveries: 3);

    // a For Each the interpreter would run whole - every instruction of it stepped - keeps the procedure in the interpreter.
    [TestMethod]
    public async Task AForEachTheInterpreterWouldRunWhole_KeepsItsProcedureInterpreted()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Listed()",
            "End Sub",
            "Private Function Listed() As Long",
            "    Dim C As Collection, V As Variant",
            "    Set C = New Collection",
            "    C.Add 1",
            "    C.Add 2",
            "    For Each V In C",
            "        Debug.Print V",
            "    Next",
            "    Listed = 3",
            "End Function"));

        Assert.IsNull(compiled["Listed"].Body);
        Assert.IsTrue(compiled["Listed"].Reasons.Any(reason => reason is { Kind: IneligibilityKind.Instruction, Detail: nameof(RDCore.Runtime.LanguageCore.Instructions.InstructionKind.ForEachNext) }),
            string.Join("; ", compiled["Listed"].Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }
}
