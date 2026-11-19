using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The <c>^</c> operator is compiled (MS-VBAL §5.6.9.3.7): its operands let-coerced to <c>Double</c>, the left raised to the power of the right, 1 when both
/// are 0. A power that is an error (0 to a negative power, a negative number to a fraction, one past the range of a <c>Double</c>) is the interpreter's,
/// which raises it. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledPowerTests
{
    // Longs, a negative exponent, a Double base, and 0 ^ 0: 30 + 0.9375 + 7.5, then 1.
    [TestMethod]
    public async Task APower_IsCompiled()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Powers(4); Powers(0)",
            "End Sub",
            "Private Function Powers(ByVal N As Long) As Double",
            "    Dim I As Long, S As Double, D As Double, Z As Double",
            "    For I = 1 To N",
            "        D = I / 2",
            "        S = S + I ^ 2 + 2 ^ -I + D ^ 2",
            "    Next",
            "    If N = 0 Then S = Z ^ Z",
            "    Powers = S",
            "End Function"), [], "| 38.4375  1 |", ("Powers", 0));

    // 0 to a negative power is error 5 (MS-VBAL §5.6.9.3.7); a negative number to a fraction, which has no real power, is error 5 in MS-VBA; and a power
    // past the range of a Double is error 6 (MS-VBAL §5.6.9.3). The interpreter is given the statement, and raises it.
    [TestMethod]
    [DataRow("0", "-1", "| 5 |", DisplayName = "0 ^ -1")]
    [DataRow("-8", "1 / 3", "| 5 |", DisplayName = "-8 ^ (1 / 3)")]
    [DataRow("10", "400", "| 6 |", DisplayName = "10 ^ 400")]
    public async Task APowerThatIsAnError_IsRaisedByTheInterpreter(string left, string right, string expected)
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer()",
            "End Sub",
            "Private Function Outer() As Long",
            "    Dim B As Double, E As Double, R As Double",
            $"    B = {left}",
            $"    E = {right}",
            "    On Error Resume Next",
            "    R = B ^ E",
            "    Outer = Err.Number",
            "End Function"), [], expected, ("Outer", 1));
}
