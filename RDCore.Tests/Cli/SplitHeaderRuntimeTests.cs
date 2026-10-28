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
        // MS-VBA says "Sub or Function not defined" of a call of the dead name, and nothing the dead header's body declares is declared for it.
        var source = string.Join("\r\n",
            "#If VBA7 Then", "Private Function F() As Long", "#Else", "Private Function G() As Long", "#End If", "    Dim n As Long", "    n = 1", "    F = n",
            "End Function",
            "Public Sub Main()", "    Debug.Print G()", "End Sub", "");
        IReadOnlyList<string> defined = [];
        IReadOnlyList<string> orphans = [];
        await ModuleWorkspace.WithHostAsync([], source, provider =>
            {
                Assert.IsTrue(provider.Session.Symbols.TryGetModule("Program", out var module));
                defined = [.. provider.Session.Symbols.MembersOf(module.Uri).Select(member => member.Name)];
                orphans = [.. provider.Session.Symbols.DeclaredIn(module.Uri).Select(symbol => symbol.Uri.Fragment)
                    .Where(fragment => fragment.StartsWith("#Program.G", StringComparison.OrdinalIgnoreCase))];
                var live = provider.Session.Symbols.MembersOf(module.Uri).OfType<SDK.Model.Symbols.Abstract.VBReturningMemberSymbol>().Single(member => member.Name == "F");
                Assert.IsTrue(live.Locals.Any(local => local.Name == "n"), "the live procedure has the body's local");
                return Task.CompletedTask;
            });

        CollectionAssert.Contains(defined.ToArray(), "F");
        CollectionAssert.DoesNotContain(defined.ToArray(), "G");
        Assert.IsEmpty(orphans);
        var errors = await ModuleWorkspace.LoadErrorsAsync([], source);
        Assert.IsTrue(errors.Any(error => error.Contains(SDK.Model.Errors.VBCompileErrorInfo.GetErrorString(SDK.Model.Errors.VBCompileErrorId.SubOrFunctionNotDefined))),
            string.Join("; ", errors));
    }

    [TestMethod]
    public async Task AHeaderWrittenInADeadBranchOnly_LeavesItsBodyOutsideAnyProcedure()
    {
        // MS-VBA: "Invalid outside procedure", at the first statement of the body.
        var errors = await ModuleWorkspace.LoadErrorsAsync([], string.Join("\r\n",
            "#If Mac Then", "Private Function F() As Long", "#End If", "    F = 1", "End Function",
            "Public Sub Main()", "End Sub", ""));

        Assert.IsTrue(errors.Any(error => error.Contains(SDK.Model.Errors.VBCompileErrorInfo.GetErrorString(SDK.Model.Errors.VBCompileErrorId.InvalidOutsideProcedure))),
            string.Join("; ", errors));
    }

    [TestMethod]
    [DataRow("    F = 1", DisplayName = "a dead body")]
    [DataRow("", DisplayName = "no body")]
    public async Task AHeaderAndItsBodyInADeadBranch_LeaveALiveEndOutsideAnyProcedure(string body)
    {
        // MS-VBA: "Invalid outside procedure", of the End Function.
        var errors = await ModuleWorkspace.LoadErrorsAsync([], string.Join("\r\n",
            new[] { "#If Mac Then", "Private Function F() As Long", body, "#End If", "End Function", "Public Sub Main()", "End Sub", "" }.Where(line => line.Length > 0 || body.Length > 0)));

        Assert.IsTrue(errors.Any(error => error.Contains(SDK.Model.Errors.VBCompileErrorInfo.GetErrorString(SDK.Model.Errors.VBCompileErrorId.InvalidOutsideProcedure))),
            string.Join("; ", errors));
    }

    [TestMethod]
    public async Task AProcedureWrittenWholeInADeadBranch_IsNoError()
        => Assert.IsEmpty(await ModuleWorkspace.LoadErrorsAsync([], string.Join("\r\n",
            "#If Mac Then", "Private Function F() As Long", "    F = 1", "End Function", "#End If", "Public Sub Main()", "End Sub", "")));

    [TestMethod]
    [Ignore("the static semantics do not check a procedure's closing statement against the kind of its live header: MS-VBA says \"Expected End Sub\"")]
    public async Task ALiveSubHeaderClosedByEndFunction_IsExpectedEndSub()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], string.Join("\r\n",
            "#If Mac Then", "Private Function F() As Long", "#Else", "Private Sub F()", "#End If", "    Debug.Print 1", "End Function",
            "Public Sub Main()", "    F", "End Sub", ""));

        Assert.IsTrue(errors.Any(error => error.Contains("Expected End Sub")), string.Join("; ", errors));
    }

    // ---- a name a module declares only in dead branches: it matches nothing, and a procedure declares it ----

    private static readonly string NotDefined = SDK.Model.Errors.VBCompileErrorInfo.GetErrorString(SDK.Model.Errors.VBCompileErrorId.SubOrFunctionNotDefined);

    [TestMethod]
    public async Task AReDimOfAFieldOnlyADeadBranchDeclares_DeclaresALocalArray()
        => Assert.AreEqual("3 5", await RunAsync(
            "#If Mac Then", "Public arr() As Long", "#End If",
            "Private Function Probe() As String", "    ReDim arr(3)", "    arr(2) = 5", "    Probe = UBound(arr) & \" \" & arr(2)", "End Function"));

    [TestMethod]
    public async Task AVariableOnlyADeadBranchDeclares_IsImplicitWhereItIsUsed()
        => Assert.AreEqual("2", await RunAsync(
            "#If Mac Then", "Dim v As Long", "#End If",
            "Private Function Probe() As String", "    v = Array(1, 2)", "    Probe = CStr(v(1))", "End Function"));

    [TestMethod]
    public async Task AFieldALiveBranchDeclares_IsTheModules()
        // the procedures share it: no procedure declares one of its own.
        => Assert.AreEqual("5", await RunAsync(
            "#If VBA7 Then", "Private arr() As Long", "#End If",
            "Private Sub Fill()", "    ReDim arr(3)", "    arr(2) = 5", "End Sub",
            "Private Function Probe() As String", "    Fill", "    Probe = CStr(arr(2))", "End Function"));

    [TestMethod]
    [DataRow("    v = Array(1)|    Probe = CStr(v(0))", "1", DisplayName = "an implicit local")]
    [DataRow("    ReDim a(2)|    a(1) = 4|    Probe = CStr(a(1))", "4", DisplayName = "a ReDim's target")]
    [DataRow("    Dim v As Variant|    v = Array(7)|    Probe = CStr(v(0))", "7", DisplayName = "a Variant that holds an array")]
    public async Task ANameThatIsDeclared_IsCalledOrIndexed_AsWithoutAnyIf(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(["#If Mac Then", "#End If", "Private Function Probe() As String", .. body.Split('|'), "End Function"]));

    [TestMethod]
    public async Task AFunctionOfAnotherModule_IsCalled_WhereTheModuleHasADeadBranch()
    {
        var output = await ModuleWorkspace.RunAsync([], [("Helper", ModuleWorkspace.StandardModule("Helper", "Public Function Twice(ByVal n As Long) As Long", "    Twice = n * 2", "End Function"))],
            string.Join("\r\n", "#If Mac Then", "#End If", "Public Sub Main()", "    Debug.Print Twice(3)", "End Sub", ""));

        CollectionAssert.AreEqual(new[] { "6" }, output);
    }

    [TestMethod]
    public async Task ADeclare_IsAFunctionThatIsDeclared_WhereTheModuleHasADeadBranch()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], string.Join("\r\n",
            "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long", "#If Mac Then", "#End If",
            "Private Sub Never()", "    Debug.Print GetTickCount()", "End Sub", "Public Sub Main()", "End Sub", ""));

        Assert.IsFalse(errors.Any(error => error.Contains(NotDefined)), string.Join("; ", errors));
    }

    // ---- what the host defines of one name ----

    [TestMethod]
    public async Task AVariableAndAProcedureOfOneName_AreANameDeclaredTwice_WhereTheModuleHasADeadBranch()
    {
        var source = string.Join("\r\n", "#If Mac Then", "#End If", "Private x As Long", "Private Sub x()", "End Sub", "Public Sub Main()", "End Sub", "");
        var kinds = new List<string>();
        await ModuleWorkspace.WithHostAsync([], source, provider =>
        {
            Assert.IsTrue(provider.Session.Symbols.TryGetModule("Program", out var module));
            kinds.AddRange(provider.Session.Symbols.MembersOf(module.Uri).Where(member => member.Name == "x").Select(member => member.GetType().Name));
            return Task.CompletedTask;
        });
        var errors = await ModuleWorkspace.LoadErrorsAsync([], source);

        CollectionAssert.AreEqual(new[] { nameof(SDK.Model.Symbols.VBProject.VBModuleFieldVariableMemberSymbol) }, kinds, "the first stands, and the other is not fused into it");
        Assert.IsTrue(errors.Any(error => error.Contains(SDK.Model.Errors.VBCompileErrorInfo.GetErrorString(SDK.Model.Errors.VBCompileErrorId.AmbiguousName))),
            string.Join("; ", errors));
    }

    // ---- what is checked of a procedure is its live header ----

    [TestMethod]
    [DataRow("ByRef a() As Long", "ByVal a() As Long", SDK.Model.Errors.VBCompileErrorId.ArrayArgumentMustBeByRef, DisplayName = "an array parameter passed by value")]
    [DataRow("Optional ByVal n As Long = 1", "Optional ByVal n As Long = Len", SDK.Model.Errors.VBCompileErrorId.SyntaxError, DisplayName = "a reserved name with no argument list")]
    public async Task WhatIsWrongWithTheLiveHeader_IsAnErrorOfTheModule_AndWhatIsWrongWithTheDeadOneIsNot(
        string fine, string wrong, SDK.Model.Errors.VBCompileErrorId expected)
    {
        string[] Module(string first, string second) =>
            ["#If Mac Then", $"Private Function F({first}) As Long", "#Else", $"Private Function F({second}) As Long", "#End If", "    F = 1", "End Function",
                "Public Sub Main()", "End Sub", ""];
        var live = await ModuleWorkspace.LoadErrorsAsync([], string.Join("\r\n", Module(fine, wrong)));
        var dead = await ModuleWorkspace.LoadErrorsAsync([], string.Join("\r\n", Module(wrong, fine)));

        Assert.IsTrue(live.Any(error => error.Contains(SDK.Model.Errors.VBCompileErrorInfo.GetErrorString(expected))), string.Join("; ", live));
        Assert.IsEmpty(dead);
    }

    [TestMethod]
    public async Task TheSizesOfABodysDeclarations_AreReducedInTheScopeOfTheLiveHeader()
        // the local constant is the live procedure's, which a differently named dead header does not declare.
        => Assert.AreEqual("3", await RunAsync(
            "#If Mac Then", "Private Function F() As Long", "#Else", "Private Function G() As Long", "#End If",
            "    Const N = 3", "    Dim s As String * N", "    G = Len(s)", "End Function",
            "Private Function Probe() As String", "    Probe = CStr(G())", "End Function"));
}
