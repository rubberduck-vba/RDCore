using static RDCore.Tests.Runtime.StdLib.Information.InformationWorkspace;

namespace RDCore.Tests.Runtime.StdLib.Information;

/// <summary>
/// <strong>MS-VBAL §6.1.2.7.1.10 QBColor</strong>, <strong>§6.1.2.7.1.11 RGB</strong> and <strong>§6.1.2.7.1.1 IMEStatus</strong>.
/// </summary>
/// <remarks>
/// Each expected value is what MS-VBA printed for the same source, in Excel. A color is <c>&amp;HBBGGRR</c>, red in the low byte.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.7 Information")]
public sealed class ColorAndImeFunctionTests
{
    private static async Task<string> Evaluate(string expression)
    {
        var printed = await Run(string.Empty,
            "On Error Resume Next",
            $"Debug.Print {expression}",
            "If Err.Number <> 0 Then Debug.Print \"ERR \" & Err.Number");
        Assert.HasCount(1, printed, string.Join(" | ", printed));
        return printed[0];
    }

    [TestMethod]
    [DataRow(0, "0")]
    [DataRow(1, "8388608")]
    [DataRow(2, "32768")]
    [DataRow(3, "8421376")]
    [DataRow(4, "128")]
    [DataRow(5, "8388736")]
    [DataRow(6, "32896")]
    [DataRow(7, "12632256")]
    [DataRow(8, "8421504")]
    [DataRow(9, "16711680")]
    [DataRow(10, "65280")]
    [DataRow(11, "16776960")]
    [DataRow(12, "255")]
    [DataRow(13, "16711935")]
    [DataRow(14, "65535")]
    [DataRow(15, "16777215")]
    public async Task QBColor_IsTheTablesColor(int color, string expected)
        => Assert.AreEqual(expected, await Evaluate($"QBColor({color})"));

    [TestMethod]
    [DataRow("-1", "ERR 5")]
    [DataRow("16", "ERR 5")]
    // the argument is Let-coerced to Integer: it rounds to even, overflows, and refuses Null and text.
    [DataRow("3.5", "128")]
    [DataRow("4.5", "128")]
    [DataRow("\"5\"", "8388736")]
    [DataRow("32768", "ERR 6")]
    [DataRow("Null", "ERR 94")]
    [DataRow("Empty", "0")]
    [DataRow("\"abc\"", "ERR 13")]
    public async Task QBColor_OfAnythingElse(string argument, string expected)
        => Assert.AreEqual(expected, await Evaluate($"QBColor({argument})"));

    [TestMethod]
    [DataRow("0, 0, 0", "0")]
    [DataRow("255, 255, 255", "16777215")]
    [DataRow("1, 2, 3", "197121")]
    // a component above 255 counts as 255: the specification's max() means the smaller of the two.
    [DataRow("256, 0, 0", "255")]
    [DataRow("300, 300, 300", "16777215")]
    [DataRow("-1, 0, 0", "ERR 5")]
    [DataRow("0, -1, 0", "ERR 5")]
    [DataRow("0, 0, -1", "ERR 5")]
    [DataRow("1.5, 2.5, 0", "514")]
    [DataRow("32768, 0, 0", "ERR 6")]
    [DataRow("100000, 0, 0", "ERR 6")]
    [DataRow("Null, 0, 0", "ERR 94")]
    [DataRow("Empty, Empty, Empty", "0")]
    [DataRow("\"10\", 0, 0", "10")]
    public async Task RGB_IsBlueGreenRed(string arguments, string expected)
        => Assert.AreEqual(expected, await Evaluate($"RGB({arguments})"));

    [TestMethod]
    public async Task IMEStatus_IsNoControl_WhereThereIsNoInputMethodEditor()
        => Assert.AreEqual("0", await Evaluate("IMEStatus"));
}
