using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>String</c> variable of a compiled activation appended to (<c>S = S &amp; ...</c>): what is appended is held by the activation until the variable is
/// read, by compiled code, a callee it is passed to by reference, or the interpreter, which read the String joined. A program runs as it does in the interpreter.
/// </summary>
[TestClass]
public sealed class CompiledStringAppendTests
{
    private static async Task<IReadOnlyDictionary<string, CompilationResult>> AssertAlikeAndCompiledAsync(string program, string expected)
    {
        var compiled = await AssertAlikeAsync(program, expected: expected);
        Assert.IsNotNull(compiled["Build"].Body, string.Join("; ", compiled["Build"].Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
        return compiled;
    }

    // appended to in a loop by &, by + of two Strings and by a whole number, compared, and assigned to another String between appends.
    [TestMethod]
    public async Task AStringAppendedToInALoop_ReadsAsTheStringJoined_WhereverItIsRead()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Build(4)",
            "End Sub",
            "Private Function Build(ByVal N As Long) As String",
            "    Dim S As String, T As String, I As Long, K As Long",
            "    For I = 1 To N",
            "        S = S & \"x\"",
            "        S = S & I & \";\"",
            "        If S = \"x1;\" Then K = K + 1",
            "        S = S + \"-\"",
            "        T = S",
            "    Next",
            "    Build = S & \"|\" & T & \"|\" & K",
            "End Function"), "Build", recoveries: 0, expected: "x1;-x2;-x3;-x4;-|x1;-x2;-x3;-x4;-|1");

    // vbNullString appended, a Let that assigns the String something else between appends, and the String appended to itself.
    [TestMethod]
    public async Task AStringAppendedToItself_AndAssignedBetweenAppends_IsWhatItWasAssigned()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Build(3)",
            "End Sub",
            "Private Function Build(ByVal N As Long) As String",
            "    Dim S As String, I As Long",
            "    For I = 1 To N",
            "        S = S & vbNullString",
            "        S = S & \"ab\"",
            "        If I = 2 Then S = \"c\"",
            "        S = S & S",
            "    Next",
            "    Build = S",
            "End Function"), "Build", recoveries: 0, expected: "ccabccab");

    // each pass printed: Debug.Print reads the String joined.
    [TestMethod]
    public async Task AStringAppendedTo_PrintedAtEachPass_PrintsWhatWasAppendedSoFar()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Build(3)",
            "End Sub",
            "Private Function Build(ByVal N As Long) As String",
            "    Dim S As String, I As Long",
            "    For I = 1 To N",
            "        S = S & I",
            "        Debug.Print S",
            "    Next",
            "    Build = S & \"!\"",
            "End Function"), "Build", recoveries: 0, expected: "12 ; 123 ; 123!");

    // a parameter by value is the callee's own String: the caller's variable is as it was.
    [TestMethod]
    public async Task AParameterByValue_AppendedTo_LeavesTheCallersVariableAsItWas()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As String",
            "    A = \"q\"",
            "    Debug.Print Build(A, 3); A",
            "End Sub",
            "Private Function Build(ByVal S As String, ByVal N As Long) As String",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        S = S & I",
            "    Next",
            "    Build = S",
            "End Function"), "Build", recoveries: 0, expected: "q123q");

    // a parameter by reference is the caller's variable, which the caller reads once the call returned: appended to in a loop, the caller prints it joined.
    [TestMethod]
    public async Task AParameterByReference_AppendedTo_IsTheCallersVariable_Joined()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As String, I As Long",
            "    For I = 1 To 3",
            "        Build A, I",
            "        Debug.Print A",
            "    Next",
            "End Sub",
            "Private Sub Build(ByRef S As String, ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        S = S & I",
            "    Next",
            "End Sub"), "Build", recoveries: 0, expected: "1 ; 112 ; 112123");

    // the String passed by reference to a callee that appends to it, between appends: the callee is given the String joined, and what it appended stays.
    [TestMethod]
    public async Task AStringAppendedTo_PassedByReferenceBetweenAppends_IsGivenToTheCalleeJoined()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Build(3)",
            "End Sub",
            "Private Function Build(ByVal N As Long) As String",
            "    Dim S As String, I As Long",
            "    For I = 1 To N",
            "        S = S & \"a\"",
            "        Touch S",
            "        S = S & \"b\"",
            "    Next",
            "    Build = S",
            "End Function",
            "Private Sub Touch(ByRef T As String)",
            "    T = T & \"|\"",
            "End Sub"), "Build", recoveries: 0, expected: "a|ba|ba|b");

    // a statement the interpreter runs between appends - the Mid$ statement - reads and writes the String joined.
    [TestMethod]
    public async Task AStringAppendedTo_ChangedByAStatementTheInterpreterRuns_GoesOnFromWhatThatStatementLeft()
        => await AssertAlikeAndCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Build(3)",
            "End Sub",
            "Private Function Build(ByVal N As Long) As String",
            "    Dim S As String, I As Long",
            "    For I = 1 To N",
            "        S = S & \"ab\"",
            "        Mid$(S, I, 1) = \"Z\"",
            "        S = S & \"c\"",
            "    Next",
            "    Build = S",
            "End Function"), "ZZZabcabc");

    // an error in what is appended, gone past: nothing of the statement is appended.
    [TestMethod]
    public async Task AnErrorInWhatIsAppended_GonePast_AppendsNothing()
        => await AssertAlikeAndCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Build(3)",
            "End Sub",
            "Private Function Build(ByVal N As Long) As String",
            "    Dim S As String, I As Long, Z As Long",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        S = S & \"a\"",
            "        S = S & (I \\ Z) & \"x\"",
            "        S = S & \"b\"",
            "    Next",
            "    Build = S & Err.Number",
            "End Function"), "ababab11");

    // the address of the String taken between appends: the String goes on joined, the interpreter's from then on.
    [TestMethod]
    [DataRow("StrPtr", DisplayName = "StrPtr")]
    [DataRow("VarPtr", DisplayName = "VarPtr")]
    public async Task AStringAppendedTo_WhoseAddressIsTaken_GoesOnJoined(string function)
        => await AssertAlikeAndCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Build(3)",
            "End Sub",
            "Private Function Build(ByVal N As Long) As String",
            "    Dim S As String, I As Long, P As Variant",
            "    For I = 1 To N",
            "        S = S & \"a\"",
            $"        If I = 2 Then P = {function}(S)",
            "        S = S & \"b\"",
            "    Next",
            "    Build = S & (P <> 0)",
            "End Function"), "abababTrue");
}
