using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The least <c>Integer</c> literal negated, as MS-VBA has it: <c>-&amp;H8000</c> is the unary minus of an <c>Integer</c> that has no negation an <c>Integer</c>
/// holds, an overflow (6) when it runs and a compile error in a <c>Const</c> (observed in Excel).
/// </summary>
[TestClass]
public sealed class NegatedLeastIntegerTests
{
    private static string Program(string declarations, string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\n" + declarations
            + "Private Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nDebug.Print acc\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("P -&H8000 ;; P \"e\" & Err.Number", "e6|", DisplayName = "N01 printed")]
    [DataRow("P -(&H8000) ;; P \"e\" & Err.Number", "e6|", DisplayName = "N03 in parentheses")]
    [DataRow("Dim x As Long ;; x = -&H8000 ;; P x ;; P \"e\" & Err.Number", "0|e6|", DisplayName = "N05 assigned to a Long")]
    [DataRow("Dim x As Variant ;; x = -&O100000 ;; P x ;; P \"e\" & Err.Number", "|e6|", DisplayName = "N06 in octal")]
    [DataRow("P -&H8000& ;; P -32768 ;; P TypeName(-32768) ;; P -&H7FFF", "-32768|-32768|Long|-32767|", DisplayName = "N02 what does fit")]
    [DataRow("Dim x As Variant ;; x = Not &H8000 ;; P x ;; x = &H8000 - 1 ;; P x ;; P \"e\" & Err.Number", "32767|32767|e6|", DisplayName = "N06 the other operators")]
    public async Task TheLeastIntegerNegated_Overflows(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync([], Program(string.Empty, body))).Last());

    [TestMethod]
    public async Task TheLeastIntegerNegated_InAConst_IsACompileError()
        => StringAssert.Contains(string.Join(" ", await LoadErrorsAsync([], Program("Private Const K = -&H8000\r\n", "P K"))), "Overflow");
}
