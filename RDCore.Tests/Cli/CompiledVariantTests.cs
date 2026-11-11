using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> parameter or variable, compiled: held in the cell its frame holds it in, read and written there by compiled code,
/// and each operation of it planned for the subtypes of its operands, as the interpreter's evaluation on payloads plans it (MS-VBAL §5.6.9). What that
/// evaluation declines (a Variant holding a <c>String</c>, an object, a sum that overflows its subtype) the interpreter runs alone. A program runs the same
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
            "End Function"), "Replaced", recoveries: 1, classes: Noisy);

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

    // a Variant let-assigned to a scalar variable is converted to its type as any payload is: an integral one by compiled code, a floating-point one to an
    // integral type by the interpreter, which rounds, running that statement alone - compiled code goes on after it. L = V converts the Integer 6;
    // L + V is the Double 8.5, which the interpreter converts: 1.
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
            "End Function"), "Rounded", recoveries: 1);

    // a loop condition reading a Variant that is Empty, which compiled code does not compare: the interpreter runs the Do statement alone, and the loop
    // goes on compiled once the Variant holds a number. Empty is read twice: by the first test of the condition, and by the first S + 3: 2.
    [TestMethod]
    public async Task ALoopConditionOnAVariantThatIsEmpty_IsTheInterpretersOnce_AndTheLoopGoesOnCompiled()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Climbed()",
            "End Sub",
            "Private Function Climbed() As Long",
            "    Dim S As Variant",
            "    Do While S < 10",
            "        S = S + 3",
            "        Climbed = Climbed * 10 + S",
            "    Loop",
            "End Function"), "Climbed", recoveries: 2);

    // a condition reading a Variant that holds a String, which the interpreter compares as a number: each If is the interpreter's alone, and the loop it
    // is in goes on compiled. The If of each of the 3 passes of Judged("5") is the interpreter's; Judged(2) compares an Integer, compiled: 3.
    [TestMethod]
    public async Task AConditionOnAVariantHoldingAString_IsTheInterpreters_StatementByStatement()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Judged(\"5\"); Judged(2)",
            "End Sub",
            "Private Function Judged(ByVal V As Variant) As Long",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        If V > 3 Then Judged = Judged * 10 + I Else Judged = Judged * 10 - I",
            "    Next",
            "End Function"), "Judged", recoveries: 3);

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

    // a Variant parameter passed a variable of another type by reference reads and writes it as its type (MS-VBAL §5.3.1.11): it is in no Variant's cell,
    // and each statement that reads or writes it is the interpreter's.
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
            "End Sub"), "Bumped", recoveries: 3);

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

    private static readonly (string Name, string Source)[] Valued =
        [("Valued", ModuleWorkspace.ClassModule("Valued", "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Value = 100", "End Property"))];

    // the program run both ways, and the procedure named compiled: a Variant speculated to hold a subtype it does not hold is evaluated as any is - slower,
    // never another result.
    private static async Task AssertSpeculatedAsync(string program, string compiled, IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        var results = await AssertAlikeAsync(program, classes);
        Assert.IsTrue(results.TryGetValue(compiled, out var result), $"{compiled} was not called");
        Assert.IsNotNull(result.Body, $"{compiled}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
    }

    // S is speculated to hold the Double it is assigned after its first loop: in that loop it is Empty, and then the Long that Empty + Long is.
    [TestMethod]
    public async Task AVariantThatIsEmpty_IsNotTheSubtypeItIsSpeculatedToHold()
        => await AssertSpeculatedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print FromEmpty()",
            "    Debug.Print NeverAssigned()",
            "End Sub",
            "Private Function FromEmpty() As String",
            "    Dim I As Long",
            "    Dim S As Variant",
            "    For I = 1 To 4",
            "        S = S + I",
            "        FromEmpty = FromEmpty & TypeName(S) & \" \"",
            "    Next",
            "    S = 0#",
            "    For I = 1 To 3",
            "        S = S + I",
            "    Next",
            "    FromEmpty = FromEmpty & S & \" \" & TypeName(S)",
            "End Function",
            "Private Function NeverAssigned() As String",
            "    Dim I As Integer",
            "    Dim S As Variant",
            "    For I = 1 To 3",
            "        S = S + I",
            "    Next",
            "    NeverAssigned = S & \" \" & TypeName(S)",
            "End Function"), "FromEmpty");

    // a Variant speculated to hold a Long is assigned a String, Null, or an object partway through a loop: what it then holds is evaluated as any is.
    [TestMethod]
    public async Task AVariantWhoseSubtypeChangesInALoop_IsEvaluatedAsWhatItHolds()
        => await AssertSpeculatedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Changing(1)",
            "    Debug.Print Changing(2)",
            "    Debug.Print Changing(3)",
            "End Sub",
            "Private Function Changing(ByVal Becomes As Long) As String",
            "    Dim I As Long",
            "    Dim S As Variant",
            "    S = 0",
            "    For I = 1 To 6",
            "        If I = 3 Then",
            "            If Becomes = 1 Then S = \"10\"",
            "            If Becomes = 2 Then S = Null",
            "            If Becomes = 3 Then Set S = New Valued",
            "        End If",
            "        S = S + I",
            "        Changing = Changing & TypeName(S) & \" \"",
            "    Next",
            "    Changing = Changing & S",
            "End Function"), "Changing", Valued);

    // a sum that overflows the subtype it is done in is the interpreter's, which widens it (MS-VBAL §5.6.9.3; Single to Double, as MS-VBA does): a Variant
    // speculated to hold the narrower subtype holds the wider from then on, and is evaluated as it is - a difference back in the narrower range is the wider.
    [TestMethod]
    public async Task AVariantSumThatOverflows_IsWidened_NeverWrapped()
        => await AssertSpeculatedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Widened()",
            "End Sub",
            "Private Function Widened() As String",
            "    Dim K As Long",
            "    Dim B As Byte",
            "    Dim V As Variant",
            "    Dim W As Variant",
            "    Dim X As Variant",
            "    Dim Y As Variant",
            "    B = 2",
            "    V = 32760",
            "    W = 2147483640",
            "    X = B",
            "    Y = CSng(1E+38)",
            "    For K = 1 To 8",
            "        V = V + 3",
            "        W = W + 3",
            "        X = X * B",
            "        Y = Y * 2",
            "    Next",
            "    V = V - 100",
            "    W = W - 2147483000",
            "    X = X - B",
            "    Widened = V & \" \" & TypeName(V) & \" \" & W & \" \" & TypeName(W) & \" \" & X & \" \" & TypeName(X) & \" \" & TypeName(Y)",
            "End Function"), "Widened");

    // S + Null is Null (MS-VBAL §5.6.9.3), and a Variant speculated to hold a Long that holds Null is no Long.
    [TestMethod]
    public async Task AVariantThatHoldsNull_PropagatesIt()
        => await AssertSpeculatedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Propagated()",
            "End Sub",
            "Private Function Propagated() As String",
            "    Dim I As Long",
            "    Dim S As Variant",
            "    Dim T As Variant",
            "    S = 0",
            "    For I = 1 To 5",
            "        If I = 3 Then S = Null",
            "        T = S + I",
            "        Propagated = Propagated & TypeName(T) & \" \"",
            "        If I = 3 Then S = I",
            "    Next",
            "    T = T + Null",
            "    Propagated = Propagated & IsNull(T)",
            "End Function"), "Propagated");

    // + of two Strings concatenates them, and + of a String and a number adds the number the String is (MS-VBAL §5.6.9.3.1).
    [TestMethod]
    public async Task APlusOfStrings_ConcatenatesThem_AndOfAStringAndANumber_AddsThem()
        => await AssertSpeculatedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Joined()",
            "End Sub",
            "Private Function Joined() As String",
            "    Dim I As Long",
            "    Dim S As Variant",
            "    Dim N As Variant",
            "    S = \"a\"",
            "    N = 0",
            "    For I = 1 To 4",
            "        S = S + \"b\"",
            "        N = N + I",
            "        If I = 2 Then N = \"5\"",
            "    Next",
            "    Joined = S & \" \" & N & \" \" & TypeName(N)",
            "End Function"), "Joined");

    // a Currency is added and multiplied as one (a product that is no whole number of ten-thousandths, the interpreter's to round); a Date is no subtype
    // compiled code speculates a Variant holds.
    [TestMethod]
    public async Task AVariantHoldingACurrencyOrADate_IsOperatedOnAsOne()
        => await AssertSpeculatedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Typed()",
            "End Sub",
            "Private Function Typed() As String",
            "    Dim I As Long",
            "    Dim C As Currency",
            "    Dim D As Date",
            "    Dim V As Variant",
            "    Dim W As Variant",
            "    C = 1.25",
            "    D = #1/1/2020#",
            "    V = C",
            "    W = 0",
            "    For I = 1 To 4",
            "        V = V + C",
            "        V = V * C",
            "        If I = 2 Then W = D",
            "        W = W + I",
            "    Next",
            "    Typed = V & \" \" & TypeName(V) & \" \" & Format(W, \"yyyy-mm-dd\") & \" \" & TypeName(W)",
            "End Function"), "Typed");

    // a Variant counter counting down, by a step whose variable is assigned in the loop - which the loop does not see (MS-VBAL §5.4.2.3) - to bounds that
    // are Strings, or assigned another subtype in its body.
    [TestMethod]
    public async Task AVariantCounter_IsSteppedAsTheLoopDecided()
        => await AssertSpeculatedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted()",
            "End Sub",
            "Private Function Counted() As String",
            "    Dim I As Variant",
            "    Dim P As Variant",
            "    Dim E As Variant",
            "    For I = 10 To 1 Step -3",
            "        Counted = Counted & I & \" \"",
            "    Next",
            "    P = -1",
            "    For I = 3 To 1 Step P",
            "        P = 1",
            "        Counted = Counted & I & \" \"",
            "    Next",
            "    E = \"10\"",
            "    For I = 7 To E",
            "        Counted = Counted & I & \" \"",
            "    Next",
            "    For I = 1 To \"4\"",
            "        Counted = Counted & TypeName(I) & \" \"",
            "    Next",
            "    For I = 1 To 5",
            "        If I = 2 Then I = I + 0.5",
            "        Counted = Counted & I & \" \"",
            "    Next",
            "    Counted = Counted & TypeName(I)",
            "End Function"), "Counted");

    // a statement the interpreter runs alone in the middle of a speculated loop - a Let of a Variant holding a Double to a Long, which compiled code does
    // not convert, whole or not - and compiled code goes on after it, with what the Variant then holds: never handed to the interpreter for good.
    // L = L + V is a Double at each of the 6 passes, the 7 of the fourth among them; and the last Let joins Strings, which is the interpreter's: 7.
    [TestMethod]
    public async Task AStatementTheInterpreterRunsInASpeculatedLoop_IsGoneOnFrom()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rounded()",
            "End Sub",
            "Private Function Rounded() As String",
            "    Dim I As Long",
            "    Dim L As Long",
            "    Dim V As Variant",
            "    V = 0",
            "    For I = 1 To 6",
            "        V = V + 0.75",
            "        L = L + V",
            "    Next",
            "    Rounded = L & \" \" & V & \" \" & TypeName(V)",
            "End Function"), "Rounded", recoveries: 7);

    // a Variant counter stepped by a Double, in a loop whose body compiled code runs whole: each Next steps it as the loop decided at its For, from the
    // state it found then.
    [TestMethod]
    public async Task AVariantCounterSteppedByADouble_InABodyWithNothingStepped_IsSteppedAsTheLoopDecided()
        => await AssertSpeculatedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Halves(); Wholes()",
            "End Sub",
            "Private Function Halves() As Double",
            "    Dim I As Variant",
            "    Dim S As Double",
            "    For I = 1 To 4 Step 0.25",
            "        S = S * 2 + I",
            "    Next",
            "    Halves = S + I",
            "End Function",
            "Private Function Wholes() As Long",
            "    Dim I As Variant",
            "    Dim S As Long",
            "    For I = 1 To 200",
            "        S = (S * 3 + I) Mod 100003",
            "    Next",
            "    Wholes = S + I",
            "End Function"), "Halves");

    // a Variant of each other scalar subtype - LongLong, Decimal, Boolean - and one that holds an error value, operated on as the interpreter does.
    [TestMethod]
    public async Task AVariantOfEachOtherSubtype_IsOperatedOnAsTheInterpreterDoes()
        => await AssertSpeculatedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Kinds()",
            "    Debug.Print Errored()",
            "End Sub",
            "Private Function Kinds() As String",
            "    Dim V As Variant",
            "    Dim I As Long",
            "    V = CLngLng(5)",
            "    For I = 1 To 3",
            "        V = V * 3 + I",
            "    Next",
            "    Kinds = V & \" \" & TypeName(V)",
            "    V = CDec(1.5)",
            "    For I = 1 To 3",
            "        V = V * 2 + 0.25",
            "    Next",
            "    Kinds = Kinds & \" \" & V & \" \" & TypeName(V)",
            "    V = True",
            "    For I = 1 To 3",
            "        V = V + True",
            "        If V Then Kinds = Kinds & \" \" & V",
            "    Next",
            "    Kinds = Kinds & \" \" & TypeName(V)",
            "End Function",
            "Private Function Errored() As String",
            "    Dim V As Variant",
            "    Dim W As Variant",
            "    Dim I As Long",
            "    On Error GoTo Failed",
            "    V = CVErr(2042)",
            "    For I = 1 To 2",
            "        W = V + I",
            "    Next",
            "    Exit Function",
            "Failed:",
            "    Errored = Err.Number & \" \" & TypeName(V) & \" \" & IsEmpty(W)",
            "End Function"), "Kinds");

    // one Variant passed by reference as two parameters: each is another name for the caller's variable, and what one writes the other reads.
    [TestMethod]
    public async Task AVariantPassedByReferenceAsTwoParameters_IsOneVariable()
        => await AssertSpeculatedAsync(Program(
            "Public Sub Main()",
            "    Dim X As Variant",
            "    X = 1",
            "    Twice X, X",
            "    Debug.Print X; TypeName(X)",
            "End Sub",
            "Private Sub Twice(ByRef A As Variant, ByRef B As Variant)",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        A = A + 1",
            "        B = B * 2",
            "    Next",
            "End Sub"), "Twice");

    // a loop of a Variant counter entered again, its For statement run again with another end: each Next steps the counter to the end it was given
    // this time, not the one before. The inner For statement, the interpreter's at each of the 2 passes: 2.
    [TestMethod]
    public async Task AVariantLoopEnteredAgain_IsSteppedToItsNewEnd()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Nested()",
            "End Sub",
            "Private Function Nested() As Long",
            "    Dim I As Long",
            "    Dim V As Variant",
            "    Dim N As Long",
            "    For I = 1 To 2",
            "        For V = 1 To I * 2",
            "            N = N + V",
            "        Next",
            "    Next",
            "    Nested = N",
            "End Function"), "Nested", recoveries: 2);

    // a Variant ReDim'd by the interpreter, then assigned: the ReDim keeps the Variant in its cell, which compiled code goes on reading, and finds the
    // element of the array it holds in. The ReDim, V = N, a Let over the array the Variant holds, and the Let of the function's String result: 3.
    [TestMethod]
    public async Task AVariantReDimmedByTheInterpreter_IsWrittenWhereTheFrameHoldsIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rebound()",
            "End Sub",
            "Private Function Rebound() As String",
            "    Dim V As Variant",
            "    Dim N As Long",
            "    V = 1",
            "    N = V + 1",
            "    ReDim V(2)",
            "    V(1) = N",
            "    N = N + V(1)",
            "    V = N",
            "    N = V * 2",
            "    Rebound = TypeName(V) & N",
            "End Function"), "Rebound", recoveries: 3);
}
