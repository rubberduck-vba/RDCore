using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.4.4.1, §5.4.2.3: an error in the start, the end or the step of a <c>For</c> statement, under <c>On Error Resume Next</c>, goes on at the
/// statement following it, the first of its body, as MS-VBA has it (observed in Excel):
/// <list type="bullet">
/// <item>nothing the statement evaluates is kept: the counter is not assigned, and neither the end nor the step is, even one evaluated before the error;</item>
/// <item>the body runs, with no test of the counter against the end;</item>
/// <item>its <c>Next</c> steps the counter and tests it with the end and the step the loop had: what an earlier pass through the statement left, 0 when there
/// was none, and a step of 1 for a loop that has no <c>Step</c>.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ForHeaderResumeNextTests
{
    private static async Task<string> Run(string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nOn Error Resume Next\r\nDim n As Long, i As Long, k As Long, pass As Long\r\n"
            + string.Join("\r\n", body.Split(" ;; ")) + "\r\nDebug.Print \"after \" & Err.Number & \" i=\" & i & \" k=\" & k\r\nEnd Sub\r\n";
        return string.Join("/", await RunAsync([], program));
    }

    // each loop is bounded by k, so a step of 0 cannot hang the test.
    private const string Body = "k = k + 1 ;; Debug.Print \"i\" & i ;; If k > 2 Then Exit For ;; Next";

    [TestMethod]
    [DataRow("For i = 10 \\ n To 2", "i0/after 11 i=1 k=1", DisplayName = "F01 the start")]
    [DataRow("For i = 1 To 10 \\ n", "i0/after 11 i=1 k=1", DisplayName = "F02 the end")]
    [DataRow("i = 7 ;; For i = 10 \\ n To 2", "i7/after 11 i=8 k=1", DisplayName = "F04 the start, the counter keeps its value")]
    [DataRow("i = 7 ;; For i = 1 To 10 \\ n", "i7/after 11 i=8 k=1", DisplayName = "F05 the end, the counter not given the start")]
    [DataRow("For i = -3 To 10 \\ n", "i0/after 11 i=1 k=1", DisplayName = "F07 the end, a start below it")]
    public async Task AnErrorInTheHeader_OfALoopWithNoStep_RunsTheBodyOnce_AndNextStepsByOne(string header, string expected)
        => Assert.AreEqual(expected, await Run($"{header} ;; {Body}"));

    [TestMethod]
    [DataRow("For i = 1 To 3 Step 10 \\ n", "i0/i0/i0/after 11 i=0 k=3", DisplayName = "F03 the step")]
    [DataRow("For i = 5 To 1 Step 10 \\ n", "i0/i0/i0/after 11 i=0 k=3", DisplayName = "F06 the step, an end below the start")]
    public async Task AnErrorInTheStep_LeavesAStepOf0_AndTheCounterWhereItWas(string header, string expected)
        => Assert.AreEqual(expected, await Run($"{header} ;; {Body}"));

    [TestMethod]
    public async Task AnEndEvaluatedBeforeAnErrorInTheStep_IsNotKept()
        // X01: with an end of 3 kept, 2 would be within it and the loop would go on.
        => Assert.AreEqual("i2/after 11 i=2 k=1", await Run($"i = 2 ;; For i = 1 To 3 Step 10 \\ n ;; {Body}"));

    [TestMethod]
    public async Task AStepWrittenAsAConstant_IsNotKept_WhenTheStartFails()
        // X04: the step of -1 is evaluated after the start, and its error leaves it 0.
        => Assert.AreEqual("i5/after 11 i=5 k=1", await Run($"i = 5 ;; For i = 10 \\ n To 1 Step -1 ;; {Body}"));

    [TestMethod]
    [DataRow("For i = 1 To 10 \\ n", DisplayName = "X02 the end of the first pass")]
    [DataRow("For i = 1 To 10 Step 10 \\ n", DisplayName = "X03 the step of the first pass")]
    public async Task AnErrorInTheHeader_OfASecondPass_LeavesTheLoopWhatTheFirstPassGaveIt(string header)
        // the first pass runs with n = 1 (an end of 10, a step of 1) and leaves at i = 2; the second, with n = 0, keeps them.
        => Assert.AreEqual("p1i1/p1i2/p2i2/p2i3/p2i4/p2i5/p2i6/after 11 i=6 k=7", await Run(
            $"For pass = 1 To 2 ;; If pass = 1 Then n = {(header.Contains("Step") ? 10 : 1)} Else n = 0 ;; {header} ;; k = k + 1 ;; Debug.Print \"p\" & pass & \"i\" & i"
            + " ;; If pass = 1 And i >= 2 Then Exit For ;; If k > 6 Then Exit For ;; Next ;; Next"));
}
