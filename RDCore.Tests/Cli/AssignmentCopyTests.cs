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
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

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
}
