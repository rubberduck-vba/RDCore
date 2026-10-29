using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Currency</c> is a 64-bit integer of ten-thousandths: its limits are 922337203685477.5807 and -922337203685477.5808 exactly, and one ten-thousandth past
/// either is error 6, whatever it is made of. A <c>Double</c> or a <c>Single</c> is made one from its exact value, rounded to four decimals, a tie to the even
/// one. Every expected line was recorded in Excel (64-bit VBA7). Each target is given 5 first where it raises, so that one left as it was is told from one
/// assigned.
/// </summary>
[TestClass]
public sealed class CurrencyLimitTests
{
    // the program of a case: P writes what it is given, as a Variant, and what was written is printed at the end.
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
    [DataRow("Dim c As Currency ;; c = CCur(\"922337203685477.5807\") ;; P Err.Number ;; P c", "0|922337203685477.5807|", DisplayName = "the largest, of a String")]
    [DataRow("Dim c As Currency ;; c = 5 ;; c = CCur(\"922337203685477.5808\") ;; P Err.Number ;; P c", "6|5|", DisplayName = "one past the largest, of a String")]
    [DataRow("Dim c As Currency ;; c = CCur(\"-922337203685477.5808\") ;; P Err.Number ;; P c", "0|-922337203685477.5808|", DisplayName = "the smallest, of a String")]
    [DataRow("Dim c As Currency ;; c = 5 ;; c = CCur(\"-922337203685477.5809\") ;; P Err.Number ;; P c", "6|5|", DisplayName = "one past the smallest, of a String")]
    [DataRow("Dim c As Currency ;; c = CDec(\"922337203685477.5807\") ;; P Err.Number ;; P c", "0|922337203685477.5807|", DisplayName = "the largest, of a Decimal")]
    [DataRow("Dim c As Currency ;; c = 5 ;; c = CDec(\"922337203685477.5808\") ;; P Err.Number ;; P c", "6|5|", DisplayName = "one past the largest, of a Decimal")]
    [DataRow("Dim c As Currency ;; c = CDec(\"-922337203685477.5808\") ;; P Err.Number ;; P c", "0|-922337203685477.5808|", DisplayName = "the smallest, of a Decimal")]
    [DataRow("Dim c As Currency ;; c = 5 ;; c = CDec(\"-922337203685477.5809\") ;; P Err.Number ;; P c", "6|5|", DisplayName = "one past the smallest, of a Decimal")]
    [DataRow("Dim c As Currency, d As Currency ;; c = CCur(\"922337203685477.5806\") ;; d = c + 0.0001@ ;; P Err.Number ;; P d", "0|922337203685477.5807|",
        DisplayName = "the largest, of a sum")]
    [DataRow("Dim c As Currency, d As Currency ;; c = CCur(\"922337203685477.5807\") ;; d = 5 ;; d = c + 0.0001@ ;; P Err.Number ;; P d", "6|5|",
        DisplayName = "one past the largest, of a sum")]
    [DataRow("Dim c As Currency, d As Currency ;; c = CCur(\"-922337203685477.5808\") ;; d = 5 ;; d = c - 0.0001@ ;; P Err.Number ;; P d", "6|5|",
        DisplayName = "one past the smallest, of a difference")]
    [DataRow("Dim c As Currency ;; c = 922337203685477.5807@ ;; P Err.Number ;; P c", "0|922337203685477.5807|", DisplayName = "the largest, a literal")]
    [DataRow("Dim v As Variant ;; v = CCur(\"922337203685477.5807\") ;; P Err.Number ;; P v ;; P TypeName(v)", "0|922337203685477.5807|Currency|",
        DisplayName = "the largest, in a Variant")]
    [DataRow("Dim s As String ;; s = CStr(CCur(\"922337203685477.5807\")) ;; P s ;; s = CStr(CCur(\"-922337203685477.5808\")) ;; P s",
        "922337203685477.5807|-922337203685477.5808|", DisplayName = "the limits, as text")]
    public void ACurrencyAtItsLimit_IsOne_AndOneTenThousandthPast_IsAnOverflow(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    [TestMethod]
    [DataRow("Dim c As Currency, d As Double ;; d = 123456789012.3456 ;; c = d ;; P Err.Number ;; P c", "0|123456789012.3456|", DisplayName = "more digits than 15")]
    [DataRow("Dim c As Currency, d As Double ;; d = 922337203685477.5 ;; c = d ;; P Err.Number ;; P c", "0|922337203685477.5|", DisplayName = "below the largest")]
    [DataRow("Dim c As Currency, d As Double ;; d = -922337203685477.5 ;; c = d ;; P Err.Number ;; P c", "0|-922337203685477.5|", DisplayName = "above the smallest")]
    [DataRow("Dim c As Currency, d As Double ;; d = 922337203685477.625 ;; c = 5 ;; c = d ;; P Err.Number ;; P c", "6|5|", DisplayName = "the next Double past the largest")]
    [DataRow("Dim c As Currency, d As Double ;; d = 922337203685477# ;; c = d ;; P Err.Number ;; P c", "0|922337203685477|", DisplayName = "a whole number below the largest")]
    [DataRow("Dim c As Currency, d As Double ;; d = 922337203685478# ;; c = 5 ;; c = d ;; P Err.Number ;; P c", "6|5|", DisplayName = "a whole number past the largest")]
    [DataRow("Dim c As Currency, d As Double ;; d = 0.00005 ;; c = d ;; P Err.Number ;; P c", "0|0.0001|", DisplayName = "0.00005, a little above half")]
    [DataRow("Dim c As Currency, d As Double ;; d = 0.00015 ;; c = d ;; P Err.Number ;; P c", "0|0.0001|", DisplayName = "0.00015, a little below half")]
    [DataRow("Dim c As Currency, d As Double ;; d = 1234567890.12345 ;; c = d ;; P Err.Number ;; P c", "0|1234567890.1235|", DisplayName = "a fifth decimal of 5, a little above")]
    [DataRow("Dim c As Currency, d As Double ;; d = 99999999999.99995 ;; c = d ;; P Err.Number ;; P c", "0|100000000000|", DisplayName = "rounded up to a whole number")]
    [DataRow("Dim c As Currency ;; c = CCur(123456789012.3456#) ;; P Err.Number ;; P c", "0|123456789012.3456|", DisplayName = "CCur of a Double")]
    [DataRow("Dim c As Currency, s As Single ;; s = 123456.7 ;; c = s ;; P Err.Number ;; P c", "0|123456.7031|", DisplayName = "a Single, by its exact value")]
    [DataRow("Dim c As Currency, d As Double ;; d = 1.03125 ;; c = d ;; P c ;; d = 2.15625 ;; c = d ;; P c ;; d = -1.03125 ;; c = d ;; P c ;; d = 1.09375 ;; c = d ;; P c ;; P CCur(1.03125)",
        "1.0312|2.1562|-1.0312|1.0938|1.0312|", DisplayName = "an exact tie, to the even one")]
    [DataRow("Dim c As Currency, s As Single ;; s = 1.03125 ;; c = s ;; P c ;; s = 2.15625 ;; c = s ;; P c", "1.0312|2.1562|", DisplayName = "an exact tie of a Single, to the even one")]
    public void ACurrencyMadeOfAFloatingPointNumber_IsItsExactValue_RoundedToFourDecimals(string body, string printed)
        => Assert.AreEqual(printed, Run(body));

    [TestMethod]
    public void TheTypesLimits_AreExact()
    {
        Assert.AreEqual(922337203685477.5807m, Convert.ToDecimal(RDCore.SDK.Model.Types.VBCurrencyType.MaxValue.Handle.Value.BoxedValue));
        Assert.AreEqual(-922337203685477.5808m, Convert.ToDecimal(RDCore.SDK.Model.Types.VBCurrencyType.MinValue.Handle.Value.BoxedValue));
    }
}
