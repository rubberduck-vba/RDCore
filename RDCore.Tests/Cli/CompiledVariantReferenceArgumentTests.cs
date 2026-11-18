using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> variable given to a <c>Variant</c> parameter passed by reference of a procedure or a member compiled code calls (MS-VBAL §5.3.1.11):
/// the variable's address, which the parameter is a second name for, so that what the callee assigns it is what the caller holds after, an array included.
/// A <c>Variant</c> parameter bound to a variable of another type passes that variable on, with its type. A function of the standard library
/// given an element of an array of <c>Variant</c>s, or of the array a <c>Variant</c> holds, is given what the element holds, whatever it holds
/// (MS-VBAL §5.3.1.11), and <c>IsEmpty</c> of a <c>Variant</c> that holds an array is False. A program runs the same either way.
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
            "Public Function Tagged(Value As Variant) As Long",
            "    Value = Value & \"!\"",
            "    Tagged = 1",
            "End Function",
            "Public Function IsHeld(Key As Variant) As Boolean",
            "    Dim Pair As Variant",
            "    Pair = Found(Key)",
            "    If Not IsEmpty(Pair) Then",
            "        If Not VBA.IsObject(Pair(1)) Then IsHeld = True",
            "    End If",
            "End Function")),
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
                Assert.AreEqual(recoveries, result.Body.Recoveries, $"{procedure}'s recoveries");            }
        }
    }

    // a Variant variable given by reference to a function, at each turn of a loop: what the function assigns its parameter, a number then a String, is
    // what the variable holds after the call, which it gives on to be shown.
    [TestMethod]
    public async Task AVariantGivenByReference_ToAFunction_HoldsWhatTheCalleeAssigns()
        => await AssertRunAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Dim V As Variant",
            "    Dim I As Long, N As Long",
            "    V = 1",
            "    For I = 1 To 3",
            "        N = N + Twice(V)",
            "    Next",
            "    Shown V",
            "    N = N + Named(V)",
            "    Shown V",
            "    Counted = N",
            "End Function",
            "Private Function Twice(X As Variant) As Long",
            "    X = X * 2",
            "    Twice = 1",
            "End Function",
            "Private Function Named(ByRef X As Variant) As Long",
            "    X = \"eight\"",
            "    Named = 10",
            "End Function",
            "Private Sub Shown(X As Variant)",
            "    Debug.Print TypeName(X) & \" \" & X",
            "End Sub"), "Integer 8 ; String eight ; 13", ("Counted", 0));

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
            "    Dim I As Long, Grew As Boolean",
            "    For I = 1 To 2",
            "        Grew = Grown(Given)",
            "    Next",
            "End Sub",
            "Private Function Grown(X As Variant) As Boolean",
            "    X = X & \"y\"",
            "    Grown = True",
            "End Function"), "xyy", ("Relayed", 0));

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
            "    Dim Mark As Long",
            "    Mark = Marked(Given)",
            "    Grown Given",
            "End Sub",
            "Private Function Marked(X As Variant) As Long",
            "    X(1) = 20",
            "End Function",
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

    // the rest of the dictionary's lookup: IsEmpty of the array a function of the class returned, then IsObject of an element of it, a String; Empty
    // when it returned nothing. IsHeld recovers nothing.
    [TestMethod]
    public async Task IsEmptyAndIsObjectOfTheArrayAFunctionReturned_AreCompiled()
        => await AssertRunAsync(Program(
            "Public Sub Main()",
            "    Dim L As New Lookup",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        Debug.Print L.IsHeld(\"a\") & \" \" & L.IsHeld(\"c\")",
            "    Next",
            "End Sub"), "True False ; True False ; True False", ("IsHeld", 0));

    // IsEmpty of a Variant that holds an array, and of an element that holds one: neither is Empty, which compiled code reads in the Variant.
    [TestMethod]
    public async Task IsEmptyOfAVariantThatHoldsAnArray_IsFalse()
        => await AssertRunAsync(Program(
            "Public Sub Main()",
            "    Dim V As Variant",
            "    V = Array(1, Array(2, 3), Empty)",
            "    Debug.Print Filled(V)",
            "End Sub",
            "Private Function Filled(Given As Variant) As Long",
            "    Dim I As Long",
            "    If Not IsEmpty(Given) Then Filled = 100",
            "    For I = 0 To 2",
            "        If Not IsEmpty(Given(I)) Then Filled = Filled + I + 1",
            "    Next",
            "End Function"), "103", ("Filled", 0));

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
            "    Dim I As Long, N As Long",
            "    For I = 1 To 3",
            "        N = N + L.Tagged(Given)",
            "    Next",
            "End Sub"), "t!!!", ("Tags", 0));

    // a Variant parameter bound to a Long of its caller is a Variant reference to a Long, which compiled code gives on as one, with that type: the
    // callee's assignment is let-coerced to Long.
    [TestMethod]
    public async Task AVariantParameterBoundToALong_IsGivenOnWithItsType()
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
            "End Sub"), "Long 7", ("Relayed", 0));
}
