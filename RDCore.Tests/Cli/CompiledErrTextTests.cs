using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>Err.Description</c> and <c>Err.Source</c> (MS-VBAL §6.1.3.2.2.1, §6.1.3.2.2.6) read by compiled code: assigned to a <c>String</c>, joined, compared,
/// in a handler. Each is the text of the session's error, read where the session holds it, as the error object reads it. A program runs the same either
/// way.
/// </summary>
[TestClass]
public sealed class CompiledErrTextTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static void AssertNoneStepped(CompilationResult result)
        => Assert.IsEmpty(result.Stepped, string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));

    // Err.Description assigned to a String after a statement that raised an error resumed past, and after one that raised none, then compared: each error
    // raised is the interpreter's.
    [TestMethod]
    public async Task ErrDescription_AssignedAndCompared_IsTheSessionsErrorText()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Count(6)",
            "End Sub",
            "Private Function Count(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    Dim X As Long",
            "    Dim D As String",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        Err.Clear",
            "        X = 10 \\ (I Mod 3)",
            "        D = Err.Description",
            "        If D = \"Division by zero\" Then Count = Count + 10",
            "        If D = \"\" Then Count = Count + 1",
            "    Next",
            "End Function"), "Count", recoveries: 2, expected: "24");
        AssertNoneStepped(result);
    }

    // Err.Source and Err.Description of an error Err.Raise gave both, joined, and compared in a condition.
    [TestMethod]
    public async Task ErrSourceAndDescription_JoinedAndCompared_AreWhatErrRaiseGave()
    {
        var result = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Raised(3)",
            "End Sub",
            "Private Function Raised(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    Dim S As String",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        Err.Clear",
            "        If I = 2 Then Err.Raise 513, \"Mine\", \"Bad\"",
            "        S = Err.Source & \":\" & Err.Description",
            "        If S = \"Mine:Bad\" Then Raised = Raised + 100",
            "        If Err.Source = \"\" Then Raised = Raised + 1",
            "    Next",
            "End Function"), "Raised", recoveries: 1, expected: "102");
        AssertNoneStepped(result);
    }

    // Err.Description in the handler an error went to.
    [TestMethod]
    public async Task ErrDescription_InAHandler_IsTheErrorItCaught()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Guarded(100)",
            "    Debug.Print Guarded(40000)",
            "End Sub",
            "Private Function Guarded(ByVal N As Long) As Long",
            "    Dim K As Integer",
            "    On Error GoTo Failed",
            "    K = N",
            "    Guarded = K",
            "    Exit Function",
            "Failed:",
            "    If Err.Description = \"Overflow\" Then Guarded = -6",
            "End Function"), "Guarded", recoveries: 1, expected: "100 ; -6");
}
