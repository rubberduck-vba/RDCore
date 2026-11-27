using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An assignment copies the value it assigns (<strong>MS-VBAL §5.5.1.2</strong>: the result of a Let-coercion is "a copy of the source value"):
/// an array or a user-defined type assigned to a variable, or passed <c>ByVal</c>, is the variable's own, and a write to one of its elements or
/// fields through the variable is not a write to the source. A <c>Variant</c> given an array, and a record assigned to another, shared the
/// source's elements before.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2 Let-coercion")]
public sealed class AssignmentCopyTests
{
    private static Task<string[]> Run(string declarations, params string[] lines)
        => Run([], declarations, lines);

    private static Task<string[]> Run(IReadOnlyList<(string Name, string Source)> classes, string declarations, params string[] lines)
        => RunAsync(classes, $"Attribute VB_Name = \"Program\"\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task AnArray_AssignedToAnArray_IsACopy()
        => CollectionAssert.AreEqual(new[] { "1 2" }, await Run(string.Empty,
            "Dim a(1) As Long", "a(0) = 1", "Dim b() As Long", "b = a", "b(0) = 2", "Debug.Print a(0) & \" \" & b(0)"));

    [TestMethod]
    public async Task AnArray_AssignedToAVariant_IsACopy()
        => CollectionAssert.AreEqual(new[] { "1 2" }, await Run(string.Empty,
            "Dim a(1) As Long", "a(0) = 1", "Dim v As Variant", "v = a", "v(0) = 2", "Debug.Print a(0) & \" \" & v(0)"));

    [TestMethod]
    public async Task AVariantHoldingAnArray_AssignedToAVariant_IsACopy()
        => CollectionAssert.AreEqual(new[] { "1 9" }, await Run(string.Empty,
            "Dim v As Variant, w As Variant", "v = Array(1, 2)", "w = v", "w(0) = 9", "Debug.Print v(0) & \" \" & w(0)"));

    [TestMethod]
    public async Task AnArrayOfArrays_AssignedToAVariant_IsACopyTheWholeWayDown()
        => CollectionAssert.AreEqual(new[] { "1 9" }, await Run(string.Empty,
            "Dim v As Variant, w As Variant", "v = Array(Array(1, 2))", "w = v", "w(0)(0) = 9", "Debug.Print v(0)(0) & \" \" & w(0)(0)"));

    [TestMethod]
    public async Task AVariantHoldingAnArray_PassedByVal_IsTheCalleesOwn()
        => CollectionAssert.AreEqual(new[] { "1" }, await Run(
            "Private Sub Change(ByVal v As Variant)\r\nv(0) = 5\r\nEnd Sub",
            "Dim a As Variant", "a = Array(1, 2)", "Change a", "Debug.Print a(0)"));

    // a value read from a variable is the value the variable holds then: a Variant given it does not change with the variable afterwards.
    [TestMethod]
    public async Task ANumber_AssignedToAVariant_IsWhatTheVariableHeldThen()
        => CollectionAssert.AreEqual(new[] { "1" }, await Run(string.Empty,
            "Dim n As Integer, v As Variant", "n = 1", "v = n", "n = 2", "Debug.Print v"));

    [TestMethod]
    public async Task ANumber_InAnArrayLiteral_IsWhatTheVariableHeldThen()
        => CollectionAssert.AreEqual(new[] { "1" }, await Run(string.Empty,
            "Dim n As Integer, v As Variant", "n = 1", "v = Array(n)", "n = 2", "Debug.Print v(0)"));

    [TestMethod]
    [DataRow("Set v = o", "v", DisplayName = "A Variant")]
    [DataRow("Set a(0) = o", "a(0)", DisplayName = "An element of a Variant array")]
    [DataRow("v = Array(o)", "v(0)", DisplayName = "An element of an array literal")]
    public async Task AnObject_SetToAVariant_IsTheObjectTheVariableHeldThen(string assignment, string read)
        => CollectionAssert.AreEqual(new[] { "Collection" }, await Run(string.Empty,
            "Dim o As Object, v As Variant, a(1) As Variant", "Set o = New Collection", assignment, "Set o = Nothing", $"Debug.Print TypeName({read})"));

    [TestMethod]
    public async Task ARecord_AssignedToARecord_IsACopy()
        => CollectionAssert.AreEqual(new[] { "1 2" }, await Run("Private Type T\r\n    X As Long\r\nEnd Type",
            "Dim p As T, q As T", "p.X = 1", "q = p", "q.X = 2", "Debug.Print p.X & \" \" & q.X"));

    [TestMethod]
    public async Task ARecordWithAVariantField_AssignedToARecord_IsACopy()
        => CollectionAssert.AreEqual(new[] { "a b" }, await Run("Private Type T\r\n    V As Variant\r\nEnd Type",
            "Dim p As T, q As T", "p.V = \"a\"", "q = p", "q.V = \"b\"", "Debug.Print p.V & \" \" & q.V"));

    [TestMethod]
    public async Task ARecordWithANestedRecord_AssignedToARecord_IsACopyTheWholeWayDown()
        => CollectionAssert.AreEqual(new[] { "1 2" }, await Run("Private Type TInner\r\n    X As Long\r\nEnd Type\r\nPrivate Type TOuter\r\n    Inner As TInner\r\nEnd Type",
            "Dim p As TOuter, q As TOuter", "p.Inner.X = 1", "q = p", "q.Inner.X = 2", "Debug.Print p.Inner.X & \" \" & q.Inner.X"));

    // a String field, and an element of a fixed-size array field of Strings, assigned with the record: the copy's characters are another String's, of the
    // same value, and the source's are where they were.
    [TestMethod]
    public async Task ARecordWithStringFields_AssignedToARecord_HasCharactersOfItsOwn()
        => CollectionAssert.AreEqual(new[] { "True True True abc abc", "True True q q" }, await Run(
            "Private Type T\r\n    S As String\r\n    N(1) As String\r\nEnd Type",
            "Dim p As T, q As T, s0 As LongPtr, n0 As LongPtr", "p.S = \"abc\"", "p.N(1) = \"q\"", "s0 = StrPtr(p.S)", "n0 = StrPtr(p.N(1))", "q = p",
            "Debug.Print (s0 <> 0) & \" \" & (StrPtr(q.S) <> s0) & \" \" & (StrPtr(p.S) = s0) & \" \" & p.S & \" \" & q.S",
            "Debug.Print (StrPtr(q.N(1)) <> n0) & \" \" & (StrPtr(p.N(1)) = n0) & \" \" & p.N(1) & \" \" & q.N(1)"));

    // an object field assigned with the record: the copy's field holds the object of its own, which lives on when the source's field lets go of it, and the
    // copy lets go of it when the copy is assigned a record that holds Nothing there.
    [TestMethod]
    public async Task AnObjectField_AssignedWithItsRecord_IsHeldByTheCopy_UntilTheCopyIsAssignedAnother()
        => CollectionAssert.AreEqual(new[] { "p let go", "True", "gone", "q assigned" }, await Run(
            [("Noisy", ClassModule("Noisy", "Option Explicit", "Private Sub Class_Terminate()", "    Debug.Print \"gone\"", "End Sub"))],
            "Private Type T\r\n    O As Noisy\r\n    N As Long\r\nEnd Type",
            "Dim p As T, q As T, e As T", "Set p.O = New Noisy", "q = p", "Set p.O = Nothing", "Debug.Print \"p let go\"",
            "Debug.Print TypeName(q.O) = \"Noisy\"", "q = e", "Debug.Print \"q assigned\""));

    // a record whose Variant field holds an array, let to a variable, to an element of an array of records and to a field of another record: each holds an
    // array of its own, so an element of the source's assigned afterwards is the source's alone, and a record let itself holds what it held.
    [TestMethod]
    public async Task ARecordWithAnArrayInAVariant_LetToAVariableAnElementAndAField_HoldsAnArrayOfItsOwn()
        => CollectionAssert.AreEqual(new[] { "9 1 1 1", "1 3" }, await Run(
            "Private Type T\r\n    V As Variant\r\n    N As Long\r\nEnd Type\r\nPrivate Type TOuter\r\n    In As T\r\nEnd Type",
            "Dim p As T, q As T, a(1 To 2) As T, o As TOuter", "p.V = Array(1, 2)", "p.N = 3", "q = p", "a(1) = p", "o.In = p", "p.V(0) = 9",
            "Debug.Print p.V(0) & \" \" & q.V(0) & \" \" & a(1).V(0) & \" \" & o.In.V(0)", "q = q", "Debug.Print q.V(0) & \" \" & q.N"));
}
