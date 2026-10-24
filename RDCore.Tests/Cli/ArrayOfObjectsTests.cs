using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// Arrays of objects: an <c>As</c> clause that names a class or <c>Object</c> with a trailing <c>()</c> declares an array of it (<strong>MS-VBAL
/// §5.2.3.1</strong>, §5.3.1.6), and a <c>For Each</c> over an array Set-assigns an element that is an object to the control variable - whatever the
/// array is declared as, as MS-VBA does (§5.4.2.4 says only an array declared <c>Object</c>, which the author rules an oversight).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.1 Variable Declarations")]
public sealed class ArrayOfObjectsTests
{
    private static readonly (string, string) Widget = ("Widget", ClassModule("Widget", "Public Name As String"));

    private const string Make = "Private Function Make(ByVal name As String) As Widget\r\n    Set Make = New Widget\r\n    Make.Name = name\r\nEnd Function";

    private static Task<string[]> RunWith(string declarations, params string[] lines)
        => RunAsync([Widget], $"Attribute VB_Name = \"Program\"\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    // TypeName of an array of objects is "Object()" whatever class its elements are declared as (recorded in Excel: TypeNameAndVarTypeTests).
    [TestMethod]
    [DataRow("Object", DisplayName = "As Object()")]
    [DataRow("Widget", DisplayName = "As Widget()")]
    public async Task AFunctionDeclaredAsAnArrayOfObjects_ReturnsOne(string typeName)
        => CollectionAssert.AreEqual(new[] { "Object() a b" }, await RunWith(
            Make + $"\r\nPrivate Function Both() As {typeName}()\r\n    Dim r(1) As {typeName}\r\n    Set r(0) = Make(\"a\")\r\n    Set r(1) = Make(\"b\")\r\n    Both = r\r\nEnd Function",
            $"Dim items() As {typeName}",
            "items = Both()",
            "Debug.Print TypeName(items) & \" \" & items(0).Name & \" \" & items(1).Name"));

    // the () after the name: written on the type (`As Widget()`), it is a syntax error (ArrayTypeClauseTests).
    [TestMethod]
    public async Task AVariableDeclaredAsAnArrayOfAClass_IsAResizableArrayOfIt()
        => CollectionAssert.AreEqual(new[] { "Object() 2" }, await RunWith(string.Empty,
            "Dim items() As Widget",
            "ReDim items(1 To 2)",
            "Debug.Print TypeName(items) & \" \" & UBound(items)"));

    [TestMethod]
    public async Task ForEachOverAVariantArrayOfObjects_SetAssignsEachToTheControlVariable()
        => CollectionAssert.AreEqual(new[] { "a", "b" }, await RunWith(Make,
            "Dim x As Variant",
            "For Each x In Array(Make(\"a\"), Make(\"b\"))",
            "    Debug.Print x.Name",
            "Next"));

    [TestMethod]
    public async Task ForEachOverAnArrayOfAClass_SetAssignsEachToTheControlVariable()
        => CollectionAssert.AreEqual(new[] { "a", "b" }, await RunWith(Make,
            "Dim items(1) As Widget, x As Widget",
            "Set items(0) = Make(\"a\")",
            "Set items(1) = Make(\"b\")",
            "For Each x In items",
            "    Debug.Print x.Name",
            "Next"));

    [TestMethod]
    public async Task ForEachOverAVariantArrayOfNumbers_StillLetAssignsThem()
        => CollectionAssert.AreEqual(new[] { "1", "2" }, await RunWith(string.Empty,
            "Dim x As Variant",
            "For Each x In Array(1, 2)",
            "    Debug.Print x",
            "Next"));
}
