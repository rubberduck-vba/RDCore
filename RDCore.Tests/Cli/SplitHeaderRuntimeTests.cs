namespace RDCore.Tests.Cli;

/// <summary>
/// A procedure header written once in each branch of a <c>#If</c> block, around one body (<strong>MS-VBAL §3.4</strong>), run as MS-VBA runs it: the live
/// header, and the live branch's statements (recorded in Excel, 64-bit, where VBA7 and Win64 are True and Mac is False: oracle\cc-split).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.4 Conditional Compilation")]
public sealed class SplitHeaderRuntimeTests
{
    private const string LiveAlternative = "the host defines the live alternative of a procedure (S2): today the first header written is the procedure's";

    private static async Task<string> RunAsync(params string[] lines)
        => string.Join(" ", await ModuleWorkspace.RunAsync([], string.Join("\r\n", lines) + "\r\nPublic Sub Main()\r\n    Debug.Print Probe()\r\nEnd Sub\r\n"));

    [TestMethod]
    public async Task ThePtrSafeHeader_IsTheProcedure_WhereVBA7IsTrue()
        // S01
        => Assert.AreEqual("LongLong 10", await RunAsync(
            "#If VBA7 Then", "Private Function F(ByVal a As LongPtr) As LongPtr", "#Else", "Private Function F(ByVal a As Long) As Long", "#End If",
            "    F = a * 2", "End Function",
            "Private Function Probe() As String", "    Probe = TypeName(F(5)) & \" \" & F(5)", "End Function"));

    [TestMethod]
    public async Task TheLiveBranchsStatements_RunBeforeTheOnesAfterTheBlock()
        // S11
        => Assert.AreEqual("11", await RunAsync(
            "#If VBA7 Then", "Private Function F() As Long", "    F = 1", "#Else", "Private Function F() As Long", "    F = 2", "#End If", "    F = F + 10", "End Function",
            "Private Function Probe() As String", "    Probe = CStr(F())", "End Function"));

    [TestMethod]
    public async Task ANestedBlock_GivesTheProcedureTheHeaderOfItsLiveBranch()
        // S10
        => Assert.AreEqual("LongLong", await RunAsync(
            "#If VBA7 Then", "#If Win64 Then", "Private Function F() As LongLong", "#Else", "Private Function F() As Long", "#End If",
            "#Else", "Private Function F() As Integer", "#End If", "    F = 7", "End Function",
            "Private Function Probe() As String", "    Probe = TypeName(F())", "End Function"));

    [TestMethod]
    [Ignore(LiveAlternative)]
    public async Task TheElseBranchsHeader_IsTheProcedure_WhereTheIfBranchIsDead()
        // S02
        => Assert.AreEqual("String x!", await RunAsync(
            "#If Mac Then", "Private Function F(ByVal a As Long) As Long", "#Else", "Private Function F(ByVal a As String) As String", "#End If",
            "    F = a & \"!\"", "End Function",
            "Private Function Probe() As String", "    Probe = TypeName(F(\"x\")) & \" \" & F(\"x\")", "End Function"));

    [TestMethod]
    [Ignore(LiveAlternative)]
    public async Task ADeadSubHeader_LeavesTheLiveFunction()
        // S04
        => Assert.AreEqual("3", await RunAsync(
            "#If Mac Then", "Private Sub F()", "#Else", "Private Function F() As Long", "#End If", "    F = 3", "End Function",
            "Private Function Probe() As String", "    Probe = CStr(F())", "End Function"));

    [TestMethod]
    [Ignore(LiveAlternative)]
    public async Task TheLiveBranchOfAnElseIfChain_IsTheProcedure()
        // S09
        => Assert.AreEqual("LongLong", await RunAsync(
            "#If Mac Then", "Private Function F() As Integer", "#ElseIf Win64 Then", "Private Function F() As LongLong", "#Else", "Private Function F() As Long", "#End If",
            "    F = 7", "End Function",
            "Private Function Probe() As String", "    Probe = TypeName(F())", "End Function"));
}
