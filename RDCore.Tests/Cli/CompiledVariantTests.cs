using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> parameter or variable, compiled (IR lowering S2.5c): held in the cell its frame holds it in, read and written there by compiled code,
/// and each operation of it planned for the subtypes of its operands, as the interpreter's evaluation on payloads plans it (MS-VBAL §5.6.9). What that
/// evaluation declines - a Variant holding a <c>String</c>, an object, a sum that overflows its subtype - the interpreter runs alone. A program runs the same
/// either way.
/// </summary>
[TestClass]
public sealed class CompiledVariantTests
{
    private static readonly (string Name, string Source)[] Noisy =
        [("Noisy", ModuleWorkspace.ClassModule("Noisy", "Private Sub Class_Terminate()", "    Debug.Print \"gone\"", "End Sub"))];

    // the For statement of a Variant counter is the interpreter's, which decides the type the loop counts in (ForLoopEvaluator.Operands); each Next,
    // compiled code's.
    [TestMethod]
    public async Task AVariantSum_IsComputedInCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Summed() As Double",
            "    Dim I As Variant",
            "    Dim S As Variant",
            "    S = 0#",
            "    For I = 1 To 100",
            "        S = S + I",
            "    Next",
            "    Summed = S",
            "End Function"), "Summed", recoveries: 1);

    // typed operands and a Variant in one expression: the typed product computed as it always is - in the extended format - and the sum planned for the
    // Variant's subtype.
    [TestMethod]
    public async Task ATypedOperand_IsAddedToAVariant_AsTheInterpreterAddsIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Mixed()",
            "End Sub",
            "Private Function Mixed() As Double",
            "    Dim I As Long",
            "    Dim D As Double",
            "    Dim V As Variant",
            "    V = 0",
            "    D = 0.1",
            "    For I = 1 To 50",
            "        V = V + I * D",
            "    Next",
            "    Mixed = V",
            "End Function"), "Mixed", recoveries: 0);

    // a Variant holds the type of what it is assigned (MS-VBAL §5.5.1.2.12): an Integer sum that overflows is the interpreter's, which widens it to a Long, and
    // compiled code goes on adding Longs.
    [TestMethod]
    public async Task AnIntegerSumThatOverflows_IsWidenedByTheInterpreter_AndCompiledCodeGoesOn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Widened()",
            "End Sub",
            "Private Function Widened() As String",
            "    Dim K As Long",
            "    Dim V As Variant",
            "    V = 32000",
            "    For K = 1 To 10",
            "        V = V + 100",
            "    Next",
            "    Widened = V & \" \" & TypeName(V)",
            "End Function"), "Widened", recoveries: 2);

    // what the evaluation on payloads does not do of a Variant - one holding a String - the interpreter does, alone.
    [TestMethod]
    public async Task AVariantHoldingAString_IsAddedToByTheInterpreter()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Parsed()",
            "End Sub",
            "Private Function Parsed() As Double",
            "    Dim V As Variant",
            "    V = \"12\"",
            "    V = V + 1",
            "    V = V * 2",
            "    Parsed = V",
            "End Function"), "Parsed", recoveries: 2);

    // a Let of a Variant that holds an object is the interpreter's, which lets go of the object there.
    [TestMethod]
    public async Task ALetOverAnObject_LetsGoOfIt_WhereTheInterpreterDoes()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Replaced()",
            "End Sub",
            "Private Function Replaced() As Long",
            "    Dim V As Variant",
            "    Set V = New Noisy",
            "    Debug.Print \"held\"",
            "    V = 5",
            "    Debug.Print \"replaced\"",
            "    V = V + 1",
            "    Replaced = V",
            "End Function"), "Replaced", recoveries: 4, classes: Noisy);

    [TestMethod]
    public async Task AVariantCondition_IsWhetherItsPayloadIsNotZero()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted(0); Counted(3)",
            "End Sub",
            "Private Function Counted(ByVal N As Long) As Long",
            "    Dim V As Variant",
            "    V = N",
            "    If V Then Counted = 100",
            "    Do While V < 10",
            "        V = V + 2.5",
            "        Counted = Counted + 1",
            "    Loop",
            "End Function"), "Counted", recoveries: 0);

    [TestMethod]
    public async Task ANegationAndANot_ArePlannedForTheSubtype()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Flipped()",
            "End Sub",
            "Private Function Flipped() As Long",
            "    Dim V As Variant",
            "    Dim B As Variant",
            "    V = 7",
            "    V = -V",
            "    B = True",
            "    B = Not B",
            "    V = V * 3 \\ 2 Mod 5",
            "    If B = False Then Flipped = V",
            "End Function"), "Flipped", recoveries: 0);

    // a Variant let-assigned to a scalar variable is converted to its type as any payload is: exactly, or by the interpreter, which rounds.
    [TestMethod]
    public async Task AVariantAssignedToALong_ThatIsNoInteger_IsRoundedByTheInterpreter()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rounded()",
            "End Sub",
            "Private Function Rounded() As Long",
            "    Dim V As Variant",
            "    Dim L As Long",
            "    V = 6",
            "    L = V",
            "    V = 2.5",
            "    L = L + V",
            "    Rounded = L",
            "End Function"), "Rounded", recoveries: 0, walkBacks: 1);

    // a Variant parameter is held in the cell its caller gave it: by reference, its caller's variable.
    [TestMethod]
    public async Task AVariantParameter_IsReadAndWrittenInItsCell()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim X As Variant",
            "    X = 1",
            "    Bumped X, 2",
            "    Bumped X, 2.5",
            "    Debug.Print X; TypeName(X)",
            "End Sub",
            "Private Sub Bumped(ByRef V As Variant, ByVal By As Variant)",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        V = V + By",
            "    Next",
            "End Sub"), "Bumped", recoveries: 0);

    // a Variant parameter passed a variable of another type by reference reads and writes it as its type (MS-VBAL §5.3.1.11): no Variant's cell, and the
    // activation is the interpreter's from the start.
    [TestMethod]
    public async Task AVariantParameterPassedALong_IsTheInterpreters()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim X As Long",
            "    X = 1",
            "    Bumped X, 2",
            "    Debug.Print X; TypeName(X)",
            "End Sub",
            "Private Sub Bumped(ByRef V As Variant, ByVal By As Variant)",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        V = V + By",
            "    Next",
            "End Sub"), "Bumped", recoveries: 0, walkBacks: 1);

    // a Variant counter that overflows the type it counts in is stepped by the interpreter, which widens it.
    [TestMethod]
    public async Task AVariantCounterThatOverflows_IsSteppedByTheInterpreter()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Stepped()",
            "End Sub",
            "Private Function Stepped() As String",
            "    Dim I As Variant",
            "    Dim N As Long",
            "    For I = 32760 To 32767",
            "        N = N + 1",
            "    Next",
            "    Stepped = N & \" \" & I & \" \" & TypeName(I)",
            "End Function"), "Stepped", recoveries: 3);

    // a division by zero of Variants is the interpreter's to raise: in a body with an error handler, the handler is given it, and compiled code goes on after.
    [TestMethod]
    public async Task AVariantDivisionByZero_IsRaisedByTheInterpreter_ToTheHandler()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Guarded()",
            "End Sub",
            "Private Function Guarded() As Double",
            "    On Error Resume Next",
            "    Dim I As Long",
            "    Dim V As Variant",
            "    For I = -2 To 2",
            "        V = 12 / I",
            "        Guarded = Guarded + V",
            "    Next",
            "End Function"), "Guarded", recoveries: 1);

    // what a Variant holds, read from its frame by the interpreter at an instruction it runs alone, is what compiled code wrote.
    [TestMethod]
    public async Task AStatementTheInterpreterRuns_ReadsWhatCompiledCodeWroteIntoTheVariant()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Shown",
            "End Sub",
            "Private Sub Shown()",
            "    Dim V As Variant",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        V = I * 1.5",
            "        Debug.Print V; TypeName(V)",
            "    Next",
            "End Sub"), "Shown", recoveries: 3);

    [TestMethod]
    public async Task AVariantForStatement_IsReportedAsStepped()
    {
        var run = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim I As Variant",
            "    For I = 1 To 3",
            "        Summed = Summed + I",
            "    Next",
            "End Function"), "Summed", recoveries: 1);

        Assert.AreEqual("ForCounter I", string.Join("; ", run.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }
}
