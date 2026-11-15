using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.6.12</strong> a member access is of an object or a record: an array has no member. A variable declared as an array is refused at compile
/// time, <em>Invalid qualifier</em>; a <c>Variant</c> that holds one is error 424 when the member is assigned. Each expectation is what MS-VBA reported for the
/// same code, observed in Excel.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.12 Member Access Expressions")]
public sealed class MemberOfAnArrayTests
{
    private static string Program(params string[] body)
        => string.Join("\r\n",
            [
                "Attribute VB_Name = \"Program\"",
                "Option Explicit",
                "Public Sub Main()",
                "    On Error Resume Next",
                .. body,
                "End Sub",
            ]) + "\r\n";

    [TestMethod]
    [DataRow("v = Array(1, 2)", "v.Foo = 1", DisplayName = "an array, assigned (I01)")]
    [DataRow("v = Array(1, 2)", "x = v.Foo", DisplayName = "an array, read (C03)")]
    [DataRow("v = 5", "v.Foo = 1", DisplayName = "a number, assigned (C04)")]
    [DataRow("v = Array(1, 2)", "v.Foo(1) = 1", DisplayName = "an array, indexed and assigned (D09)")]
    [DataRow("v = Array(1, 2)", "Set v.Foo = Nothing", DisplayName = "an array, Set (D10)")]
    public async Task AMemberOfAVariantHoldingNoObject_IsObjectRequired(string held, string statement)
        => CollectionAssert.AreEqual(new[] { "424" },
            await RunAsync([], Program("Dim v As Variant, x As Variant", held, statement, "Debug.Print Err.Number")));

    [TestMethod]
    [DataRow("Dim v(2) As Long", "v.Foo = 1", DisplayName = "fixed-size, assigned (I02)")]
    [DataRow("Dim v() As Long", "v.Foo = 1", DisplayName = "resizable, assigned (C01)")]
    [DataRow("Dim v(2) As Long", "x = v.Foo", DisplayName = "fixed-size, read (C02)")]
    [DataRow("Dim v() As Long", "x = v.Foo", DisplayName = "resizable, read (C13)")]
    public async Task AMemberOfAVariableDeclaredAsAnArray_IsInvalidQualifier(string declaration, string statement)
    {
        var errors = await LoadErrorsAsync([], Program(declaration, "Dim x As Variant", statement));

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        Assert.StartsWith("Invalid qualifier", errors[0]);
    }
}
