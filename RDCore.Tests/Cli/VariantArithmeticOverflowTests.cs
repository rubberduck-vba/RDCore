using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL 5.6.9.3: an arithmetic operation with an operand declared Variant that overflows an Integer or Long effective type is evaluated again, in the
/// narrowest of Integer, Long and Double that holds its value; without a Variant operand, and in any other effective type, the overflow is error 6.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators")]
public sealed class VariantArithmeticOverflowTests
{
    private static Task<string[]> RunMainAsync(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    // what lets a Variant accumulate past 2147483647.
    [TestMethod]
    public async Task AVariantLong_ThatOverflows_WidensToADouble()
        => CollectionAssert.AreEqual(new[] { "Double", "2147483648" }, await RunMainAsync(
            "Dim S As Variant",
            "S = 2147483647",
            "S = S + 1",
            "Debug.Print TypeName(S)",
            "Debug.Print S"));

    [TestMethod]
    public async Task AVariantInteger_ThatOverflows_WidensToALong()
        => CollectionAssert.AreEqual(new[] { "Long", "90000" }, await RunMainAsync(
            "Dim V As Variant",
            "V = 300",
            "Debug.Print TypeName(V * 300)",
            "Debug.Print V * 300"));

    [TestMethod]
    public async Task TheNegation_OfAVariantInteger_ThatOverflows_WidensToALong()
        => CollectionAssert.AreEqual(new[] { "Long", "32768" }, await RunMainAsync(
            "Dim V As Variant",
            "V = CInt(-32768)",
            "Debug.Print TypeName(-V)",
            "Debug.Print -V"));

    [TestMethod]
    public async Task ALong_ThatOverflows_WithNoVariantOperand_IsAnOverflow()
        => CollectionAssert.AreEqual(new[] { "6" }, await RunMainAsync(
            "Dim L As Long",
            "L = 2147483647",
            "On Error Resume Next",
            "Debug.Print L + 1",
            "Debug.Print Err.Number"));

    [TestMethod]
    public async Task AVariantLongLong_ThatOverflows_IsAnOverflow()
        => CollectionAssert.AreEqual(new[] { "6" }, await RunMainAsync(
            "Dim V As Variant",
            "V = 4611686018427387904^",
            "On Error Resume Next",
            "Debug.Print V * 2",
            "Debug.Print Err.Number"));
}
