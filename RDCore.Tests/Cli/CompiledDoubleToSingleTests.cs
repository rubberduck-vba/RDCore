using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Double</c>, or a <c>Date</c>'s serial, let-coerced to a <c>Single</c> is compiled (MS-VBAL §5.5.1.2.1): it is the <c>Single</c> nearest it, ties
/// to even, when it is within the magnitude range of a <c>Single</c>. Past it, the interpreter is given the statement, and raises error 6.
/// </summary>
[TestClass]
public sealed class CompiledDoubleToSingleTests
{
    // the nearest Single, read back as a Double: 2^24 + 1 is a tie, which goes to the even 2^24, and 2^24 + 3 one that goes to 2^24 + 4; 1 + 2^-24 is a
    // tie that goes to the even 1, 1 + 2^-23 is a Single, and 1E-50 is too small for a Single, which makes it 0.
    [TestMethod]
    public async Task ADouble_IsTheNearestSingle()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Narrowed(16777217) - 16777216; Narrowed(16777219) - 16777216; Narrowed(2.5); Narrowed(1E-50);",
            "    Debug.Print Narrowed(1 + 2 ^ -24) - 1; Narrowed(1 + 2 ^ -23) - 1",
            "End Sub",
            "Private Function Narrowed(ByVal D As Double) As Double",
            "    Dim S As Single",
            "    S = D",
            "    Narrowed = S",
            "End Function"), [], "| 0  4  2.5  0  0  1.19209289550781E-07 |", ("Narrowed", 0));

    // a Double past the magnitude range of a Single, either way: the interpreter is given the statement, and raises error 6 (Overflow).
    [TestMethod]
    [DataRow("3.5E+38", DisplayName = "3.5E+38")]
    [DataRow("-1E+39", DisplayName = "-1E+39")]
    public async Task ADoublePastTheRangeOfASingle_RaisesError6(string value)
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer()",
            "End Sub",
            "Private Function Outer() As Long",
            "    Dim S As Single, D As Double",
            $"    D = {value}",
            "    On Error Resume Next",
            "    S = D",
            "    Outer = Err.Number",
            "End Function"), [], "| 6 |", ("Outer", 1));

    // a Variant that holds a Double, let-coerced to a Single.
    [TestMethod]
    public async Task AVariantHoldingADouble_IsTheNearestSingle()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(16777217) - 16777216; Outer(16777219) - 16777216; Outer(2.5)",
            "End Sub",
            "Private Function Outer(ByVal D As Double) As Double",
            "    Dim V As Variant, S As Single",
            "    V = D",
            "    S = V",
            "    Outer = S",
            "End Function"), [], "| 0  4  2.5 |", ("Outer", 0));

    // a Date is its serial (MS-VBAL §5.5.1.2.3), as a Single: noon of 2000-01-02 is 36527.5, of a Date variable and of a Variant that holds a Date.
    [TestMethod]
    public async Task ADate_IsItsSerialAsASingle()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(False); Outer(True)",
            "End Sub",
            "Private Function Outer(ByVal Held As Boolean) As Double",
            "    Dim T As Date, V As Variant, S As Single",
            "    T = #1/2/2000 12:00:00 PM#",
            "    If Held Then",
            "        V = T",
            "        S = V",
            "    Else",
            "        S = T",
            "    End If",
            "    Outer = S",
            "End Function"), [], "| 36527.5  36527.5 |", ("Outer", 0));

    // MS-VBAL §5.5.1.2.1: a floating-point value let-coerced to a floating-point type must be finite, or error 6 is raised. Each value is made of the bytes
    // of a Double, the LSet of one record over another: +inf, -inf and a NaN.
    [TestMethod]
    [Ignore("The interpreter's let-coercion (ValueConversions, a non-finite floating-point source) gives a Single an infinity or a NaN as it is, with no"
        + " error, and PayloadOperations.Convert does the same in compiled code: both engines leave Err.Number 0. Not measured in MS-VBA for a Double.")]
    [DataRow("&H7FF00000", DisplayName = "+inf")]
    [DataRow("&HFFF00000", DisplayName = "-inf")]
    [DataRow("&H7FF80000", DisplayName = "a NaN")]
    public async Task ANonFiniteDouble_RaisesError6(string high)
        => await CompiledErrorHandlingTests.AssertAlikeAsync(Program(
            "Private Type Halves",
            "    Low As Long",
            "    High As Long",
            "End Type",
            "Private Type Whole",
            "    D As Double",
            "End Type",
            "Public Sub Main()",
            $"    Debug.Print Narrowed({high})",
            "End Sub",
            "Private Function Narrowed(ByVal High As Long) As Long",
            "    Dim H As Halves, W As Whole, S As Single",
            "    H.High = High",
            "    LSet W = H",
            "    On Error Resume Next",
            "    S = W.D",
            "    Narrowed = Err.Number",
            "End Function"), expected: "| 6 |");

    // a Double given to a Single parameter of a Property Let, which compiled code calls: the interpreter assigns nothing (no support call), and the property
    // holds the nearest Single, which Main reads back.
    [TestMethod]
    public async Task ADoubleGivenToASingleParameter_IsTheNearestSingle_ByCompiledCodesCall()
    {
        var compiled = await CompiledErrorHandlingTests.AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim B As Box",
            "    Set B = New Box",
            "    Outer B, 2.5",
            "    Debug.Print B.Size;",
            "    Outer B, 16777217",
            "    Debug.Print B.Size - 16777216",
            "End Sub",
            "Private Sub Outer(ByVal B As Box, ByVal D As Double)",
            "    B.Size = D",
            "End Sub"),
            [("Box", ModuleWorkspace.ClassModule("Box", "Option Explicit", "Private mSize As Single",
                "Public Property Get Size() As Single", "    Size = mSize", "End Property",
                "Public Property Let Size(ByVal Value As Single)", "    mSize = Value", "End Property"))],
            "| 2.5  0 |");

        Assert.IsTrue(compiled.TryGetValue("Outer", out var outer), "Outer was not called");
        Assert.IsNotNull(outer.Body, string.Join("; ", outer.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
        if (!RDCore.Runtime.Compiled.CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(0L, outer.Body.SupportCallsMade, "Outer's support calls");
            Assert.AreEqual(0L, outer.Body.Recoveries, "Outer's recoveries");
        }
    }
}
