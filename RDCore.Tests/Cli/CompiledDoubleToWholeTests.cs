using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Single</c> or a <c>Double</c> let-coerced to an integer type is compiled (MS-VBAL §5.5.1.2.1): it is the whole number Banker's rounding makes of
/// it (§5.5.1.2.1.1) when that number is in the range of the type. Past it, the interpreter is given the statement, and raises error 6.
/// </summary>
[TestClass]
public sealed class CompiledDoubleToWholeTests
{
    // a tie goes to the even number, at either end of the range of a Long and an Integer too; -0.5 is the Byte 0, and the least LongLong is a Double.
    [TestMethod]
    public async Task ADoubleAtTheEndsOfTheRange_IsRoundedHalfToEven()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Longs(2147483646.5) - 2147483646; Longs(2147483647.4) - 2147483647; Longs(-2147483648.5) + 2147483647; Longs(-2.5); Longs(3.5);",
            "    Debug.Print Integers(32766.5); Integers(-32768.5); Bytes(-0.5); Bytes(254.5); Bytes(255.4); Singles(2.5); Singles(3.5);",
            "    Debug.Print LongLongs(-2 ^ 63)",
            "End Sub",
            "Private Function Longs(ByVal D As Double) As Long",
            "    Longs = CLng(D)",
            "End Function",
            "Private Function Integers(ByVal D As Double) As Integer",
            "    Integers = CInt(D)",
            "End Function",
            "Private Function Bytes(ByVal D As Double) As Byte",
            "    Dim B As Byte",
            "    B = D",
            "    Bytes = B",
            "End Function",
            "Private Function Singles(ByVal S As Single) As Long",
            "    Singles = CLng(S)",
            "End Function",
            "Private Function LongLongs(ByVal D As Double) As LongLong",
            "    LongLongs = CLngLng(D)",
            "End Function"), [], "| 0  0 -1 -2  4  32766 -32768  0  254  255  2  4 -9223372036854775808 |",
            ("Longs", 0), ("Integers", 0), ("Bytes", 0), ("Singles", 0), ("LongLongs", 0));

    // a Double whose whole number is past the range of the type: the interpreter is given the statement, and raises error 6 (Overflow).
    [TestMethod]
    [DataRow("CLng", "2147483647.5", DisplayName = "CLng(2147483647.5)")]
    [DataRow("CLng", "-2147483648.6", DisplayName = "CLng(-2147483648.6)")]
    [DataRow("CInt", "32767.5", DisplayName = "CInt(32767.5)")]
    [DataRow("CByte", "-0.6", DisplayName = "CByte(-0.6)")]
    [DataRow("CByte", "255.5", DisplayName = "CByte(255.5)")]
    [DataRow("CLngLng", "9.2233720368547758E+18", DisplayName = "CLngLng(2 ^ 63)")]
    public async Task ADoublePastTheRangeOfTheType_RaisesError6(string conversion, string value)
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer()",
            "End Sub",
            "Private Function Outer() As Long",
            "    Dim L As LongLong, D As Double",
            $"    D = {value}",
            "    On Error Resume Next",
            $"    L = {conversion}(D)",
            "    Outer = Err.Number",
            "End Function"), [], "| 6 |", ("Outer", 1));

    // MS-VBAL §5.5.1.2.1: an infinity or a NaN is in the range of no integer type, and raises error 6. Each value is made of the bytes of a Double, the LSet
    // of one record over another: +inf, -inf and a NaN.
    [TestMethod]
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
            $"    Debug.Print Rounded({high})",
            "End Sub",
            "Private Function Rounded(ByVal High As Long) As Long",
            "    Dim H As Halves, W As Whole, L As Long",
            "    H.High = High",
            "    LSet W = H",
            "    On Error Resume Next",
            "    L = W.D",
            "    Rounded = Err.Number",
            "End Function"), expected: "| 6 |");
}
