using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.3.1.11</strong> a <c>Variant</c> reference parameter given a variable of another type is that variable, and a <c>ReDim</c> of the parameter
/// (<strong>§5.4.3.3</strong>) re-dimensions the variable: an array keeps the element type it is declared with. Each expectation is what MS-VBA printed for the
/// same code, recorded in Excel (oracle\s56 I03-I06).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
public sealed class ReDimThroughAVariantParameterTests
{
    private static Task<string[]> RunAsync(string grow, params string[] body)
        => ModuleWorkspace.RunAsync([], string.Join("\r\n",
            [
                "Attribute VB_Name = \"Program\"",
                "Option Explicit",
                grow,
                "Public Sub Main()",
                "    On Error Resume Next",
                .. body,
                "End Sub",
            ]) + "\r\n");

    [TestMethod]
    public async Task AResizableArray_GivenToAVariantParameter_IsReDimensionedThroughIt_AndKeepsItsElementType()
        => CollectionAssert.AreEqual(new[] { "0", "5", "Long()" },
            await RunAsync("Private Sub Grow(a As Variant)\r\n    ReDim a(5)\r\nEnd Sub",
                "Dim arr() As Long", "ReDim arr(2)", "Grow arr", "Debug.Print Err.Number", "Debug.Print UBound(arr)", "Debug.Print TypeName(arr)"));

    [TestMethod]
    public async Task AResizableArray_GivenToAnUntypedParameter_IsReDimPreservedThroughIt_AndKeepsItsElements()
        => CollectionAssert.AreEqual(new[] { "0", "5", "7", "Long()" },
            await RunAsync("Private Sub Grow(a)\r\n    ReDim Preserve a(5)\r\nEnd Sub",
                "Dim arr() As Long", "ReDim arr(2)", "arr(1) = 7", "Grow arr",
                "Debug.Print Err.Number", "Debug.Print UBound(arr)", "Debug.Print arr(1)", "Debug.Print TypeName(arr)"));

    [TestMethod]
    public async Task AFixedSizeArray_GivenToAVariantParameter_IsNotReDimensioned_Error10()
        => CollectionAssert.AreEqual(new[] { "10", "2" },
            await RunAsync("Private Sub Grow(a As Variant)\r\n    ReDim a(5)\r\nEnd Sub",
                "Dim arr(2) As Long", "Grow arr", "Debug.Print Err.Number", "Debug.Print UBound(arr)"));

    [TestMethod]
    public async Task AVariantHoldingAnArray_GivenToAVariantParameter_IsReDimensionedThroughIt()
        => CollectionAssert.AreEqual(new[] { "0", "5", "9" },
            await RunAsync("Private Sub Grow(a As Variant)\r\n    ReDim a(5)\r\n    a(4) = 9\r\nEnd Sub",
                "Dim arr As Variant", "arr = Array(1, 2)", "Grow arr", "Debug.Print Err.Number", "Debug.Print UBound(arr)", "Debug.Print arr(4)"));
}
