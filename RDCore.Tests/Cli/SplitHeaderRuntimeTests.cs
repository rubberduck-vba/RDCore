namespace RDCore.Tests.Cli;

/// <summary>
/// A procedure header written once in each branch of a <c>#If</c> block, around one body (<strong>MS-VBAL §3.4</strong>), run as MS-VBA runs it: the live
/// header, and the live branch's statements (recorded in Excel, 64-bit, where VBA7 and Win64 are True and Mac is False).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.4 Conditional Compilation")]
public sealed class SplitHeaderRuntimeTests
{
    private static async Task<string> RunAsync(params string[] lines)
        => string.Join(" ", await ModuleWorkspace.RunAsync([], string.Join("\r\n", lines) + "\r\nPublic Sub Main()\r\n    Debug.Print Probe()\r\nEnd Sub\r\n"));

    [TestMethod]
    public async Task ThePtrSafeHeader_IsTheProcedure_WhereVBA7IsTrue()
        => Assert.AreEqual("LongLong 10", await RunAsync(
            "#If VBA7 Then", "Private Function F(ByVal a As LongPtr) As LongPtr", "#Else", "Private Function F(ByVal a As Long) As Long", "#End If",
            "    F = a * 2", "End Function",
            "Private Function Probe() As String", "    Probe = TypeName(F(5)) & \" \" & F(5)", "End Function"));

    [TestMethod]
    public async Task TheLiveBranchsStatements_RunBeforeTheOnesAfterTheBlock()
        => Assert.AreEqual("11", await RunAsync(
            "#If VBA7 Then", "Private Function F() As Long", "    F = 1", "#Else", "Private Function F() As Long", "    F = 2", "#End If", "    F = F + 10", "End Function",
            "Private Function Probe() As String", "    Probe = CStr(F())", "End Function"));

    [TestMethod]
    public async Task ANestedBlock_GivesTheProcedureTheHeaderOfItsLiveBranch()
        => Assert.AreEqual("LongLong", await RunAsync(
            "#If VBA7 Then", "#If Win64 Then", "Private Function F() As LongLong", "#Else", "Private Function F() As Long", "#End If",
            "#Else", "Private Function F() As Integer", "#End If", "    F = 7", "End Function",
            "Private Function Probe() As String", "    Probe = TypeName(F())", "End Function"));

    [TestMethod]
    public async Task TheElseBranchsHeader_IsTheProcedure_WhereTheIfBranchIsDead()
        => Assert.AreEqual("String x!", await RunAsync(
            "#If Mac Then", "Private Function F(ByVal a As Long) As Long", "#Else", "Private Function F(ByVal a As String) As String", "#End If",
            "    F = a & \"!\"", "End Function",
            "Private Function Probe() As String", "    Probe = TypeName(F(\"x\")) & \" \" & F(\"x\")", "End Function"));

    [TestMethod]
    public async Task ADeadSubHeader_LeavesTheLiveFunction()
        => Assert.AreEqual("3", await RunAsync(
            "#If Mac Then", "Private Sub F()", "#Else", "Private Function F() As Long", "#End If", "    F = 3", "End Function",
            "Private Function Probe() As String", "    Probe = CStr(F())", "End Function"));

    [TestMethod]
    public async Task TheLiveBranchOfAnElseIfChain_IsTheProcedure()
        => Assert.AreEqual("LongLong", await RunAsync(
            "#If Mac Then", "Private Function F() As Integer", "#ElseIf Win64 Then", "Private Function F() As LongLong", "#Else", "Private Function F() As Long", "#End If",
            "    F = 7", "End Function",
            "Private Function Probe() As String", "    Probe = TypeName(F())", "End Function"));

    // ---- a declaration written whole in each branch ----

    [TestMethod]
    public async Task AProcedureWrittenInEachBranch_RunsTheLiveBranchsBody_WhereItIsFirst()
        // the dead copy was the procedure's body, empty, and returned 0.
        => Assert.AreEqual("1", await RunAsync(
            "#If VBA7 Then", "Private Function F() As Long", "    F = 1", "End Function", "#Else", "Private Function F() As Long", "    F = 2", "End Function", "#End If",
            "Private Function Probe() As String", "    Probe = CStr(F())", "End Function"));

    [TestMethod]
    public async Task AProcedureWrittenInEachBranch_IsTheLiveBranchs_WhereItIsLast()
        // the first branch's signature was the procedure's, and its String result a type mismatch.
        => Assert.AreEqual("String two", await RunAsync(
            "#If Mac Then", "Private Function F() As Long", "    F = 1", "End Function", "#Else", "Private Function F() As String", "    F = \"two\"", "End Function",
            "#End If",
            "Private Function Probe() As String", "    Probe = TypeName(F()) & \" \" & F()", "End Function"));

    [TestMethod]
    public async Task ATypesFieldWrittenInEachBranch_IsTheLiveBranchs()
        => Assert.AreEqual("Long", await RunAsync(
            "Private Type Pair", "#If Mac Then", "    P As Integer", "#Else", "    P As Long", "#End If", "End Type",
            "Private Function Probe() As String", "    Dim T As Pair", "    Probe = TypeName(T.P)", "End Function"));

    [TestMethod]
    public async Task ANameOnlyADeadBranchDeclares_IsNotDeclared()
    {
        // S06: MS-VBA says "Sub or Function not defined" of a call of the dead name.
        RDCore.SDK.Platform.Protocol.ExecuteSessionResult? result = null;
        IReadOnlyList<string> defined = [];
        await ModuleWorkspace.InspectAsync([], string.Join("\r\n",
            "#If VBA7 Then", "Private Function F() As Long", "#Else", "Private Function G() As Long", "#End If", "    F = 1", "End Function",
            "Public Sub Main()", "    Debug.Print G()", "End Sub", ""), (provider, run, _) =>
            {
                result = run;
                Assert.IsTrue(provider.Session.Symbols.TryGetModule("Program", out var module));
                defined = [.. provider.Session.Symbols.MembersOf(module.Uri).Select(member => member.Name)];
                return Task.CompletedTask;
            });

        CollectionAssert.Contains(defined.ToArray(), "F");
        CollectionAssert.DoesNotContain(defined.ToArray(), "G");
        Assert.AreNotEqual(SDK.Platform.Protocol.ExecutionOutcome.Completed, result!.Outcome);
    }
}
