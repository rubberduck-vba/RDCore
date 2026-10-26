using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBA makes a <c>Single</c> a <c>Decimal</c> as its value to 7 significant digits, ties to even - <c>CDec</c>, and a <c>Variant</c> operation of a
/// <c>Decimal</c> and a <c>Single</c> alike - where a <c>Double</c> keeps 15 digits and a <c>Currency</c> the exact value. Every expected line was recorded
/// in Excel (grid <c>oracle\s43</c> K01-K26, J01).
/// </summary>
[TestClass]
public sealed class SingleToDecimalTests
{
    private static string Run(string body)
    {
        string[] lines =
        [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private acc As String",
            "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "    Debug.Print acc",
            "End Sub",
        ];
        return string.Join("/", RunAsync([], string.Join("\r\n", lines) + "\r\n").GetAwaiter().GetResult());
    }

    [TestMethod]
    [DataRow("P CDec(CSng(16777217))", "16777220|", DisplayName = "K01")]
    [DataRow("P CDec(CSng(0.1))", "0.1|", DisplayName = "K02")]
    [DataRow("Dim s As Single ;; s = 1 ;; s = s / 3 ;; P CDec(s)", "0.3333333|", DisplayName = "K03")]
    [DataRow("P CDec(CSng(123456.789))", "123456.8|", DisplayName = "K04")]
    [DataRow("P CDec(CSng(3E+38)) ;; P Err.Number", "6|", DisplayName = "K05")]
    [DataRow("P CDec(CSng(1E-20)) ;; P Err.Number", "0.00000000000000000001|0|", DisplayName = "K06")]
    [DataRow("P CDec(CSng(-2.5))", "-2.5|", DisplayName = "K07")]
    [DataRow("P CDec(CSng(9999999))", "9999999|", DisplayName = "K08")]
    [DataRow("P CDec(CSng(99999999))", "100000000|", DisplayName = "K09")]
    [DataRow("Dim v As Variant ;; v = CSng(0.1) ;; P CDec(v)", "0.1|", DisplayName = "K10")]
    [DataRow("Dim s As Single ;; s = 0.1 ;; P CDec(0) + s ;; P TypeName(CDec(0) + s)", "0.1|Decimal|", DisplayName = "K11")]
    [DataRow("P CCur(CSng(0.1))", "0.1|", DisplayName = "K12")]
    [DataRow("P CCur(CSng(16777217))", "16777216|", DisplayName = "K13")]
    [DataRow("Dim s As Single ;; s = 0.1 ;; P CDec(s) * 3", "0.3|", DisplayName = "K14")]
    [DataRow("P CDec(CSng(1.5E-07))", "0.00000015|", DisplayName = "K15")]
    [DataRow("P CDec(CSng(12345678))", "12345680|", DisplayName = "K16")]
    [DataRow("Dim d As Double ;; d = 0.1 ;; P CDec(d) ;; P CDec(1 / 3) ;; P CDec(16777217.123456789)", "0.1|0.333333333333333|16777217.1234568|", DisplayName = "K17")]
    [DataRow("P CDec(CSng(0.000001234567))", "0.000001234567|", DisplayName = "K18")]
    [DataRow("P CDec(CDbl(CSng(0.1)))", "0.100000001490116|", DisplayName = "K19")]
    [DataRow("Dim d As Double ;; d = 3E+20 ;; P CDec(d)", "300000000000000000000|", DisplayName = "K20")]
    [DataRow("P CDec(CSng(0.5)) ;; P CDec(CSng(1E+28)) ;; P Err.Number", "0.5|9999999000000000000000000000|0|", DisplayName = "K21")]
    [DataRow("P CDec(CSng(2.5E-28)) ;; P CDec(CSng(1E-28))", "0.0000000000000000000000000003|0.0000000000000000000000000001|", DisplayName = "K22")]
    [DataRow("Dim s As Single ;; s = 16777217 ;; P CDec(s) ;; P CStr(s) ;; P Str(s)", "16777220|1.677722E+07| 1.677722E+07|", DisplayName = "K23")]
    [DataRow("Dim s As Single, v As Variant ;; s = 0.1 ;; v = CDec(1) ;; v = v * s ;; P v ;; P TypeName(v)", "0.1|Decimal|", DisplayName = "K24")]
    [DataRow("Dim s As Single, c As Currency ;; s = 0.1 ;; c = s ;; P c", "0.1|", DisplayName = "K25")]
    [DataRow("Dim s As Single ;; s = 1.25 ;; P CDec(s) ;; s = 1234.5678 ;; P CDec(s)", "1.25|1234.568|", DisplayName = "K26")]
    [DataRow("P CDec(CSng(12345665)) ;; P CDec(CSng(12345675)) ;; P CDec(CSng(-12345665)) ;; P CDec(CSng(12345645)) ;; P CDec(CSng(1234566.5)) ;; P CDec(CSng(1234567.5)) ;; P CDec(CSng(16777215)) ;; P CDec(CSng(0.0001234565))", "12345660|12345680|-12345660|12345640|1234566|1234568|16777220|0.0001234565|", DisplayName = "J01")]
    public void ASingleMadeADecimal_IsItsSevenSignificantDigits(string body, string printed)
        => Assert.AreEqual(printed, Run(body));
}