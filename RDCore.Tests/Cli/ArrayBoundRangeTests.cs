using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An array's bounds are <c>Long</c>: a fixed-size array's are "Let-coercible to the declared type Long" (<strong>MS-VBAL §5.2.3.1.3</strong>), and
/// <c>ReDim</c>'s are integer expressions, "Let-coerced to Long" (<strong>§5.6.16.4</strong>). Found by the first run of <c>rdc bench</c>: the bounds
/// were Let-coerced to <c>Integer</c>, so that a bound past 32767 overflowed and the array was never allocated.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.1.3 Array Dimensions and Bounds")]
public sealed class ArrayBoundRangeTests
{
    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task AFixedSizeArray_WhoseBoundIsPastTheRangeOfAnInteger_IsIndexed()
        => CollectionAssert.AreEqual(new[] { "7", "40000" }, await Run(
            "Dim Values(1 To 40000) As Long",
            "Values(40000) = 7",
            "Debug.Print Values(40000)",
            "Debug.Print UBound(Values)"));

    [TestMethod]
    public async Task AFixedSizeArrayOfTheModule_WhoseBoundIsPastTheRangeOfAnInteger_IsIndexed()
        => CollectionAssert.AreEqual(new[] { "7" }, await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nPrivate Values(-40000 To 40000) As Long\r\nPublic Sub Main()\r\nValues(-40000) = 7\r\nDebug.Print Values(-40000)\r\nEnd Sub\r\n"));

    [TestMethod]
    public async Task ReDim_WithABoundPastTheRangeOfAnInteger_Allocates()
        => CollectionAssert.AreEqual(new[] { "100000 7" }, await Run(
            "Dim Values() As Long",
            "Dim Upper As Long",
            "Upper = 100000",
            "ReDim Values(1 To Upper)",
            "Values(Upper) = 7",
            "Debug.Print UBound(Values) & \" \" & Values(Upper)"));

    [TestMethod]
    public async Task ReDim_WithADoubleBound_RoundsIt_AsALetCoercionToLong()
        // 2.5 rounds to the even 2 (MS-VBAL 5.5.1.2.1.1).
        => CollectionAssert.AreEqual(new[] { "2" }, await Run(
            "Dim Values() As Long",
            "ReDim Values(2.5)",
            "Debug.Print UBound(Values)"));
}
