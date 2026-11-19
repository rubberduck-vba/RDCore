using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A fixed-size array passed to an <c>A() As T</c> parameter is passed by reference, as a resizable one is (MS-VBAL §5.3.1.11): the declared type of
/// either is an array of <c>T</c>. What the callee writes is the caller's, and a <c>ReDim</c> of it raises 10. Every expected line was observed in Excel.
/// </summary>
[TestClass]
public sealed class FixedArrayByReferenceTests
{
    private static async Task<string> Run(string declarations, string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + string.Join("\r\n", declarations.Split(" ;; "))
            + "\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n" + string.Join("\r\n", body.Split(" ;; ")) + "\r\nEnd Sub\r\n";
        return string.Join("/", await RunAsync([], program));
    }

    [TestMethod]
    [DataRow("Private Sub Fill(ByRef A() As Long) ;; A(2) = 5 ;; End Sub", "Dim V(1 To 3) As Long ;; Fill V ;; Debug.Print V(2)", "5", DisplayName = "B01 a local fixed-size array")]
    [DataRow("Private Sub Fill(A() As Long) ;; A(2) = 5 ;; End Sub", "Dim V(1 To 3) As Long ;; Fill V ;; Debug.Print V(2)", "5", DisplayName = "B05 ByRef by default")]
    [DataRow("Private Values(1 To 3) As Long ;; Private Sub Fill(ByRef A() As Long) ;; A(2) = A(2) + 5 ;; Debug.Print Values(2) ;; End Sub",
        "Values(2) = 1 ;; Fill Values ;; Debug.Print Values(2)", "6/6", DisplayName = "B06 a module's fixed-size array, the same array in the callee")]
    [DataRow("Private Sub Fill(ByRef A() As Long) ;; A(2) = 5 ;; End Sub", "Dim V() As Long ;; ReDim V(1 To 3) ;; Fill V ;; Debug.Print V(2)", "5", DisplayName = "B07 a resizable array, as before")]
    [DataRow("Private Sub Wipe(ByRef A() As Long) ;; Erase A ;; End Sub", "Dim V(1 To 3) As Long ;; V(2) = 4 ;; Wipe V ;; Debug.Print Err.Number & \"|\" & V(2) & \"|\" & UBound(V)", "0|0|3",
        DisplayName = "B03 Erase resets the elements, the bounds kept")]
    public async Task AFixedSizeArray_IsPassedByReference(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    [TestMethod]
    [DataRow("Private Sub Grow(ByRef A() As Long) ;; ReDim A(1 To 5) ;; End Sub", DisplayName = "B02 ReDim")]
    [DataRow("Private Sub Grow(ByRef A() As Long) ;; ReDim Preserve A(1 To 5) ;; End Sub", DisplayName = "B08 ReDim Preserve")]
    public async Task AReDimOfAFixedSizeArrayThroughAParameter_RaisesTen(string declarations)
        => Assert.AreEqual("10|3", await Run(declarations, "Dim V(1 To 3) As Long ;; Grow V ;; Debug.Print Err.Number & \"|\" & UBound(V)"));

    // a copy of a fixed-size array is an array like any other: a resizable variable, or a Variant, it is assigned to can be resized.
    [TestMethod]
    [DataRow("Dim V(1 To 3) As Long, W() As Long ;; V(2) = 4 ;; W = V ;; ReDim Preserve W(1 To 5) ;; Debug.Print Err.Number & \"|\" & UBound(W) & \"|\" & W(2)", DisplayName = "into a resizable array")]
    [DataRow("Dim V(1 To 3) As Long, X As Variant ;; V(2) = 4 ;; X = V ;; ReDim Preserve X(1 To 5) ;; Debug.Print Err.Number & \"|\" & UBound(X) & \"|\" & X(2)", DisplayName = "into a Variant")]
    public async Task ACopyOfAFixedSizeArray_CanBeResized(string body)
        => Assert.AreEqual("0|5|4", await Run("", body));
}
