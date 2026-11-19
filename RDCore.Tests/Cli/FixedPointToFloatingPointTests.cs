using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBA makes a <c>Decimal</c> a <c>Double</c> by rounding twice, and a <c>Single</c> of that <c>Double</c>; it makes a <c>Currency</c> a <c>Double</c> or
/// a <c>Single</c> by rounding once, and compares a <c>Decimal</c> or a <c>Currency</c> with a <c>Double</c> as a <c>Decimal</c> or a <c>Currency</c>. A
/// <c>Decimal</c> with decimal places made a whole number is its <c>Double</c>, rounded. Each floating-point result is printed as its bits. Every expected
/// line was recorded in Excel.
/// </summary>
[TestClass]
public sealed class FixedPointToFloatingPointTests
{
    private static async Task<string> Run(string body)
    {
        string[] lines =
        [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private Type TD", "    v As Double", "End Type",
            "Private Type TL", "    v As LongLong", "End Type",
            "Private Type TS", "    v As Single", "End Type",
            "Private Type TI", "    v As Long", "End Type",
            "Private acc As String",
            "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
            "Private Function H8(ByVal d As Double) As String", "    Dim a As TD, b As TL", "    a.v = d", "    LSet b = a", "    H8 = Right$(\"0000000000000000\" & Hex$(b.v), 16)", "End Function",
            "Private Function H4(ByVal s As Single) As String", "    Dim a As TS, b As TI", "    a.v = s", "    LSet b = a", "    H4 = Right$(\"00000000\" & Hex$(b.v), 8)", "End Function",
            "Private Function D8(ByVal h As String) As Double", "    Dim a As TD, b As TL", "    b.v = CLngLng(\"&H\" & h)", "    LSet a = b", "    D8 = a.v", "End Function",
            "Public Sub Main()",
            "    Dim x As Double, f As Single, v As Variant",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "    Debug.Print acc",
            "End Sub",
        ];
        return string.Join("/", await RunAsync([], string.Join("\r\n", lines) + "\r\n"));
    }

    // the Double nearest each Decimal is the one next to the expected Double; a Let to a Double and a Variant passed ByVal As Double make the same one.
    [TestMethod]
    [DataRow("0.000708160857053799161669089", "3F47347BDC554EE4", DisplayName = "D01")]
    [DataRow("10.05816778433631985469989", "40241DC82AF6CF81", DisplayName = "D03")]
    [DataRow("327655774543766665.222526", "43923044C3892593", DisplayName = "D06")]
    [DataRow("48524.1786737239221237754", "40E7B185B7B1F51C", DisplayName = "D10")]
    public async Task ADecimalMadeADouble_IsRoundedTwice(string value, string bits)
        => Assert.AreEqual($"{bits}|{bits}|{bits}|", await Run($"x = CDec(\"{value}\") ;; v = CDec(\"{value}\") ;; P H8(CDbl(CDec(\"{value}\"))) ;; P H8(x) ;; P H8(v)"));

    [TestMethod]
    [DataRow("1.000000059604644775390626", "3F800000", DisplayName = "S01")]
    [DataRow("16777217.000000000000000001", "4B800000", DisplayName = "S05")]
    [DataRow("-1.000000059604644775390626", "BF800000", DisplayName = "S07")]
    public async Task ADecimalMadeASingle_IsTheSingleOfItsDouble(string value, string bits)
        => Assert.AreEqual($"{bits}|{bits}|", await Run($"f = CDec(\"{value}\") ;; P H4(CSng(CDec(\"{value}\"))) ;; P H4(f)"));

    [TestMethod]
    [DataRow("75299393168858.7426", "42D11F00653976B0", DisplayName = "C01")]
    [DataRow("507673376957223.0605", "42FCDB9EB4BA3271", DisplayName = "C03")]
    [DataRow("-922337203685477.5808", "C30A36E2EB1C432D", DisplayName = "E08")]
    public async Task ACurrencyMadeADouble_IsTheNearest(string value, string bits)
        => Assert.AreEqual($"{bits}|{bits}|{bits}|", await Run($"x = CCur(\"{value}\") ;; v = CCur(\"{value}\") ;; P H8(CDbl(CCur(\"{value}\"))) ;; P H8(x) ;; P H8(v)"));

    [TestMethod]
    public async Task ACurrencyMadeASingle_IsTheNearest()
        => Assert.AreEqual("57800001|57800001|57800000|", await Run(
            "P H4(CSng(CCur(\"281474993487872.0001\"))) ;; P H4(CSng(CCur(\"281475027042303.9999\"))) ;; P H4(CSng(CDbl(CCur(\"281474993487872.0001\"))))"));

    [TestMethod]
    public async Task ADecimalRaisedToAPower_IsItsDoubleRaised()
        => Assert.AreEqual("3F47347BDC554EE4|Double|", await Run(
            "P H8(CDec(\"0.000708160857053799161669089\") ^ 1) ;; P TypeName(CDec(\"0.000708160857053799161669089\") ^ 1)"));

    // a Decimal with decimal places is the whole number of its Double, rounded; one without them, and a Currency, are rounded as they are.
    [TestMethod]
    [DataRow("P CLng(CDec(\"2.50000000000000000000000001\")) ;; P CLng(CDec(\"3.49999999999999999999999999\"))", "2|3|", DisplayName = "E09, E13")]
    [DataRow("P CInt(CDec(\"2.50000000000000000000000001\")) ;; P CByte(CDec(\"2.50000000000000000000000001\")) ;; P CLngLng(CDec(\"2.50000000000000000000000001\"))",
        "2|2|2|", DisplayName = "E13")]
    [DataRow("P CLngLng(CDec(\"1234567890123456789\")) ;; P CLngLng(CDec(\"1234567890123456789.4\")) ;; P CLngLng(CDec(\"-1234567890123456789.4\"))",
        "1234567890123456789|1234567890123457024|-1234567890123457024|", DisplayName = "E14")]
    [DataRow("P CLngLng(CCur(\"900719925474099.4999\")) ;; P CLngLng(CCur(\"900719925474098.5001\")) ;; P CLng(CCur(\"2.5\"))",
        "900719925474099|900719925474099|2|", DisplayName = "E15")]
    [DataRow("P Not CDec(\"2.50000000000000000000000001\") ;; P CDec(\"2.50000000000000000000000001\") And 7 ;; P CDec(\"3.49999999999999999999999999\") Or 0",
        "-3|2|3|", DisplayName = "E16")]
    [DataRow("P CLng(CDec(\"2147483647.49999999999999999\")) ;; P Err.Number", "2147483647|0|", DisplayName = "E17")]
    public async Task ADecimalMadeAWholeNumber_IsItsDoubleRounded(string body, string printed)
        => Assert.AreEqual(printed, await Run(body));

    [TestMethod]
    public async Task ADecimalComparedWithADouble_IsComparedAsADecimal()
        => Assert.AreEqual("False|True|True|False|False|True|True|True|", await Run(
            "P (CDec(\"1.00000000000000001\") = 1#) ;; P (CDec(\"1.00000000000000001\") > 1#) ;; P (CVar(CDec(\"1.00000000000000001\")) > CVar(1#))"
            + " ;; P (CDec(\"0.000708160857053799161669089\") = D8(\"3F47347BDC554EE4\")) ;; P (CDec(\"0.000708160857053799161669089\") = D8(\"3F47347BDC554EE3\"))"
            + " ;; P (CDec(\"0.000708160857053799161669089\") > D8(\"3F47347BDC554EE3\")) ;; P (CDec(\"0.1\") = 0.1#) ;; P (CVar(CDec(\"0.1\")) = CVar(0.1#))"));

    [TestMethod]
    public async Task ACurrencyComparedWithADouble_IsComparedAsACurrency()
        => Assert.AreEqual("True|False|True|True|False|True|", await Run(
            "P (CCur(\"0.0001\") = 0.00011#) ;; P (CCur(\"0.0001\") < 0.00011#) ;; P (CVar(CCur(\"0.0001\")) = CVar(0.00011#))"
            + " ;; P (CCur(\"0.0001\") = 0.0001#) ;; P (CCur(\"1\") = 1.00005#) ;; P (CCur(\"1\") = 1.000049#)"));
}
