using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL 5.5.1.2.1: a whole number within the range of a numeric type converts to a copy of the value, and a fixed-point one to the value it is - neither
/// through a Double, which holds neither every LongLong nor every Currency or Decimal, and whose value nearest a bound of their ranges is past it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2.1 Let-coercion between numeric types")]
public sealed class WideNumericConversionTests
{
    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task TheLargestLongLong_IsConvertedToALongLong()
        => CollectionAssert.AreEqual(new[] { "9223372036854775807", "-9223372036854775808" }, await Run(
            "Debug.Print CLngLng(9223372036854775807^)",
            "Debug.Print CLngLng(-9223372036854775807^ - 1)"));

    [TestMethod]
    public async Task ALongLong_PastTheIntegersADoubleHolds_IsAssignedAsItIs()
        => CollectionAssert.AreEqual(new[] { "9007199254740993" }, await Run(
            "Dim L As LongLong",
            "L = 9007199254740993^",
            "Debug.Print L"));

    // MS-VBAL 5.5.1.2.1.1: a floating-point value let-coerced to an integral type is rounded to the nearest integer, in that type.
    [TestMethod]
    public async Task ADouble_PastTheRangeOfALong_IsAssignedToALongLong()
        => CollectionAssert.AreEqual(new[] { "3000000000", "3000000002" }, await Run(
            "Dim L As LongLong",
            "L = 3000000000.5#",
            "Debug.Print L",
            "L = 3000000001.5#",
            "Debug.Print L"));

    // MS-VBAL 5.6.9.8.3: an integral value Or Null is the integral value.
    [TestMethod]
    public async Task TheLargestLongLong_OrNull_IsTheLongLong()
        => CollectionAssert.AreEqual(new[] { "9223372036854775807" }, await Run(
            "Debug.Print 9223372036854775807^ Or Null"));

    [TestMethod]
    public async Task AString_OfTheLargestLongLong_IsConvertedToALongLong()
        => CollectionAssert.AreEqual(new[] { "9223372036854775807" }, await Run(
            "Debug.Print CLngLng(\"9223372036854775807\")"));

    // the Double nearest the largest LongLong is 2^63, which is past it: an overflow, and not an exception of the host.
    [TestMethod]
    public async Task TheDoubleNearestTheLargestLongLong_IsAnOverflow()
        => CollectionAssert.AreEqual(new[] { "6" }, await Run(
            "On Error Resume Next",
            "Debug.Print CLngLng(9223372036854775807#)",
            "Debug.Print Err.Number"));

    [TestMethod]
    public async Task TheDoubleNearestTheLargestCurrency_IsAnOverflow()
        => CollectionAssert.AreEqual(new[] { "6" }, await Run(
            "On Error Resume Next",
            "Debug.Print CCur(922337203685477.5807#)",
            "Debug.Print Err.Number"));

    // what is written of it is 15 digits at most (MS-VBAL 5.5.1.2.4): its fraction is what is left of it once its integer part is taken away.
    [TestMethod]
    public async Task AString_OfTheLargestCurrency_IsConvertedToACurrency()
        => CollectionAssert.AreEqual(new[] { "0.5807" }, await Run(
            "Debug.Print CCur(\"922337203685477.5807\") - CCur(\"922337203685477\")"));

    // MS-VBAL 5.5.1.2.4: an integral or fixed-point value is written in normal notation, whatever the number of digits of its integer part.
    [TestMethod]
    public async Task ADecimal_WithMoreThan15IntegerDigits_IsWrittenInNormalNotation()
        => CollectionAssert.AreEqual(new[] { "79228162514264337593543950335", "-12345678901234567890" }, await Run(
            "Debug.Print CDec(\"79228162514264337593543950335\")",
            "Debug.Print CDec(\"-12345678901234567890\")"));

    [TestMethod]
    public async Task ADecimal_IsConvertedToALongLong_AsItIs()
        => CollectionAssert.AreEqual(new[] { "9223372036854775807" }, await Run(
            "Debug.Print CLngLng(CDec(\"9223372036854775807\"))"));
}
