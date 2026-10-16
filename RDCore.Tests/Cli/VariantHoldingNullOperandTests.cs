using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> holding <c>Null</c> is a <c>Null</c> operand, as the <c>Null</c> literal is: the operators' semantics speak of the operand's value
/// (<strong>MS-VBAL §5.6.9.4</strong> Let-coerces only "its non-Null operands"; the logical operators' tables, <strong>§5.6.9.8</strong>, have rows for
/// <c>Null</c>). Found by the golden master of the relational, logical and concatenation operators (<c>OtherOperatorsGoldenMasterTests</c>): the
/// <c>Variant</c> was Let-coerced to the operation's effective type, which raised error 94.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.9 Operators")]
public sealed class VariantHoldingNullOperandTests
{
    private static Task<string[]> RunMainAsync(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    // MS-VBAL 5.6.9.4: Null is concatenated as the zero-length string.
    [TestMethod]
    public async Task AVariantHoldingNull_ConcatenatedToANumber_IsTheNumber()
        => CollectionAssert.AreEqual(new[] { "7" }, await RunMainAsync(
            "Dim V As Variant",
            "V = Null",
            "Debug.Print V & 7"));

    // MS-VBAL 5.6.9.8.2: Null And a number other than 0 is Null.
    [TestMethod]
    public async Task AVariantHoldingNull_AndANumber_IsNull()
        => CollectionAssert.AreEqual(new[] { "True" }, await RunMainAsync(
            "Dim V As Variant",
            "V = Null",
            "Debug.Print IsNull(V And 7)"));

    // each operator, either side: what the Variant gives is what the literal gives - Null, or the value a logical operator's table makes of the other operand.
    [TestMethod]
    [DataRow("&")]
    [DataRow("+")]
    [DataRow("-")]
    [DataRow("*")]
    [DataRow("/")]
    [DataRow("\\")]
    [DataRow("Mod")]
    [DataRow("^")]
    [DataRow("=")]
    [DataRow("<>")]
    [DataRow("<")]
    [DataRow(">=")]
    [DataRow("Like")]
    [DataRow("And")]
    [DataRow("Or")]
    [DataRow("Xor")]
    [DataRow("Eqv")]
    [DataRow("Imp")]
    public async Task AVariantHoldingNull_IsTheNullLiteral_ToEveryBinaryOperator(string @operator)
    {
        static string Shown(string expression) => $"Debug.Print IsNull({expression}) & \" \" & ({expression})";

        var literal = await RunMainAsync(Shown($"Null {@operator} 0"), Shown($"Null {@operator} 7"), Shown($"0 {@operator} Null"), Shown($"7 {@operator} Null"));
        var held = await RunMainAsync(
            "Dim V As Variant",
            "V = Null",
            Shown($"V {@operator} 0"), Shown($"V {@operator} 7"), Shown($"0 {@operator} V"), Shown($"7 {@operator} V"));

        CollectionAssert.AreEqual(literal, held);
    }
}
