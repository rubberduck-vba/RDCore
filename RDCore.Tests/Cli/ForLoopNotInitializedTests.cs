using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// RD-VBAL §5.4.2.3: a jump into the body of a <c>For</c> loop whose <c>For</c> statement never ran. MS-VBAL §5.4.2.3 has its <c>Next</c> raise
/// error 92; MS-VBA raises it only for a counter declared <c>Variant</c>, and for a <c>For Each</c>. A <c>Next</c> of any other counter steps it on
/// the loop a <c>For</c> that was never given its operands has: a 0 end, and a step of 0, or of 1 for a loop that writes no <c>Step</c> and counts
/// an <c>Integer</c>, a <c>Long</c> or a <c>LongLong</c>. MS-VBA, recorded in Excel. Each program runs in the interpreter and in compiled code alike.
/// </summary>
[TestClass]
public sealed class ForLoopNotInitializedTests
{
    // Main prints what Probe appends: each line of the loop appends what it sees, and the handler the error and its line label. n guards every loop.
    private static string Probe(string declarations, params string[] lines) => Program(
    [
        "Public Sub Main()",
        "    Debug.Print Probe()",
        "End Sub",
        "Private Function Probe() As String",
        $"    Dim {declarations}, n As Long, acc As String",
        "    On Error GoTo H",
        .. lines,
        "    GoTo X",
        "H:",
        "    acc = acc & \"E\" & Err.Number & \"@\" & Erl & \"|\"",
        "X:",
        "    Probe = acc",
        "End Function",
    ]);

    // the loop of a counter i, with the label L as its body's first line.
    private static string[] IntoTheBody(string header, string counter = "i") =>
    [
        $"10 For {header}",
        "L:",
        $"20 n = n + 1: acc = acc & \"b\" & {counter} & \"|\": If n > 4 Then Exit For",
        $"30 Next {counter}",
        $"40 acc = acc & \"after {counter}=\" & {counter} & \"|\"",
    ];

    [TestMethod]
    [DataRow("", "b0|after i=1|", DisplayName = "a counter never given a value: one pass, at 0")]
    [DataRow("i = 7", "b7|after i=8|", DisplayName = "a counter of 7: one pass, stepped past the 0 end")]
    [DataRow("i = -3", "b-3|b-2|b-1|b0|after i=1|", DisplayName = "a counter below 0: counted up to the 0 end")]
    public async Task AGoToIntoTheBody_OfALongCounter_RunsTheLoopToA0End_StepBy1(string before, string expected)
        => await AssertRunsAsync(Probe("i As Long", [$"    {before}", "    GoTo L", .. IntoTheBody("i = 1 To 3")]), $"| {expected} |");

    [TestMethod]
    [DataRow("Integer", "b0|after i=1|", DisplayName = "an Integer: stepped by 1")]
    [DataRow("LongLong", "b0|after i=1|", DisplayName = "a LongLong: stepped by 1")]
    [DataRow("Byte", "b0|b0|b0|b0|b0|after i=0|", DisplayName = "a Byte: stepped by 0")]
    [DataRow("Single", "b0|b0|b0|b0|b0|after i=0|", DisplayName = "a Single: stepped by 0")]
    [DataRow("Double", "b0|b0|b0|b0|b0|after i=0|", DisplayName = "a Double: stepped by 0")]
    [DataRow("Currency", "b0|b0|b0|b0|b0|after i=0|", DisplayName = "a Currency: stepped by 0")]
    public async Task AGoToIntoTheBody_OfALoopThatWritesNoStep_StepsByWhatTheCountersTypeGives(string type, string expected)
        => await AssertRunsAsync(Probe($"i As {type}", ["    GoTo L", .. IntoTheBody("i = 1 To 3")]), $"| {expected} |");

    [TestMethod]
    public async Task AGoToIntoTheBody_OfADateCounter_StepsItBy0()
        => await AssertRunsAsync(Probe("i As Date",
        [
            "    GoTo L",
            "10 For i = 1 To 3",
            "L:",
            "20 n = n + 1: acc = acc & \"b\" & CDbl(i) & \"|\": If n > 4 Then Exit For",
            "30 Next i",
            "40 acc = acc & \"after i=\" & CDbl(i) & \"|\"",
        ]), "| b0|b0|b0|b0|b0|after i=0| |");

    [TestMethod]
    public async Task AGoToIntoTheBody_OfALoopThatWritesAStep_StepsTheCounterBy0()
        // the counter never leaves 0, which is never past the 0 end.
        => await AssertRunsAsync(Probe("i As Long", ["    GoTo L", .. IntoTheBody("i = 1 To 3 Step 2")]), "| b0|b0|b0|b0|b0|after i=0| |");

    [TestMethod]
    public async Task AGoToOntoTheNext_OfALoopThatCountsDown_StepsTheCounterBy0()
        // the Next, run first, goes back into the body.
        => await AssertRunsAsync(Probe("i As Long",
        [
            "    GoTo L",
            "10 For i = 3 To 1 Step -1",
            "20 n = n + 1: acc = acc & \"b\" & i & \"|\": If n > 4 Then Exit For",
            "L:",
            "30 Next i",
            "40 acc = acc & \"after i=\" & i & \"|\"",
        ]), "| b0|b0|b0|b0|b0|after i=0| |");

    [TestMethod]
    public async Task AGoToIntoTheBody_OfAVariantCounter_IsError92AtTheNext()
        // the body runs, the counter Empty, and its Next raises the error.
        => await AssertRunsAsync(Probe("k", ["    GoTo L", .. IntoTheBody("k = 1 To 3", "k")]), "| b|E92@30| |");

    [TestMethod]
    public async Task ALoopEnteredTwice_JumpedIntoTheSecondTime_GoesOnWithTheLoopItHadTheFirstTime()
        // the second pass's Next steps on the end and the step the For statement gave the loop on the first.
        => await AssertRunsAsync(Probe("i As Long, pass As Long",
        [
            "    For pass = 1 To 2",
            "    If pass = 2 Then GoTo L",
            "10 For i = 1 To 3",
            "L:",
            "20 n = n + 1: acc = acc & \"p\" & pass & \"b\" & i & \"|\": If n > 8 Then Exit For",
            "30 Next i",
            "    Next pass",
            "40 acc = acc & \"after i=\" & i & \"|\"",
        ]), "| p1b1|p1b2|p1b3|p2b4|after i=5| |");

    [TestMethod]
    public async Task AResumeToALabelInTheBody_RunsTheLoopToA0End()
        // the handler resumes into the loop; its Next steps the counter past the 0 end.
        => await AssertRunsAsync(Program(
        [
            "Public Sub Main()",
            "    Debug.Print Probe()",
            "End Sub",
            "Private Function Probe() As String",
            "    Dim i As Long, n As Long, acc As String",
            "    On Error GoTo H",
            "    Err.Raise 5",
            .. IntoTheBody("i = 1 To 3"),
            "    GoTo X",
            "H:",
            "    acc = acc & \"E\" & Err.Number & \"@\" & Erl & \"|\"",
            "    If n = 0 Then Resume L",
            "X:",
            "    Probe = acc",
            "End Function",
        ]), "| E5@0|b0|after i=1| |");

    [TestMethod]
    [DataRow("L:", "20 n = n + 1: acc = acc & \"b\" & x & \"|\": If n > 4 Then Exit For", "b|E92@30|", DisplayName = "into the body")]
    [DataRow("20 n = n + 1: acc = acc & \"b\" & x & \"|\": If n > 4 Then Exit For", "L:", "E92@30|", DisplayName = "onto the Next")]
    public async Task AGoToIntoAForEach_IsError92AtTheNext(string first, string second, string expected)
        => await AssertRunsAsync(Probe("a(1 To 2) As Long, x As Variant",
            ["    a(1) = 1: a(2) = 2", "    GoTo L", "10 For Each x In a", $"{first}", $"{second}", "30 Next x", "40 acc = acc & \"after|\""]),
            $"| {expected} |");

    // the program run in the interpreter and in compiled code, alike, as MS-VBA runs it.
    private static async Task AssertRunsAsync(string program, string expected)
    {
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        StringAssert.Contains(interpreted, expected, interpreted);

        CompiledExecutionTests.RequirePayloads();
        Assert.AreEqual(interpreted, await RunAsync(program, VBExecutionEngine.Compiled));
    }

    // Erl counts line-number labels, as MS-VBA's does.
    private static async Task<string> RunAsync(string program, VBExecutionEngine engine)
    {
        var observed = string.Empty;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (_, run, _) =>
        {
            observed = Observed(run);
            return Task.CompletedTask;
        }, engine: engine, environment: profile => profile with { ErlLineNumbering = VBErlLineNumbering.LineLabel });
        return observed;
    }
}
