using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> holding a number, a <c>Date</c> or a <c>Boolean</c>, compared with a <c>String</c> that is no <c>Variant</c>, is compared as text: MS-VBA
/// let-coerces the <c>Variant</c>'s value to <c>String</c>, as Microsoft Learn documents it, where <strong>MS-VBAL §5.6.9.5</strong>'s table makes the
/// effective type the number's - a type mismatch (13) when the <c>String</c> is no number. Every row was observed in Excel.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.9.5 Relational Operators")]
public sealed class VariantComparedWithStringTests
{
    private static Task<string[]> RunAsync(params string[] body)
        => ModuleWorkspace.RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body)}\r\nDebug.Print Err.Number\r\nEnd Sub\r\n");

    [TestMethod]
    [DataRow(new[] { "Dim v As Variant: v = CCur(0)", "Dim s As String: s = \".\"", "Debug.Print v = s" }, new[] { "False", "0" }, DisplayName = "V01 a Currency and a separator")]
    [DataRow(new[] { "Dim v As Variant: v = CCur(0)", "Debug.Print v = \".\"" }, new[] { "False", "0" }, DisplayName = "V02 a literal")]
    [DataRow(new[] { "Dim v As Variant: v = CCur(5)", "Dim s As String: s = \".\"", "Debug.Print v < s", "Debug.Print v > s" }, new[] { "False", "True", "0" },
        DisplayName = "V03 \"5\" is after \".\"")]
    [DataRow(new[] { "Dim v As Variant: v = 0", "Dim s As String: s = \"x\"", "Debug.Print v = s", "Debug.Print v <> s" }, new[] { "False", "True", "0" },
        DisplayName = "V04 an Integer and a word")]
    [DataRow(new[] { "Dim v As Variant: v = CCur(10)", "Dim s As String: s = \"9\"", "Debug.Print v > s", "Debug.Print v < s" }, new[] { "False", "True", "0" },
        DisplayName = "W01 \"10\" is before \"9\", a number though it is")]
    [DataRow(new[] { "Dim v As Variant: v = 10", "Dim s As String: s = \"9x\"", "Debug.Print v > s", "Debug.Print v < s" }, new[] { "False", "True", "0" },
        DisplayName = "W04 and before \"9x\"")]
    [DataRow(new[] { "Dim v As Variant: v = 1.5", "Dim s As String: s = \"1.5\"", "Debug.Print v = s" }, new[] { "True", "0" }, DisplayName = "W06 the same text")]
    [DataRow(new[] { "Dim v As Variant: v = 10", "Dim s As String: s = \"\"", "Debug.Print v > s", "Debug.Print v = s" }, new[] { "True", "False", "0" },
        DisplayName = "W07 after an empty String")]
    [DataRow(new[] { "Dim v As Variant: v = #1/2/2020#", "Dim s As String: s = \"x\"", "Debug.Print v = s", "Debug.Print v < s" }, new[] { "False", "True", "0" },
        DisplayName = "W08 a Date")]
    [DataRow(new[] { "Dim v As Variant: v = True", "Dim s As String: s = \"x\"", "Debug.Print v = s" }, new[] { "False", "0" }, DisplayName = "W10 a Boolean")]
    public async Task AVariantComparedWithAString_IsComparedAsText(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    // what the specification says still holds where neither operand is a Variant, and between two Variants.
    [TestMethod]
    [DataRow(new[] { "Dim c As Currency", "Dim s As String: s = \".\"", "Debug.Print c = s" }, new[] { "13" }, DisplayName = "V05 two declared types: a type mismatch")]
    [DataRow(new[] { "Dim s As String: s = \"9x\"", "Dim n As Long: n = 10", "Debug.Print n > s" }, new[] { "13" }, DisplayName = "W09 and a Long")]
    [DataRow(new[] { "Dim v As Variant: v = CCur(0)", "Dim w As Variant: w = \".\"", "Debug.Print v = w" }, new[] { "False", "0" },
        DisplayName = "V06 two Variants: the number is less")]
    [DataRow(new[] { "Dim v As Variant: v = CCur(0)", "Dim s As String: s = \"5\"", "Debug.Print v = s", "Debug.Print v < s" }, new[] { "False", "True", "0" },
        DisplayName = "V08 a number in a String")]
    public async Task AComparisonOfAnotherKind_IsAsSpecified(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    // an arithmetic or & expression with a Variant operand is a Variant, and is compared as one.
    [TestMethod]
    [DataRow(new[] { "Dim v As Variant: v = 1", "Debug.Print (v + 1) = \"2.0\"", "Debug.Print (v + 1) = \"2\"", "Debug.Print \"2.0\" = (v + 1)", "Debug.Print (v + 1) > \"10\"" },
        new[] { "False", "True", "False", "True", "0" }, DisplayName = "G02 v + 1")]
    [DataRow(new[] { "Dim v As Variant: v = 1", "Debug.Print (v * 1) = \"1.0\"", "Debug.Print CInt(v) = \"1.0\"", "Debug.Print (v & \"\") = 1", "Debug.Print -v = \"-1.0\"" },
        new[] { "False", "True", "True", "False", "0" }, DisplayName = "G09 v * 1, CInt(v), v & \"\", -v")]
    [DataRow(new[] { "Dim v As Variant: v = \"2.5\"", "Debug.Print (v & \"\") = 2", "Debug.Print (v + 0) = 2", "Debug.Print Trim(v) = 2", "Debug.Print Trim$(v) = 2" },
        new[] { "False", "False", "False", "False", "0" }, DisplayName = "G10 v & \"\", v + 0, Trim")]
    [DataRow(new[] { "Dim v As Variant: v = \"abc\"", "Debug.Print v = \"abc\"", "v = 1", "Debug.Print v = \"a\"", "Debug.Print v < \"a\"" },
        new[] { "True", "False", "True", "0" }, DisplayName = "G11 text")]
    public async Task AnExpressionOverAVariant_IsComparedAsAVariant(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    // MS-VBA compares a String with a Byte, an Integer or a Long as Doubles, where MS-VBAL's table makes the effective type the number's.
    [TestMethod]
    [DataRow(new[] { "Dim v As Variant: v = \"2.5\"", "Debug.Print v = 2", "Debug.Print 2 = v", "Debug.Print v = 2.5", "Debug.Print v > 2", "Debug.Print v = CLng(2)", "Debug.Print v = 2#" },
        new[] { "False", "False", "True", "True", "False", "False", "0" }, DisplayName = "G01 a Variant \"2.5\"")]
    [DataRow(new[] { "Dim s As String: s = \"2.5\"", "Debug.Print s = 2", "Debug.Print s = 2.5" }, new[] { "False", "True", "0" }, DisplayName = "G03 a String \"2.5\"")]
    [DataRow(new[] { "Dim v As Variant: v = \"2.5\"", "Dim n As Integer: n = 2", "Debug.Print v = n", "Dim d As Double: d = 2.5", "Debug.Print v = d" },
        new[] { "False", "True", "0" }, DisplayName = "G04 an Integer and a Double variable")]
    [DataRow(new[] { "Dim v As Variant: v = \"abc\"", "Debug.Print v = 2", "Debug.Print v > 2" }, new[] { "False", "True", "0" }, DisplayName = "G05 a String that is no number")]
    [DataRow(new[] { "Dim v As Variant: v = \"2\"", "Debug.Print v = 2" }, new[] { "True", "0" }, DisplayName = "G07 a Variant \"2\"")]
    [DataRow(new[] { "Dim v As Variant, w As Variant: v = 2: w = \"2.0\"", "Debug.Print v = w", "Debug.Print v = \"2.0\"", "Debug.Print v > \"10\"", "Debug.Print v > w" },
        new[] { "False", "False", "True", "False", "0" }, DisplayName = "G08 two Variants")]
    [DataRow(new[] { "Dim v As Variant: v = \"2.5\"", "Dim b As Byte: b = 2", "Debug.Print v = b", "Dim c As Currency: c = 2.5", "Debug.Print v = c", "Debug.Print v = True" },
        new[] { "False", "True", "True", "0" }, DisplayName = "G12 a Byte, a Currency, a Boolean")]
    [DataRow(new[] { "Dim v As Variant: v = \"40000\"", "Dim i As Integer: i = 2", "Debug.Print v > i", "Dim s As String: s = \"40000\"", "Debug.Print s > i" },
        new[] { "True", "True", "0" }, DisplayName = "H01 no overflow for the text of a number too large for an Integer")]
    [DataRow(new[] { "Dim v As Variant: v = \"1E3\"", "Debug.Print v = 1000", "Dim s As String: s = \"&H10\"", "Debug.Print s = 16", "Debug.Print s = CLng(16)" },
        new[] { "True", "True", "True", "0" }, DisplayName = "H05 an exponent and a hexadecimal")]
    [DataRow(new[] { "Dim v As Variant: v = \"40000\"", "Dim b As Byte: b = 2", "Debug.Print v > b" }, new[] { "True", "0" }, DisplayName = "H06 nor for a Byte")]
    public async Task AStringComparedWithAnIntegralNumber_IsComparedAsADouble(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    // MS-VBA compares a Variant String with a Single or a LongLong as Doubles too, and a declared String with a Single as a Single.
    [TestMethod]
    [DataRow(new[] { "Dim v As Variant: v = \"0.1\"", "Dim f As Single: f = 0.1", "Debug.Print v = f", "Dim s As String: s = \"0.1\"", "Debug.Print s = f", "Debug.Print s = CSng(0.1)" },
        new[] { "False", "True", "True", "0" }, DisplayName = "H02 a Single")]
    [DataRow(new[] { "Dim v As Variant: v = \"9007199254740993\"", "Dim n As LongLong: n = 9007199254740^ * 1000^ + 992^", "Debug.Print v = n", "Debug.Print v > n" },
        new[] { "True", "False", "0" }, DisplayName = "H03 a LongLong beyond a Double's precision")]
    [DataRow(new[] { "Dim v As Variant: v = \"2.5\"", "Dim n As LongLong: n = 2", "Debug.Print v = n", "Debug.Print v > n" },
        new[] { "False", "True", "0" }, DisplayName = "H04 a LongLong")]
    public async Task AVariantStringComparedWithASingleOrALongLong_IsComparedAsADouble(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    // a Currency and a Decimal keep the table's types: a String is let-coerced to Currency, to 4 decimals, and to Decimal, exact.
    [TestMethod]
    [DataRow(new[] { "Dim v As Variant: v = \"2.00001\"", "Dim c As Currency: c = 2", "Debug.Print v = c", "Debug.Print v > c", "Dim s As String: s = \"2.00001\"", "Debug.Print s = c" },
        new[] { "True", "False", "True", "0" }, DisplayName = "I02 a Currency")]
    [DataRow(new[] { "Dim v As Variant: v = \"2.00001\"", "Debug.Print v = CDec(2)", "Debug.Print v > CDec(2)", "v = \"9007199254740993\"", "Debug.Print v = CDec(\"9007199254740992\")" },
        new[] { "False", "True", "False", "0" }, DisplayName = "I03, I04 a Decimal")]
    public async Task AStringComparedWithACurrencyOrADecimal_IsComparedInItsType(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));
}
