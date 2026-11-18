using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The result of a <c>String</c> function, read by its name in the function's own body (<strong>MS-VBAL §5.3.1</strong>), given to a function of the
/// standard library that takes a <c>String</c>, such as <c>Len</c> or <c>Mid$</c> (<strong>MS-VBAL §6.1.2.11.1.22, §6.1.2.11.1.26</strong>), or
/// printed: compiled code gives the function the text the result holds, where the frame holds it, as the interpreter gives it. A program runs the same
/// either way, and nothing of the function is handed to the interpreter.
/// </summary>
[TestClass]
public sealed class CompiledStringResultArgumentTests
{
    // the program run both ways, printing expected: the function named compiled, with no statement stepped and no instruction handed to the interpreter.
    private static async Task AssertNoneSteppedAsync(string program, string function, string expected)
    {
        var compiled = await AssertAlikeAsync(program, expected: expected);
        Assert.IsTrue(compiled.TryGetValue(function, out var result), $"{function} was not called");
        Assert.IsNotNull(result.Body, $"{function}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsEmpty(result.Stepped, $"{function} steps: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");

        // under the random walk-back every activation may be handed to the interpreter, which is what that run is for.
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(0, result.Body.Recoveries, $"{function}'s recoveries");
        }
    }

    // a key made of the text the result holds and of its lowercase letters, read one at a time with Mid$ in a For loop whose end is the Len of the result:
    // the loop is compiled code's, its end evaluated once, as the interpreter evaluates it (MS-VBAL §5.4.2.3).
    [TestMethod]
    public async Task TheLenAndTheMidOfTheResult_AreGivenTheTextItHolds()
        => await AssertNoneSteppedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Keyed(\"AbC\"); \"|\"; Keyed(\"abc\"); \"|\"; Keyed(\"ABC\")",
            "End Sub",
            "Private Function Keyed(ByVal K As String) As String",
            "    Keyed = K",
            "    Dim Lower As String, I As Integer, C As String",
            "    For I = 1 To VBA.Len(Keyed)",
            "        C = VBA.Mid$(Keyed, I, 1)",
            "        If Asc(C) >= 97 And Asc(C) <= 122 Then",
            "            Lower = Lower & C",
            "        Else",
            "            Lower = Lower & \"_\"",
            "        End If",
            "    Next I",
            "    If Lower <> \"\" Then Keyed = Keyed & \"__\" & Lower",
            "End Function"), "Keyed", expected: "AbC___b_|abc__abc|ABC_____");

    // before it is first assigned, and once assigned vbNullString, the result is the zero-length string: its Len is 0, and its Mid$ is empty.
    [TestMethod]
    public async Task TheResultBeforeItIsAssigned_IsGivenAsTheZeroLengthString()
        => await AssertNoneSteppedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Blank()",
            "End Sub",
            "Private Function Blank() As String",
            "    Dim N As Long, S As String",
            "    N = Len(Blank)",
            "    S = Mid$(Blank, 1, 1)",
            "    Blank = vbNullString",
            "    N = N + Len(Blank)",
            "    Blank = \"[\" & N & S & \"]\"",
            "End Function"), "Blank", expected: "[0]");

    // the result given to the other functions of Strings: UCase$, Left$ and InStr take its text.
    [TestMethod]
    public async Task TheResultGivenToOtherStringFunctions_IsItsText()
        => await AssertNoneSteppedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Shout(\"abc\")",
            "End Sub",
            "Private Function Shout(ByVal K As String) As String",
            "    Dim N As Long",
            "    Shout = K",
            "    N = InStr(Shout, \"b\")",
            "    Shout = UCase$(Shout)",
            "    Shout = Shout & Left$(Shout, 1) & N",
            "End Function"), "Shout", expected: "ABCA2");

    // each call of a recursive String function has a result of its own (MS-VBAL §5.3.1): once a call it makes returns, the Len of its result is that of its
    // own text, not of the callee's.
    [TestMethod]
    public async Task TheLenOfTheResultAfterARecursiveCall_IsThatOfItsOwnCall()
        => await AssertNoneSteppedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Depth(2)",
            "End Sub",
            "Private Function Depth(ByVal N As Integer) As String",
            "    Dim T As String, L As Long",
            "    Depth = \"x\" & N",
            "    If N > 0 Then T = Depth(N - 1)",
            "    L = Len(Depth)",
            "    Depth = Depth & L & \"(\" & T & \")\"",
            "End Function"), "Depth", expected: "x22(x12(x02()))");

    // the result printed: a String, written with no space around it, as the interpreter writes it (MS-VBAL §5.4.5.8).
    [TestMethod]
    public async Task TheResultPrinted_IsWrittenAsAString()
        => await AssertNoneSteppedAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Shown()",
            "End Sub",
            "Private Function Shown() As String",
            "    Shown = \"ab\"",
            "    Debug.Print Shown; \"|\"",
            "End Function"), "Shown", expected: "ab|");
}
