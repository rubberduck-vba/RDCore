using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// RD-VBAL §5.6.9.5: a <c>Variant</c> holding a <c>String</c>, compared with a number, a <c>Date</c> or a <c>Boolean</c> that is no <c>Variant</c>, is compared
/// as the table says when the <c>String</c> is a value of the effective type, and ranks above it otherwise, raising nothing - where MS-VBAL §5.6.9.5's table
/// makes it a type mismatch (13). Each row is the probe it was recorded with in Excel, under <c>On Error Resume Next</c> (grid <c>oracle\s48</c>).
/// </summary>
[TestClass]
public sealed class VariantStringComparedWithNumberTests
{
    private static string Run(string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate Sub P(ByVal v As Variant)\r\nDebug.Print CStr(v)\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\nOn Error Resume Next\r\n" + string.Join("\r\n", body.Split(" ;; ")) + "\r\nEnd Sub\r\n";
        return string.Join("/", RunAsync([], program).GetAwaiter().GetResult());
    }

    [TestMethod]
    [DataRow("Dim w: w = \"abc\" ;; P (w < 5) ;; P (w > 5) ;; P (w = 5) ;; P \"Err \" & Err.Number", "False/True/False/Err 0", DisplayName = "H01 \"abc\" and 5")]
    [DataRow("Dim w: w = \"10\" ;; P (w < 5) ;; w = \"+\" ;; P (w < 5) ;; P \"Err \" & Err.Number", "False/False/Err 0", DisplayName = "H02 \"10\" and \"+\"")]
    [DataRow("Dim w: w = \"1/2/1900\" ;; P (w < 5) ;; P (w < 34) ;; P (w > 32) ;; P \"Err \" & Err.Number", "False/False/True/Err 0", DisplayName = "H03 a date's text, and a number")]
    [DataRow("Dim w: w = \"abc\" ;; Dim i As Integer: i = 5 ;; P (w < i) ;; Dim d As Double: d = 5 ;; P (w > d) ;; P \"Err \" & Err.Number", "False/True/Err 0", DisplayName = "H04 typed variables")]
    [DataRow("Dim w: w = \"&H10\" ;; P (w > 5) ;; w = \" 7 \" ;; P (w > 5) ;; w = \"1e3\" ;; P (w > 5) ;; P \"Err \" & Err.Number", "True/True/True/Err 0", DisplayName = "H05 numbers of other forms")]
    [DataRow("Dim w: w = \"3\" ;; P (w < 5) ;; Dim i As Integer: i = 5 ;; P (w < i) ;; P \"Err \" & Err.Number", "True/True/Err 0", DisplayName = "I01 a number's text is the number")]
    [DataRow("Dim w: w = \"7\" ;; P (w > 5) ;; P (w < 50) ;; w = \"1/2/1900\" ;; P (w = 3) ;; P \"Err \" & Err.Number", "True/True/False/Err 0", DisplayName = "I02")]
    [DataRow("Dim w As Variant ;; w = \"3\" ;; Dim d As Date: d = 5 ;; P (w < d) ;; Dim b As Boolean ;; P (w < b) ;; Dim y As Currency: y = 5 ;; P (w < y) ;; P \"Err \" & Err.Number", "True/True/True/Err 0", DisplayName = "I04 a Date, a Boolean, a Currency")]
    [DataRow("Dim w: w = \"abc\" ;; Dim d As Date: d = 5 ;; P (w < d) ;; P (w > d) ;; P \"Err \" & Err.Number", "False/True/Err 0", DisplayName = "K01 a Date")]
    [DataRow("Dim w: w = \"abc\" ;; Dim b As Boolean ;; P (w < b) ;; P (w > b) ;; P \"Err \" & Err.Number", "False/True/Err 0", DisplayName = "K02 a Boolean")]
    [DataRow("Dim w: w = \"abc\" ;; Dim y As Currency: y = 5 ;; P (w < y) ;; P (w > y) ;; P \"Err \" & Err.Number", "False/True/Err 0", DisplayName = "K03 a Currency")]
    [DataRow("Dim w: w = \"1/2/1900\" ;; Dim d As Date: d = 5 ;; P (w < d) ;; P (w > d) ;; P \"Err \" & Err.Number", "True/False/Err 0", DisplayName = "K04 a date's text and a Date")]
    [DataRow("Dim w: w = \"abc\" ;; P (5 < w) ;; P (5 = w) ;; P (w <> 5) ;; P (5 > w) ;; P \"Err \" & Err.Number", "True/False/True/False/Err 0", DisplayName = "K05 the number on the left")]
    [DataRow("Dim w: w = \"\" ;; P (w < 5) ;; P (w = 0) ;; P (w > -5) ;; P \"Err \" & Err.Number", "False/False/True/Err 0", DisplayName = "K06 an empty String")]
    [DataRow("Dim w: w = \"abc\" ;; P (w < 5&) ;; P (w < 5.5) ;; P (w < 5@) ;; P (w < 5!) ;; P \"Err \" & Err.Number", "False/False/False/False/Err 0", DisplayName = "K07 each numeric type")]
    [DataRow("Dim w: w = \"True\" ;; Dim b As Boolean: b = True ;; P (w = b) ;; w = \"abc\" ;; Dim y As Byte: y = 5 ;; P (w > y) ;; P \"Err \" & Err.Number", "True/True/Err 0", DisplayName = "K08 \"True\" and a Boolean, a Byte")]
    [DataRow("Dim w: w = \"&H3\" ;; P (w < 5) ;; w = \" 3 \" ;; P (w < 5) ;; w = \"3e0\" ;; P (w < 5) ;; w = \"3,0\" ;; P (w < 5) ;; w = \"$3\" ;; P (w < 5) ;; P \"Err \" & Err.Number", "True/True/True/False/True/Err 0", DisplayName = "L01 what reads as a number")]
    [DataRow("Dim w: w = \"abc\" ;; Dim i As Integer: i = 5 ;; P (i < w) ;; P (i > w) ;; P (i <> w) ;; P \"Err \" & Err.Number", "True/False/True/Err 0", DisplayName = "L03 a typed number on the left")]
    [DataRow("Dim w: w = \"(3)\" ;; P (w < 0) ;; w = \"3-\" ;; P (w < 0) ;; w = \"True\" ;; P (w < 5) ;; P \"Err \" & Err.Number", "True/True/False/Err 0", DisplayName = "L04 signs, and \"True\" against a number")]
    public void AVariantString_ComparedWithANumber(string body, string expected)
        => Assert.AreEqual(expected, Run(body));

    [TestMethod]
    public void AStringDeclaredAsOne_IsATypeMismatch_WhenItIsNoNumber()
        => Assert.AreEqual("Err 13/True/Err 0", Run("Dim s As String: s = \"abc\" ;; P (s < 5) ;; P \"Err \" & Err.Number ;; Err.Clear ;; s = \"3\" ;; P (s < 5) ;; P \"Err \" & Err.Number"));

    [TestMethod]
    public void TwoVariants_AreRankedAsBefore()
        => Assert.AreEqual("True/False/Err 0", Run("Dim w: w = \"abc\" ;; Dim c ;; c = 5 ;; P (w > c) ;; P (w = c) ;; P \"Err \" & Err.Number"));
}
