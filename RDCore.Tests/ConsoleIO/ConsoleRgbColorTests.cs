using RDCore.SDK.ConsoleIO;

namespace RDCore.Tests.ConsoleIO;

/// <summary>
/// <see cref="ConsoleRgbColor"/> is what carries a theme's 24-bit shell colours to the console shell
/// frame, and what degrades them for a terminal that only has the legacy 16.
/// </summary>
[TestClass]
public sealed class ConsoleRgbColorTests
{
    [TestMethod]
    [DataRow("#0c0a50", (byte)0x0c, (byte)0x0a, (byte)0x50)]
    [DataRow("0c0a50", (byte)0x0c, (byte)0x0a, (byte)0x50)]
    [DataRow("#FFFFFF", (byte)255, (byte)255, (byte)255)]
    [DataRow("  #012345  ", (byte)0x01, (byte)0x23, (byte)0x45)]
    public void TryParse_SixDigitHex_ParsesEachChannel(string token, byte r, byte g, byte b)
    {
        Assert.IsTrue(ConsoleRgbColor.TryParse(token, out var color));
        Assert.AreEqual(new ConsoleRgbColor(r, g, b), color);
    }

    [TestMethod]
    public void TryParse_ThreeDigitHex_DoublesEachDigit()
    {
        Assert.IsTrue(ConsoleRgbColor.TryParse("#0af", out var color));
        Assert.AreEqual(new ConsoleRgbColor(0x00, 0xaa, 0xff), color);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("blue")]
    [DataRow("#12345")]
    [DataRow("#gggggg")]
    [DataRow(null)]
    public void TryParse_NotAHexLiteral_FailsAndYieldsBlack(string? token)
    {
        Assert.IsFalse(ConsoleRgbColor.TryParse(token, out var color));
        Assert.AreEqual(ConsoleRgbColor.Black, color);
    }

    [TestMethod]
    public void ToNearestConsoleColor_TheC64ShellBlue_IsDarkBlue()
        => Assert.AreEqual(ConsoleColor.DarkBlue, new ConsoleRgbColor(0x0c, 0x0a, 0x50).ToNearestConsoleColor());

    [TestMethod]
    [DataRow((byte)0, (byte)0, (byte)0, ConsoleColor.Black)]
    [DataRow((byte)255, (byte)255, (byte)255, ConsoleColor.White)]
    [DataRow((byte)0xc0, (byte)0xc0, (byte)0xc0, ConsoleColor.Gray)]
    [DataRow((byte)0xfd, (byte)0xbf, (byte)0x00, ConsoleColor.Yellow)]
    public void ToNearestConsoleColor_PicksTheClosestPaletteEntry(byte r, byte g, byte b, ConsoleColor expected)
        => Assert.AreEqual(expected, new ConsoleRgbColor(r, g, b).ToNearestConsoleColor());

    [TestMethod]
    public void ToString_IsTheHexLiteralItParsedFrom()
        => Assert.AreEqual("#0c0a50", new ConsoleRgbColor(0x0c, 0x0a, 0x50).ToString());
}
