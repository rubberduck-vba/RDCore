using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A subscript of an element of an array is let-coerced to a <c>Long</c>, as a bound is: MS-VBAL §5.6.13 only says it selects the element when it is within
/// its dimension, and MS-VBA rounds a <c>Double</c> to even, reads a <c>String</c> as a number, a <c>Boolean</c> as 0/-1, <c>Empty</c> as 0, and raises 6
/// past the range of a <c>Long</c>, 13 for text that is no number, 94 for <c>Null</c>. Every expected line was observed in Excel.
/// </summary>
[TestClass]
public sealed class ArraySubscriptCoercionTests
{
    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("Dim a(1 To 3) As Long ;; a(2.5) = 7 ;; Debug.Print Err.Number & \"|\" & a(2) & \"|\" & a(3)", "0|7|0", DisplayName = "a Double subscript, rounded to even")]
    [DataRow("Dim a(1 To 3) As Long ;; a(1.5) = 7 ;; Debug.Print Err.Number & \"|\" & a(2)", "0|7", DisplayName = "a Double subscript, rounded up to even")]
    [DataRow("Dim a(1 To 3) As Long, b As Byte ;; b = 2 ;; a(b) = 7 ;; Debug.Print Err.Number & \"|\" & a(2)", "0|7", DisplayName = "a Byte subscript")]
    [DataRow("Dim a(1 To 3) As Long ;; a(\"2\") = 7 ;; Debug.Print Err.Number & \"|\" & a(2)", "0|7", DisplayName = "a String subscript")]
    [DataRow("Dim a(1 To 3) As Long ;; a(2) = 7 ;; Debug.Print a(2.4) & \"|\" & Err.Number", "7|0", DisplayName = "a Double subscript read")]
    [DataRow("Dim a(1 To 3) As Long ;; a(3000000000#) = 7 ;; Debug.Print Err.Number", "6", DisplayName = "a subscript past a Long")]
    [DataRow("Dim a(1 To 3) As Long, v As Variant ;; v = 2.5 ;; a(v) = 7 ;; Debug.Print Err.Number & \"|\" & a(2)", "0|7", DisplayName = "a Variant subscript")]
    [DataRow("Dim a(1 To 3) As Long ;; a(\"x\") = 7 ;; Debug.Print Err.Number", "13", DisplayName = "a subscript of text that is no number")]
    [DataRow("Dim a(1 To 3) As Long ;; a(True) = 7 ;; Debug.Print Err.Number", "9", DisplayName = "True is -1, outside the bounds")]
    [DataRow("Dim a(-1 To 3) As Long ;; a(True) = 7 ;; Debug.Print Err.Number & \"|\" & a(-1)", "0|7", DisplayName = "True is -1, inside the bounds")]
    [DataRow("Dim a(1 To 3) As Long, c As Currency ;; c = 2.5 ;; a(c) = 7 ;; Debug.Print Err.Number & \"|\" & a(2)", "0|7", DisplayName = "a Currency subscript")]
    [DataRow("Dim a(1 To 3) As Long, v As Variant ;; a(v) = 7 ;; Debug.Print Err.Number", "9", DisplayName = "Empty is 0, outside the bounds")]
    [DataRow("Dim a(0 To 3) As Long, v As Variant ;; a(v) = 7 ;; Debug.Print Err.Number & \"|\" & a(0)", "0|7", DisplayName = "Empty is 0, inside the bounds")]
    [DataRow("Dim a(1 To 3) As Long ;; a(Null) = 7 ;; Debug.Print Err.Number", "94", DisplayName = "a Null subscript")]
    [DataRow("Dim a(1 To 3) As Long, d As Date ;; d = 2 ;; a(d) = 7 ;; Debug.Print Err.Number & \"|\" & a(2)", "0|7", DisplayName = "a Date subscript")]
    public async Task ASubscript_IsLetCoercedToALong(string body, string printed)
        => Assert.AreEqual(printed, (await RunAsync([], Program(body.Split(" ;; ")))).Last());
}
