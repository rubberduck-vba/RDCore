using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What the parity tests of plan execution (IR lowering S0.7) found that the interpreter gets wrong, whichever way an operation is evaluated: each is pinned
/// here, ignored until it is fixed.
/// </summary>
/// <remarks>
/// A LongLong is converted through a Double: one past 2^53 loses its low digits, the Double nearest LongLong's maximum is 2^63, which is no LongLong
/// and throws, and a fraction is rounded to an Integer's representation before it is made a LongLong.
/// </remarks>
[TestClass]
public sealed class KnownGapsFoundByPlanExecutionTests
{
    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    // MS-VBAL 5.5.1.2.1: a value within the range of the destination type converts to a copy of the value.
    [TestMethod]
    [Ignore("Known gap: CLngLng of the largest LongLong throws OverflowException in the host (VBLongLongType.CreateValue of 2^63).")]
    public async Task TheLargestLongLong_IsConvertedToALongLong()
        => CollectionAssert.AreEqual(new[] { "9223372036854775807" }, await Run(
            "Debug.Print CLngLng(9223372036854775807^)"));

    [TestMethod]
    [Ignore("Known gap: a LongLong past 2^53 assigned to a LongLong prints as 9.00719925474099E+15 (converted through a Double).")]
    public async Task ALongLong_PastTheIntegersADoubleHolds_IsAssignedAsItIs()
        => CollectionAssert.AreEqual(new[] { "9007199254740993" }, await Run(
            "Dim L As LongLong",
            "L = 9007199254740993^",
            "Debug.Print L"));

    // MS-VBAL 5.5.1.2.1.1: a floating-point value let-coerced to an integral type is rounded to the nearest integer, in that type.
    [TestMethod]
    [Ignore("Known gap: VBNumericType.BankersRounding rounds to an int, so a Double past the range of a Long is let-coerced to a wrong LongLong.")]
    public async Task ADouble_PastTheRangeOfALong_IsAssignedToALongLong()
        => CollectionAssert.AreEqual(new[] { "3000000000" }, await Run(
            "Dim L As LongLong",
            "L = 3000000000.5#",
            "Debug.Print L"));

    // MS-VBAL 5.6.9.8: a logical operation of Null and a number is Null, but for the numbers that decide it.
    [TestMethod]
    [Ignore("Known gap: a logical operation of the largest LongLong and Null throws OverflowException in the host (its Null-table result goes through a Double).")]
    public async Task TheLargestLongLong_OrNull_IsNull()
        => CollectionAssert.AreEqual(new[] { "Null" }, await Run(
            "Debug.Print TypeName(9223372036854775807^ Or Null)"));
}
