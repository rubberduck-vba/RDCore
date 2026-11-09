using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>Err.Number</c> (MS-VBAL §6.1.3.2.2.5) read by compiled code: as an operand, in a condition, for the whole value a variable is given, in a handler. It
/// is the number of the session's error, read where the session holds it, as the error object reads it. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledErrNumberTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // Err.Number as an operand, after a statement that raised an error resumed past, and after one that raised none: each error raised is the interpreter's.
    [TestMethod]
    public async Task ErrNumber_AsAnOperand_IsTheSessionsErrorNumber()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Total(6)",
            "End Sub",
            "Private Function Total(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    Dim X As Long",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        Err.Clear",
            "        X = 10 \\ (I Mod 3)",
            "        Total = Total + Err.Number + X",
            "    Next",
            "End Function"), "Total", recoveries: 2, expected: "62");

    // Err.Number for the whole value a variable is given, and in a condition.
    [TestMethod]
    public async Task ErrNumber_ForAWholeValue_OrInACondition_IsTheSessionsErrorNumber()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Count(6)",
            "End Sub",
            "Private Function Count(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    Dim X As Long",
            "    Dim E As Long",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        Err.Clear",
            "        X = 10 \\ (I Mod 3)",
            "        E = Err.Number",
            "        If Err.Number <> 0 Then Count = Count + 1",
            "    Next",
            "    Count = Count * 100 + E",
            "End Function"), "Count", recoveries: 2, expected: "211");

    // Err.Number in the handler an error went to.
    [TestMethod]
    public async Task ErrNumber_InAHandler_IsTheErrorItCaught()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Ratio(10, 2)",
            "    Debug.Print Ratio(1, 0)",
            "End Sub",
            "Private Function Ratio(ByVal A As Long, ByVal B As Long) As Long",
            "    On Error GoTo Failed",
            "    Ratio = A \\ B",
            "    Exit Function",
            "Failed:",
            "    Ratio = -Err.Number",
            "End Function"), "Ratio", recoveries: 1, expected: "5 ; -11");
}
