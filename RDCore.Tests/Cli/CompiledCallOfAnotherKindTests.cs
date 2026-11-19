using static RDCore.Tests.Cli.CompiledErrorHandlingTests;
using static RDCore.Tests.Cli.CompiledVariantParameterOfAnotherTypeTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A Let of a scalar variable, or of the scalar result of a function, of what a function of the workspace returns of another scalar type
/// (<strong>MS-VBAL §5.4.3.8</strong>): what the function returns is let-coerced to the type of the variable (<strong>MS-VBAL §5.5.1.2.1</strong>), by
/// compiled code, with no instruction handed to the interpreter.
/// </summary>
[TestClass]
public sealed class CompiledCallOfAnotherKindTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // a Byte variable given what a Long function of a standard module returns, and a Long variable given what an Integer one returns.
    [TestMethod]
    public async Task AVariableGivenWhatAFunctionReturnsOfAnotherType_IsAssignedByCompiledCode()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Private Function Doubled(ByVal N As Long) As Long",
            "    Doubled = N * 2",
            "End Function",
            "Private Function Halved(ByVal N As Long) As Integer",
            "    Halved = N \\ 2",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Joined()",
            "End Sub",
            "Private Function Joined() As String",
            "    Dim B As Byte, N As Long, I As Long, T As String",
            "    For I = 1 To 3",
            "        B = Doubled(I)",
            "        N = Halved(I * 10)",
            "        T = T & B & \",\" & N & \";\"",
            "    Next",
            "    Joined = T",
            "End Function"), expected: "2,5;4,10;6,15;");
        AssertNoneStepped(compiled, "Joined");
    }

    // the Byte result of a function given what a Long Property Get of an object returns: the size of a ship held in a record field.
    [TestMethod]
    public async Task AFunctionResultGivenWhatAMemberReturnsOfAnotherType_IsAssignedByCompiledCode()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim B As Bag, I As Long, T As String",
            "    Set B = New Bag",
            "    For I = 1 To 3",
            "        B.Add I",
            "        T = T & Size(B) & \";\"",
            "    Next",
            "    Debug.Print T",
            "End Sub",
            "Private Function Size(ByVal Items As Bag) As Byte",
            "    Size = Items.Count",
            "End Function"), classes: [("Bag", ModuleWorkspace.ClassModule("Bag",
                "Option Explicit",
                "Private Held As Long",
                "Public Sub Add(ByVal N As Long)",
                "    Held = Held + N",
                "End Sub",
                "Public Property Get Count() As Long",
                "    Count = Held",
                "End Property"))], expected: "1;3;6;");
        AssertNoneStepped(compiled, "Size");
    }

    // what the function returns does not fit the Byte variable: the Overflow is raised by the Let, after the call, and handled as the interpreter
    // handles it.
    [TestMethod]
    public async Task AValueThatDoesNotFitTheVariable_RaisesAnOverflowAfterTheCall()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Private Function Doubled(ByVal N As Long) As Long",
            "    Debug.Print \"called\";",
            "    Doubled = N * 2",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Joined()",
            "End Sub",
            "Private Function Joined() As String",
            "    On Error GoTo Failed",
            "    Dim B As Byte",
            "    B = Doubled(200)",
            "    Joined = \"fits \" & B",
            "    Exit Function",
            "Failed:",
            "    Joined = \" \" & Err.Number & \" \" & B",
            "End Function"), expected: "called 6 0");
        Assert.IsTrue(compiled.TryGetValue("Joined", out var result), "Joined was not called");
        Assert.IsNotNull(result.Body, string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }
}
