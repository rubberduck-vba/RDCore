using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledModuleFieldTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// An operation of two <c>Variant</c> operands in compiled code, of any two of their numeric subtypes, a <c>Boolean</c> and a <c>Date</c> among them: the
/// operands let-coerced to the type the operation is done in and the operation done there (MS-VBAL §5.6.9), an arithmetic operator (§5.6.9.3), a comparison
/// (§5.6.9.5) or a logical operator (§5.6.9.8), as the interpreter does it. One whose result does not fit its type is the interpreter's, which widens it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.9 Operator Expressions")]
public sealed class CompiledVariantOperatorTests
{
    // a Byte, an Integer, a Long, a Single, a Double, a Currency, a Date, a Boolean and another Long, none zero, each the left and the right operand of each
    // operator: every pair of their subtypes, once each.
    private static readonly string[] Values =
    [
        "Public Sub Main()",
        "    Dim V(1 To 9) As Variant",
        "    V(1) = CByte(7): V(2) = CInt(-300): V(3) = CLng(70000): V(4) = CSng(2.5): V(5) = CDbl(-1.25)",
        "    V(6) = CCur(3.5): V(7) = CDate(2): V(8) = True: V(9) = CLng(3)",
        "    Debug.Print Arithmetic(V); Compared(V); Bits(V)",
        "End Sub",
    ];

    // + - * / \ Mod of each pair: a result that does not fit its subtype (an Integer's -300 * -300, a Long's 70000 * 70000) is the interpreter's, which
    // widens it. The 138 recoveries are the operations compiled code leaves to the interpreter.
    [TestMethod]
    public async Task TheArithmeticOfTwoVariants_IsDoneInTheTypeOfTheirSubtypes()
        => await AssertAlikeAsync(Program([.. Values,
            "Private Function Arithmetic(V() As Variant) As Double",
            "    Dim I As Long, J As Long, R As Variant",
            "    For I = 1 To 9",
            "        For J = 1 To 9",
            "            R = V(I) + V(J)",
            "            Arithmetic = Arithmetic * 0.5 + R",
            "            R = V(I) - V(J)",
            "            Arithmetic = Arithmetic * 0.5 + R",
            "            R = V(I) * V(J)",
            "            Arithmetic = Arithmetic * 0.5 + R",
            "            R = V(I) / V(J)",
            "            Arithmetic = Arithmetic * 0.5 + R",
            "            R = V(I) \\ V(J)",
            "            Arithmetic = Arithmetic * 0.5 + R",
            "            R = V(I) Mod V(J)",
            "            Arithmetic = Arithmetic * 0.5 + R",
            "        Next",
            "    Next",
            "End Function",
            "Private Function Compared(V() As Variant) As Long",
            "End Function",
            "Private Function Bits(V() As Variant) As Long",
            "End Function"]),
            [("Arithmetic", 138)]);

    // = <> < <= > >= of each pair, each a Boolean: compared in the type of their subtypes. The 24 recoveries are the comparisons compiled code leaves to
    // the interpreter. A Single and a Double are compared as Singles (MS-VBAL §5.6.9.5), the Double made the nearest Single, which compiled code does.
    [TestMethod]
    public async Task AComparisonOfTwoVariants_IsDoneInTheTypeOfTheirSubtypes()
        => await AssertAlikeAsync(Program([.. Values,
            "Private Function Arithmetic(V() As Variant) As Double",
            "End Function",
            "Private Function Compared(V() As Variant) As Long",
            "    Dim I As Long, J As Long",
            "    For I = 1 To 9",
            "        For J = 1 To 9",
            "            Compared = (Compared * 7 + (V(I) = V(J))) Mod 1000003",
            "            Compared = (Compared * 7 + (V(I) <> V(J))) Mod 1000003",
            "            Compared = (Compared * 7 + (V(I) < V(J))) Mod 1000003",
            "            Compared = (Compared * 7 + (V(I) <= V(J))) Mod 1000003",
            "            Compared = (Compared * 7 + (V(I) > V(J))) Mod 1000003",
            "            Compared = (Compared * 7 + (V(I) >= V(J))) Mod 1000003",
            "        Next",
            "    Next",
            "End Function",
            "Private Function Bits(V() As Variant) As Long",
            "End Function"]),
            [("Compared", 24)]);

    // And Or Xor Eqv Imp of each pair: the bits of each, a Single, a Double, a Currency and a Date rounded to a Long first. The 160 recoveries are the
    // logical operations compiled code leaves to the interpreter.
    [TestMethod]
    public async Task ALogicalOperationOfTwoVariants_IsDoneInTheTypeOfTheirSubtypes()
        => await AssertAlikeAsync(Program([.. Values,
            "Private Function Arithmetic(V() As Variant) As Double",
            "End Function",
            "Private Function Compared(V() As Variant) As Long",
            "End Function",
            "Private Function Bits(V() As Variant) As Long",
            "    Dim I As Long, J As Long",
            "    For I = 1 To 9",
            "        For J = 1 To 9",
            "            Bits = (Bits * 5 + (V(I) And V(J))) Mod 1000003",
            "            Bits = (Bits * 5 + (V(I) Or V(J))) Mod 1000003",
            "            Bits = (Bits * 5 + (V(I) Xor V(J))) Mod 1000003",
            "            Bits = (Bits * 5 + (V(I) Eqv V(J))) Mod 1000003",
            "            Bits = (Bits * 5 + (V(I) Imp V(J))) Mod 1000003",
            "        Next",
            "    Next",
            "End Function"]),
            [("Bits", 160)]);
}
