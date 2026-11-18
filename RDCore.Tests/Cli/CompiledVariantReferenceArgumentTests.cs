using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> variable given to a <c>Variant</c> parameter passed by reference of a procedure or a member compiled code calls (MS-VBAL §5.3.1.11):
/// the variable's address, which the parameter is a second name for, so that what the callee assigns it is what the caller holds after, an array included.
/// A <c>Variant</c> parameter bound to a variable of another type is handed on by the interpreter, with that type. A function of the standard library
/// given an element of an array of <c>Variant</c>s, or of the array a <c>Variant</c> holds, is given what the element holds, whatever it holds
/// (MS-VBAL §5.3.1.11). A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledVariantReferenceArgumentTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Lookup", ModuleWorkspace.ClassModule("Lookup",
            "Option Explicit",
            "Private mItems As Collection",
            "Private Sub Class_Initialize()",
            "    Set mItems = New Collection",
            "    mItems.Add Array(\"a\", \"first\", 5), \"a\"",
            "    mItems.Add Array(\"b\", \"second\", 7), \"b\"",
            "End Sub",
            "Public Sub Find(Key As Variant, Result As Variant)",
            "    Result = Found(Key)",
            "End Sub",
            "Private Function Found(Key As Variant) As Variant",
            "    On Error Resume Next",
            "    Found = mItems(Key)",
            "    Err.Clear",
            "End Function",
            "Public Sub Tagged(Value As Variant)",
            "    Value = Value & \"!\"",
            "End Sub")),
    ];

    // the program run both ways, printing what MS-VBA prints, each procedure named compiled with the recoveries it says, and no walk-back.
    private static async Task AssertRunAsync(string program, string expected, params (string Procedure, long Recoveries)[] procedures)
    {
        var compiled = await AssertAlikeAsync(program, Classes, expected);
        foreach (var (procedure, recoveries) in procedures)
        {
            Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
            Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");

            // under the random walk-back every activation may be handed to the interpreter, which is what that run is for.
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(0L, result.Body.WalkBacks, $"{procedure}'s walk-backs, the last at {result.Body.LastWalkedBackAt}");
                Assert.AreEqual(recoveries, result.Body.Recoveries, $"{procedure}'s recoveries");
            }
        }
    }

    // a Variant variable given by reference to a procedure, at each turn of a loop: what the procedure assigns its parameter, a number then a String, is
    // what the variable holds after the call, which it gives on to be shown.
    [TestMethod]
    public async Task AVariantGivenByReference_ToAProcedure_HoldsWhatTheCalleeAssigns()
        => await AssertRunAsync(Program(
            "Public Sub Main()",
            "    Counted",
            "End Sub",
            "Private Sub Counted()",
            "    Dim V As Variant",
            "    Dim I As Long",
            "    V = 1",
            "    For I = 1 To 3",
            "        Twice V",
            "    Next",
            "    Shown V",
            "    Named V",
            "    Shown V",
            "End Sub",
            "Private Sub Twice(X As Variant)",
            "    X = X * 2",
            "End Sub",
            "Private Sub Named(ByRef X As Variant)",
            "    X = \"eight\"",
            "End Sub",
            "Private Sub Shown(X As Variant)",
            "    Debug.Print TypeName(X) & \" \" & X",
            "End Sub"), "Integer 8 ; String eight", ("Counted", 0));

    // a Variant parameter given on by reference: the callee assigns the caller's caller's variable.
    [TestMethod]
    public async Task AVariantParameter_GivenOnByReference_IsTheCallersVariable()
        => await AssertRunAsync(Program(
            "Public Sub Main()",
            "    Dim V As Variant",
            "    V = \"x\"",
            "    Relayed V",
            "    Debug.Print V",
            "End Sub",
            "Private Sub Relayed(Given As Variant)",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Grown Given",
            "    Next",
            "End Sub",
            "Private Sub Grown(X As Variant)",
            "    X = X & \"y\"",
            "End Sub"), "xyy", ("Relayed", 0));

    // a Variant that holds an array, given by reference: the callee writes an element of the caller's array, and ReDims it.
    [TestMethod]
    public async Task AVariantHoldingAnArray_GivenByReference_IsTheCallersArray()
        => await AssertRunAsync(Program(
            "Public Sub Main()",
            "    Dim V As Variant",
            "    V = Array(1, 2, 3)",
            "    Filled V",
            "    Debug.Print V(0) & \",\" & V(1) & \",\" & UBound(V)",
            "End Sub",
            "Private Sub Filled(Given As Variant)",
            "    Marked Given",
            "    Grown Given",
            "End Sub",
            "Private Sub Marked(X As Variant)",
            "    X(1) = 20",
            "End Sub",
            "Private Sub Grown(X As Variant)",
            "    ReDim Preserve X(4)",
            "End Sub"), "1,20,4", ("Filled", 0));

    // the shape of a dictionary's lookup: a member of the class calls a function of its own, given its Variant parameter by reference, and assigns what
    // it returns, an array, to the caller's Variant. Find recovers nothing.
    [TestMethod]
    public async Task AVariantParameter_GivenToAFunctionOfItsOwnClass_IsGivenByReference()
        => await AssertRunAsync(Program(
            "Public Sub Main()",
            "    Dim L As New Lookup",
            "    Dim R As Variant",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        L.Find \"b\", R",
            "        Debug.Print R(1) & \" \" & R(2)",
            "        L.Find \"c\", R",
            "        Debug.Print IsEmpty(R)",
            "    Next",
            "End Sub"), "second 7 ; True ; second 7 ; True ; second 7 ; True", ("Find", 0));

    // a function of the standard library given an element of the array a Variant holds, whatever the element holds: a String, Empty, an object or a
    // number; IsObject is True of the object alone, and IsNumeric of the number and of the Strings of numbers.
    [TestMethod]
    public async Task ALibraryFunction_GivenAnElementOfTheArrayAVariantHolds_IsGivenWhatItHolds()
        => await AssertRunAsync(Program(
            "Public Sub Main()",
            "    Dim V As Variant",
            "    V = Array(\"x\", Empty, New Collection, 2)",
            "    Dim N As Variant",
            "    N = Array(\"x\", \"12\", 3.5, \"4e2\")",
            "    Debug.Print Objects(V) & \" \" & Numbers(N)",
            "End Sub",
            "Private Function Objects(Given As Variant) As Long",
            "    Dim I As Long",
            "    For I = 0 To 3",
            "        If VBA.IsObject(Given(I)) Then Objects = Objects + I",
            "    Next",
            "End Function",
            "Private Function Numbers(Given As Variant) As Long",
            "    Dim I As Long",
            "    For I = 0 To 3",
            "        If IsNumeric(Given(I)) Then Numbers = Numbers + 1",
            "    Next",
            "End Function"), "2 3", ("Objects", 0), ("Numbers", 0));

    // IsObject of each element of an array of Variants: a String, an object and Null.
    [TestMethod]
    public async Task IsObjectOfAnElementOfAnArrayOfVariants_IsGivenWhatItHolds()
        => await AssertRunAsync(Program(
            "Public Sub Main()",
            "    Dim A(1 To 3) As Variant",
            "    A(1) = \"s\"",
            "    Set A(2) = New Collection",
            "    A(3) = Null",
            "    Debug.Print Held(A)",
            "End Sub",
            "Private Function Held(A() As Variant) As Long",
            "    Dim I As Long",
            "    For I = LBound(A) To UBound(A)",
            "        If VBA.IsObject(A(I)) Then Held = Held + I",
            "    Next",
            "End Function"), "2", ("Held", 0));

    // a Variant variable given by reference to a member of another object: the member assigns the caller's variable.
    [TestMethod]
    public async Task AVariantGivenByReference_ToAMemberOfAnObject_HoldsWhatTheMemberAssigns()
        => await AssertRunAsync(Program(
            "Public Sub Main()",
            "    Dim V As Variant",
            "    V = \"t\"",
            "    Tags New Lookup, V",
            "    Debug.Print V",
            "End Sub",
            "Private Sub Tags(ByVal L As Lookup, Given As Variant)",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        L.Tagged Given",
            "    Next",
            "End Sub"), "t!!!", ("Tags", 0));

    // a Variant parameter bound to a Long of its caller is a Variant reference to a Long, which the interpreter hands on as one: the callee's
    // assignment is let-coerced to Long, and the call is the interpreter's, once at each turn.
    [TestMethod]
    public async Task AVariantParameterBoundToALong_IsHandedOnByTheInterpreter()
        => await AssertRunAsync(Program(
            "Public Sub Main()",
            "    Dim N As Long",
            "    N = 1",
            "    Relayed N",
            "    Debug.Print TypeName(N) & \" \" & N",
            "End Sub",
            "Private Sub Relayed(Given As Variant)",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Halved Given",
            "    Next",
            "End Sub",
            "Private Sub Halved(X As Variant)",
            "    X = X + 2.6",
            "End Sub"), "Long 7", ("Relayed", 2));
}
