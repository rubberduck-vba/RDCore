using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBA makes a <c>Single</c> a <c>Decimal</c> as its value to 7 significant digits, ties to even - <c>CDec</c>, and a <c>Variant</c> operation of a
/// <c>Decimal</c> and a <c>Single</c> alike - where a <c>Double</c> keeps 15 digits and a <c>Currency</c> the exact value. Every expected line was observed
/// in Excel.
/// </summary>
[TestClass]
public sealed class SingleToDecimalTests
{
    private static async Task<string> Run(string body)
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
        return string.Join("/", await RunAsync([], string.Join("\r\n", lines) + "\r\n"));
    }

    [TestMethod]
    [DataRow("P CDec(CSng(16777217))", "16777220|", DisplayName = "16777217, to 7 digits")]
    [DataRow("P CDec(CSng(0.1))", "0.1|", DisplayName = "0.1")]
    [DataRow("Dim s As Single ;; s = 1 ;; s = s / 3 ;; P CDec(s)", "0.3333333|", DisplayName = "a third")]
    [DataRow("P CDec(CSng(123456.789))", "123456.8|", DisplayName = "123456.789, to 7 digits")]
    [DataRow("P CDec(CSng(3E+38)) ;; P Err.Number", "6|", DisplayName = "past a Decimal's range, error 6")]
    [DataRow("P CDec(CSng(1E-20)) ;; P Err.Number", "0.00000000000000000001|0|", DisplayName = "1E-20, no error")]
    [DataRow("P CDec(CSng(-2.5))", "-2.5|", DisplayName = "a negative")]
    [DataRow("P CDec(CSng(9999999))", "9999999|", DisplayName = "seven nines")]
    [DataRow("P CDec(CSng(99999999))", "100000000|", DisplayName = "eight nines, rounded up")]
    [DataRow("Dim v As Variant ;; v = CSng(0.1) ;; P CDec(v)", "0.1|", DisplayName = "a Variant holding a Single")]
    [DataRow("Dim s As Single ;; s = 0.1 ;; P CDec(0) + s ;; P TypeName(CDec(0) + s)", "0.1|Decimal|", DisplayName = "a Decimal plus a Single")]
    [DataRow("P CCur(CSng(0.1))", "0.1|", DisplayName = "CCur of 0.1")]
    [DataRow("P CCur(CSng(16777217))", "16777216|", DisplayName = "CCur keeps the exact value")]
    [DataRow("Dim s As Single ;; s = 0.1 ;; P CDec(s) * 3", "0.3|", DisplayName = "a Single's Decimal, times 3")]
    [DataRow("P CDec(CSng(1.5E-07))", "0.00000015|", DisplayName = "1.5E-07")]
    [DataRow("P CDec(CSng(12345678))", "12345680|", DisplayName = "12345678, to 7 digits")]
    [DataRow("Dim d As Double ;; d = 0.1 ;; P CDec(d) ;; P CDec(1 / 3) ;; P CDec(16777217.123456789)",
        "0.1|0.333333333333333|16777217.1234568|", DisplayName = "a Double keeps 15 digits")]
    [DataRow("P CDec(CSng(0.000001234567))", "0.000001234567|", DisplayName = "7 digits after leading zeros")]
    [DataRow("P CDec(CDbl(CSng(0.1)))", "0.100000001490116|", DisplayName = "through a Double, 15 digits")]
    [DataRow("Dim d As Double ;; d = 3E+20 ;; P CDec(d)", "300000000000000000000|", DisplayName = "a Double of 3E+20")]
    [DataRow("P CDec(CSng(0.5)) ;; P CDec(CSng(1E+28)) ;; P Err.Number", "0.5|9999999000000000000000000000|0|", DisplayName = "0.5 and 1E+28")]
    [DataRow("P CDec(CSng(2.5E-28)) ;; P CDec(CSng(1E-28))", "0.0000000000000000000000000003|0.0000000000000000000000000001|",
        DisplayName = "the smallest Decimal scale")]
    [DataRow("Dim s As Single ;; s = 16777217 ;; P CDec(s) ;; P CStr(s) ;; P Str(s)", "16777220|1.677722E+07| 1.677722E+07|",
        DisplayName = "CDec, CStr and Str of one Single")]
    [DataRow("Dim s As Single, v As Variant ;; s = 0.1 ;; v = CDec(1) ;; v = v * s ;; P v ;; P TypeName(v)", "0.1|Decimal|",
        DisplayName = "a Variant Decimal times a Single")]
    [DataRow("Dim s As Single, c As Currency ;; s = 0.1 ;; c = s ;; P c", "0.1|", DisplayName = "a Single let into a Currency")]
    [DataRow("Dim s As Single ;; s = 1.25 ;; P CDec(s) ;; s = 1234.5678 ;; P CDec(s)", "1.25|1234.568|", DisplayName = "1.25 and 1234.5678")]
    [DataRow("P CDec(CSng(12345665)) ;; P CDec(CSng(12345675)) ;; P CDec(CSng(-12345665)) ;; P CDec(CSng(12345645)) ;; P CDec(CSng(1234566.5)) ;; P CDec(CSng(1234567.5)) ;; P CDec(CSng(16777215)) ;; P CDec(CSng(0.0001234565))",
        "12345660|12345680|-12345660|12345640|1234566|1234568|16777220|0.0001234565|", DisplayName = "ties to even")]
    public async Task ASingleMadeADecimal_IsItsSevenSignificantDigits(string body, string printed)
        => Assert.AreEqual(printed, await Run(body));
}