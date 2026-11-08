using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A function of the <c>Variant</c> type, compiled: its result assigned a payload, and read, where its frame holds it (MS-VBAL §5.3.1); a call of it
/// assigned to a <c>Variant</c> made by compiled code, what it returned then assigned as the interpreter assigns the value of a statement's expression,
/// whatever it holds - a value of any subtype as it is (MS-VBAL §5.5.1.2.12), <c>Empty</c> and <c>Null</c> not converted, an array copied, an object's
/// default member read - and let go of when the statement is over, which is where an object only it held is destroyed. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledVariantResultTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Noisy", ModuleWorkspace.ClassModule("Noisy", "Public Id As Long", "Private Sub Class_Terminate()", "    Log = Log & \"gone \" & Id & \";\"", "End Sub")),
        ("Holder", ModuleWorkspace.ClassModule("Holder",
            "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Log = Log & \"value;\"", "    Value = 42", "End Property",
            "Private Sub Class_Terminate()", "    Log = Log & \"holder gone;\"", "End Sub")),
        ("Doubler", ModuleWorkspace.ClassModule("Doubler", "Public Function Twice(ByVal X As Long) As Variant", "    Twice = X * 2", "End Function")),
    ];

    // the program run both ways, printing what MS-VBA prints, each procedure named compiled with the recoveries and walk-backs it says.
    private static async Task AssertRunAsync(string program, string expected, params (string Procedure, long Recoveries, long WalkBacks)[] procedures)
    {
        var compiled = await AssertAlikeAsync(program, Classes, expected);
        foreach (var (procedure, recoveries, walkBacks) in procedures)
        {
            Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
            Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");

            // under the random walk-back every activation may be handed to the interpreter, which is what that run is for.
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(walkBacks, result.Body.WalkBacks, $"{procedure}'s walk-backs, the last at {result.Body.LastWalkedBackAt}");
                Assert.AreEqual(recoveries, result.Body.Recoveries, $"{procedure}'s recoveries");
            }
        }
    }

    // a Variant function assigned a value of each scalar type holds it as its own subtype, which its caller is assigned as it is and adds as it is. The
    // recovery is the sum with a Currency, which compiled code's Variant arithmetic declines.
    [TestMethod]
    public async Task AVariantResultOfEachScalarSubtype_IsAssignedAndReadInCompiledCode()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Dim K As Long",
            "    Dim S As String",
            "    For K = 1 To 8",
            "        S = S & VarType(Made(K)) & \",\"",
            "    Next",
            "    Debug.Print S & Summed()",
            "End Sub",
            "Private Function Summed() As Double",
            "    Dim V As Variant",
            "    Dim K As Long",
            "    For K = 1 To 8",
            "        V = Made(K)",
            "        Summed = Summed + V",
            "    Next",
            "End Function",
            "Private Function Made(ByVal K As Long) As Variant",
            "    Dim I As Integer",
            "    Dim L As Long",
            "    Dim F As Single",
            "    Dim D As Double",
            "    Dim C As Currency",
            "    Dim T As Date",
            "    Dim B As Boolean",
            "    Dim Y As Byte",
            "    I = 3%",
            "    L = 100000&",
            "    F = 1.5!",
            "    D = 2.25#",
            "    C = 12.5@",
            "    T = #1/1/1900#",
            "    B = True",
            "    Y = 200",
            "    Select Case K",
            "        Case 1",
            "            Made = I",
            "        Case 2",
            "            Made = L",
            "        Case 3",
            "            Made = F",
            "        Case 4",
            "            Made = D",
            "        Case 5",
            "            Made = C",
            "        Case 6",
            "            Made = T",
            "        Case 7",
            "            Made = B",
            "        Case 8",
            "            Made = Y",
            "    End Select",
            "End Function"), "2,3,4,5,6,7,11,17,100220.25", ("Summed", 1, 0), ("Made", 0, 0));

    // a Variant function's result read in its own body, and assigned what it read: a sum it keeps of its own subtype, Integer then Double.
    [TestMethod]
    public async Task AVariantResultReadInItsOwnBody_IsReadInCompiledCode()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Debug.Print Summed() & \";\" & TypeName(Accumulated(0)) & \";\" & TypeName(Accumulated(1))",
            "End Sub",
            "Private Function Summed() As Double",
            "    Dim V As Variant",
            "    V = Accumulated(10)",
            "    Summed = V",
            "End Function",
            "Private Function Accumulated(ByVal N As Long) As Variant",
            "    Dim I As Long",
            "    Accumulated = 0",
            "    For I = 1 To N",
            "        Accumulated = Accumulated + I * 1.5",
            "    Next",
            "End Function"), "82.5;Integer;Double", ("Summed", 0, 0), ("Accumulated", 0, 0));

    // what a Variant function returns that compiled code reads nothing of - Empty, Null, a String, an error value, an array - is its caller's as it is: not
    // converted, its subtype kept; the array a copy, which a write to does not write the array it was copied from (MS-VBAL §5.5.1.2.12).
    [TestMethod]
    public async Task AVariantResultHoldingWhatIsNoScalar_IsAssignedAsItIs()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Private Held As Variant",
            "Public Sub Main()",
            "    Dim K As Long",
            "    Dim V As Variant",
            "    Held = Array(1, 2)",
            "    For K = 1 To 5",
            "        V = Relayed(K)",
            "        Log = Log & K & \":\" & VarType(V) & \":\" & IsEmpty(V) & \":\" & IsNull(V) & \";\"",
            "    Next",
            "    V(0) = 9",
            "    Debug.Print Log & \"held \" & Held(0) & \" copy \" & V(0)",
            "End Sub",
            "Private Function Relayed(ByVal K As Long) As Variant",
            "    Relayed = Kept(K)",
            "End Function",
            "Private Function Kept(ByVal K As Long) As Variant",
            "    Select Case K",
            "        Case 2",
            "            Kept = Null",
            "        Case 3",
            "            Kept = \"text\"",
            "        Case 4",
            "            Kept = CVErr(2042)",
            "        Case 5",
            "            Kept = Held",
            "    End Select",
            "End Function"),
            "1:0:True:False;2:1:False:True;3:8:False:False;4:10:False:False;5:8204:False:False;held 1 copy 9", ("Relayed", 0, 0));

    // a Variant function's result that holds an object: called as a statement, what it returned is let go of when the statement is over, which destroys the
    // object; assigned to a Variant, the object's default member is read and its value assigned, and the object is destroyed after that, at the end of the
    // statement, before the next one runs. Each recovery is a String joined to Log.
    [TestMethod]
    public async Task AnObjectAVariantResultHolds_IsLetGoOfWhenItsStatementIsOver()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Log = \"a;\"",
            "    Discarded",
            "    Log = Log & \"b;\"",
            "    Log = Log & Read() & \";c;\"",
            "    Debug.Print Log",
            "End Sub",
            "Private Sub Discarded()",
            "    Made 1",
            "    Log = Log & \"after made;\"",
            "End Sub",
            "Private Function Made(ByVal Id As Long) As Variant",
            "    Dim N As Noisy",
            "    Set N = New Noisy",
            "    N.Id = Id",
            "    Set Made = N",
            "End Function",
            "Private Function Read() As Long",
            "    Dim V As Variant",
            "    V = Valued()",
            "    Log = Log & \"read;\"",
            "    Read = V",
            "End Function",
            "Private Function Valued() As Variant",
            "    Set Valued = New Holder",
            "End Function"), "a;gone 1;after made;b;value;holder gone;read;42;c;", ("Discarded", 1, 0), ("Read", 1, 0));

    // two calls in one statement, each returning a Variant that holds an object only it holds: both objects live until the statement is over, after its
    // assignment, and are let go of then, the last made first - none before the other call is made. Compiled code gives that statement to the interpreter;
    // whichever engine runs it, the order is the same.
    [TestMethod]
    public async Task ObjectsTwoVariantResultsOfAStatementHold_AreLetGoOfWhenTheStatementIsOver()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Both",
            "    Debug.Print Log",
            "End Sub",
            "Private Sub Both()",
            "    Dim S As String",
            "    Log = \"a;\"",
            "    S = IdOf(Made(1)) & IdOf(Made(2))",
            "    Log = Log & S & \";b;\"",
            "End Sub",
            "Private Function IdOf(ByVal V As Variant) As Long",
            "    IdOf = V.Id",
            "End Function",
            "Private Function Made(ByVal Id As Long) As Variant",
            "    Dim N As Noisy",
            "    Set N = New Noisy",
            "    N.Id = Id",
            "    Set Made = N",
            "End Function"), "a;gone 2;gone 1;12;b;");

    // a member of an object that returns a Variant, called by compiled code and assigned to a Variant, which compiled code then adds. The recovery is the
    // Set of the object.
    [TestMethod]
    public async Task AVariantResultOfAMember_IsAssignedToItsCaller()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Debug.Print Total()",
            "End Sub",
            "Private Function Total() As Long",
            "    Dim O As Doubler",
            "    Dim V As Variant",
            "    Dim K As Long",
            "    Set O = New Doubler",
            "    For K = 1 To 10",
            "        V = O.Twice(K)",
            "        Total = Total + V",
            "    Next",
            "End Function"), "110", ("Total", 1, 0));

    // an error the assignment of what a call returned raises - an object with no default member, assigned to a Variant - is the statement's: the handler
    // is given it, and the call is not made again. The 2 recoveries are that statement's error and Err.Number.
    [TestMethod]
    public async Task AnErrorAssigningWhatACallReturned_IsTheStatementsAndTheCallIsNotMadeAgain()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Calls As Long",
            "Public Sub Main()",
            "    Debug.Print Tried() & \";\" & Log",
            "End Sub",
            "Private Function Tried() As Long",
            "    Dim V As Variant",
            "    On Error Resume Next",
            "    V = Plain()",
            "    Tried = Err.Number * 100 + Calls",
            "End Function",
            "Private Function Plain() As Variant",
            "    Calls = Calls + 1",
            "    Set Plain = New Noisy",
            "End Function"), "43801;gone 0;", ("Tried", 2, 0));
}
