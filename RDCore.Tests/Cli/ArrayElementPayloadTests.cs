using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An element of an array of a scalar type is kept in a cell that holds it as a payload, read and written on payloads: the same values,
/// and the same errors, as the boxed evaluation - which the suite is also run with (<c>RDCORE_UNBOXED=0</c>), for these to hold both ways. The subscripts
/// are let-coerced to a <c>Long</c> either way (<see cref="ArraySubscriptCoercionTests"/>). Every expected line was observed in Excel.
/// </summary>
[TestClass]
public sealed class ArrayElementPayloadTests
{
    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static async Task<string> Run(string body)
        => (await RunAsync([], Program(body.Split(" ;; ")))).Last();

    [TestMethod]
    [DataRow("Dim a(1 To 3) As Integer ;; a(2) = 32767 ;; a(2) = a(2) + 1 ;; Debug.Print Err.Number & \"|\" & a(2)", "6|32767", DisplayName = "A18 an Integer element past its range")]
    [DataRow("Dim a(1 To 3) As Byte ;; a(2) = 255 ;; a(2) = a(2) + 1 ;; Debug.Print Err.Number & \"|\" & a(2)", "6|255", DisplayName = "A19 a Byte element past its range")]
    [DataRow("Dim a(1 To 3) As Long ;; a(2) = 2.5 ;; a(3) = 3.5 ;; Debug.Print a(2) & \"|\" & a(3)", "2|4", DisplayName = "A20 a Double assigned to a Long element, rounded to even")]
    [DataRow("Dim a(1 To 3) As Double, i As Long ;; For i = 1 To 3 ;; a(i) = i / 2 ;; Next ;; Debug.Print a(1) + a(2) + a(3)", "3", DisplayName = "A21 Double elements in a loop")]
    [DataRow("Dim a(1 To 2, 1 To 3) As Long ;; a(2, 3) = 9 ;; Debug.Print a(2, 3) ;; a(3, 1) = 1 ;; Debug.Print Err.Number", "9", DisplayName = "A22 two dimensions, then one outside them")]
    [DataRow("Dim a() As Long ;; a(1) = 1 ;; Debug.Print Err.Number", "9", DisplayName = "A23 an array with no dimensions yet")]
    [DataRow("Dim a() As Long ;; ReDim a(1 To 3) ;; a(2) = 4 ;; ReDim Preserve a(1 To 5) ;; a(5) = a(2) * 2 ;; Debug.Print a(2) & \"|\" & a(5)", "4|8", DisplayName = "A24 ReDim, then ReDim Preserve")]
    [DataRow("Dim a(1 To 3) As Currency ;; a(1) = 1.25 ;; a(2) = a(1) * 2 ;; Debug.Print a(2)", "2.5", DisplayName = "A25 Currency elements")]
    [DataRow("Dim a(1 To 3) As Boolean ;; a(1) = True ;; a(2) = Not a(1) ;; Debug.Print a(1) & \"|\" & a(2)", "True|False", DisplayName = "A26 Boolean elements")]
    [DataRow("Dim a(1 To 3) As Date ;; a(1) = #1/2/2000# ;; a(2) = a(1) + 1 ;; Debug.Print Format(a(2), \"yyyy-mm-dd\")", "2000-01-03", DisplayName = "A27 Date elements")]
    [DataRow("Dim a(1 To 3) As Long ;; a(2) = 5 ;; Erase a ;; Debug.Print a(2) ;; a(2) = 6 ;; Debug.Print a(2)", "6", DisplayName = "A28 Erase, then assigned again")]
    [DataRow("Dim a(1 To 3) As Long ;; a(2) = 5 ;; Erase a ;; Debug.Print a(2)", "0", DisplayName = "A28 Erase resets an element")]
    [DataRow("Dim a(1 To 3) As Long, b() As Long ;; a(2) = 5 ;; b = a ;; b(2) = 6 ;; Debug.Print a(2) & \"|\" & b(2)", "5|6", DisplayName = "A29 an array assigned is a copy")]
    public async Task AnElement_ReadAndWrittenOnPayloads_IsWhatTheBoxedEvaluationReadsAndWrites(string body, string printed)
        => Assert.AreEqual(printed, await Run(body));

    // a module-level array, and one passed by reference: the element written through the parameter is the caller's.
    [TestMethod]
    public async Task AnElementOfAModuleArray_AndOfAnArrayPassedByReference_IsTheSameElement()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate Values(1 To 3) As Long\r\n"
            + "Private Sub Fill(ByRef A() As Long)\r\nDim i As Long\r\nFor i = 1 To 3\r\nA(i) = A(i) + i * 10\r\nNext\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\nValues(2) = 1\r\nFill Values\r\nDebug.Print Values(1) & \"|\" & Values(2) & \"|\" & Values(3)\r\nEnd Sub\r\n";
        Assert.AreEqual("10|21|30", (await RunAsync([], program)).Last());
    }

    // an element passed by reference is the element itself, payload cell or not.
    [TestMethod]
    public async Task AnElementPassedByReference_IsWrittenWhereItIs()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
            + "Private Sub Bump(ByRef N As Long)\r\nN = N + 1\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\nDim a(1 To 3) As Long\r\na(2) = 41\r\nBump a(2)\r\nDebug.Print a(2)\r\nEnd Sub\r\n";
        Assert.AreEqual("42", (await RunAsync([], program)).Last());
    }
}
