using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A numeric scalar variable assigned an expression of them is assigned on payloads (IR lowering S1.2): the same values, and the same errors, as the
/// boxed evaluation - which the suite is also run with (<c>RDCORE_UNBOXED=0</c>), for these to hold both ways.
/// </summary>
[TestClass]
public sealed class UnboxedEvaluationTests
{
    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("Dim l As Long, i As Integer ;; i = 3 ;; l = 2147483647 ;; l = l - i ;; Debug.Print l", "2147483644", DisplayName = "a Long less an Integer")]
    [DataRow("Dim d As Double, l As Long ;; d = 0.5 ;; l = 2 ;; d = d + l ;; Debug.Print d", "2.5", DisplayName = "a Double plus a Long")]
    [DataRow("Dim d As Double ;; d = 7 ;; d = d / 2 ;; Debug.Print d", "3.5", DisplayName = "a Double divided")]
    [DataRow("Dim s As Single ;; s = 0.5 ;; s = s * 3 ;; Debug.Print s", "1.5", DisplayName = "a Single multiplied")]
    [DataRow("Dim i As Integer ;; i = 5 ;; i = -i ;; Debug.Print i", "-5", DisplayName = "an Integer negated")]
    [DataRow("Dim ll As LongLong ;; ll = 4000000000^ ;; ll = ll * 2 ;; Debug.Print ll", "8000000000", DisplayName = "a LongLong multiplied")]
    [DataRow("Dim i As Integer, l As Long ;; l = 300 ;; i = l + 1 ;; Debug.Print i", "301", DisplayName = "a Long in the range of an Integer, assigned to one")]
    [DataRow("Dim b As Byte ;; b = 200 ;; b = b + 50 ;; Debug.Print b", "250", DisplayName = "a Byte that fits")]
    public void WhatIsAssignedOnPayloads_IsWhatTheBoxedEvaluationAssigns(string body, string printed)
        => Assert.AreEqual(printed, RunAsync([], Program(body.Split(" ;; "))).GetAwaiter().GetResult().Last());

    // the errors are the boxed evaluation's: the payload evaluation declines what would raise one, and the expression is evaluated again, boxed.
    [TestMethod]
    [DataRow("Dim l As Long ;; l = 2147483647 ;; l = l + 1 ;; Debug.Print Err.Number & \" \" & l", "6 2147483647", DisplayName = "a Long past its range")]
    [DataRow("Dim i As Integer, l As Long ;; l = 40000 ;; i = l ;; Debug.Print Err.Number & \" \" & i", "6 0", DisplayName = "a Long past the range of an Integer, assigned to one")]
    [DataRow("Dim i As Integer ;; i = -32768 ;; i = -i ;; Debug.Print Err.Number & \" \" & i", "6 -32768", DisplayName = "the least Integer negated")]
    [DataRow("Dim b As Byte ;; b = 200 ;; b = b + 100 ;; Debug.Print Err.Number & \" \" & b", "6 200", DisplayName = "a Byte past its range")]
    [DataRow("Dim d As Double ;; d = 1 ;; d = d / 0 ;; Debug.Print Err.Number & \" \" & d", "11 1", DisplayName = "a Double divided by zero")]
    [DataRow("Dim ll As LongLong ;; ll = 9223372036854775807^ ;; ll = ll + 1 ;; Debug.Print Err.Number & \" \" & ll", "6 9223372036854775807", DisplayName = "a LongLong past its range")]
    public void WhatWouldRaiseAnError_RaisesTheBoxedEvaluationsError(string body, string printed)
        => Assert.AreEqual(printed, RunAsync([], Program(body.Split(" ;; "))).GetAwaiter().GetResult().Last());

    // a variable a ByRef parameter is an alias of is the caller's: assigned on payloads through the alias, the caller sees it.
    [TestMethod]
    public async Task AVariableAssignedThroughAByRefParameter_IsTheCallersVariable()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
            + "Private Sub Twice(ByRef N As Long)\r\nN = N * 2\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\nDim L As Long\r\nL = 21\r\nTwice L\r\nL = L + 0\r\nDebug.Print L\r\nEnd Sub\r\n";

        Assert.AreEqual("42", (await RunAsync([], program)).Last());
    }
}
