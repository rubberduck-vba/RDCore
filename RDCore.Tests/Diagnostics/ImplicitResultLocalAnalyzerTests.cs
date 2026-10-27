using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00115</c>: a function or a property getter that reads its own return value as the local variable it implicitly is (<strong>MS-VBAL §5.3.1</strong>).
/// </summary>
[TestClass]
public sealed class ImplicitResultLocalAnalyzerTests
{
    private static string Module(params string[] lines) => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";

    private static Task<AnalyzerFinding[]> FindingsAsync(params string[] lines) => FindingsOfAsync(Module(lines));

    private static async Task<AnalyzerFinding[]> FindingsOfAsync(string program)
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new ImplicitResultLocalAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    // the program with the edits of the fix made, the last first so that the positions of the others hold.
    private static string Fixed(string program, EditFix fix)
    {
        var lines = program.Split("\r\n").ToList();
        foreach (var edit in fix.Edits.OrderByDescending(edit => edit.Range.Start.Line).ThenByDescending(edit => edit.Range.Start.Character))
        {
            Assert.AreEqual(edit.Range.Start.Line, edit.Range.End.Line, "an edit of one line");
            var line = lines[edit.Range.Start.Line];
            lines[edit.Range.Start.Line] = line[..edit.Range.Start.Character] + edit.NewText + line[edit.Range.End.Character..];
        }

        return string.Join("\r\n", lines);
    }

    private static async Task AssertFixedAsync(string[] before, string[] after)
    {
        var program = Module(before);
        var fix = (await FindingsOfAsync(program)).Select(finding => finding.Fixes?.Edit).Distinct().Single();
        Assert.IsNotNull(fix, "the findings carry the fix");

        var fixedProgram = Fixed(program, fix);
        Assert.AreEqual(Module(after), fixedProgram);
        Assert.IsEmpty(await FindingsOfAsync(fixedProgram), "nothing is left to report");
        Assert.IsEmpty(await ModuleWorkspace.LoadErrorsAsync([], fixedProgram), "and it compiles");
    }

    [TestMethod]
    public async Task TheFix_DeclaresALocal_UsesIt_AndAssignsTheResultAtTheEnd()
        => await AssertFixedAsync(
            ["Public Function Total() As Long", "    Dim i As Long", "    For i = 1 To 3", "        Total = Total + i", "    Next", "End Function"],
            ["Public Function Total() As Long", "    Dim result As Long", "    Dim i As Long", "    For i = 1 To 3", "        result = result + i", "    Next", "    Total = result", "End Function"]);

    [TestMethod]
    public async Task TheFix_AssignsTheResultBeforeEachExit_WithSetForAnObject()
        => await AssertFixedAsync(
            ["Public Function Items(ByVal n As Long) As Collection", "    Set Items = New Collection", "    If n = 0 Then Exit Function", "    Items.Add n", "End Function"],
            ["Public Function Items(ByVal n As Long) As Collection", "    Dim result As Collection", "    Set result = New Collection", "    If n = 0 Then Set Items = result: Exit Function",
                "    result.Add n", "    Set Items = result", "End Function"]);

    [TestMethod]
    public async Task TheFix_OfAPropertyGet_AssignsBeforeExitProperty()
        => await AssertFixedAsync(
            ["Public Property Get Size() As Long", "    Size = 2", "    If Size > 1 Then", "        Exit Property", "    End If", "    Size = Size * 2", "End Property"],
            ["Public Property Get Size() As Long", "    Dim result As Long", "    result = 2", "    If result > 1 Then", "        Size = result: Exit Property", "    End If",
                "    result = result * 2", "    Size = result", "End Property"]);

    [TestMethod]
    public async Task TheFix_NamesTheLocalWhatNothingElseIsNamed_AndTypesItAsTheResultIs()
        => await AssertFixedAsync(
            ["Private result As Long", "Public Function Total$()", "    Total = \"a\"", "    Total = Total & \"b\"", "End Function"],
            ["Private result As Long", "Public Function Total$()", "    Dim result2 As String", "    result2 = \"a\"", "    result2 = result2 & \"b\"", "    Total = result2", "End Function"]);

    [TestMethod]
    public async Task AFunctionThatReadsItsResult_IsReported_WhereItReadsIt()
    {
        var findings = await FindingsAsync(
            "Public Function Total() As Long", "    Dim i As Long", "    For i = 1 To 3", "        Total = Total + i", "    Next", "    Debug.Print Total", "End Function");

        Assert.HasCount(2, findings);
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.ImplicitResultLocal && finding.Severity == DiagnosticSeverity.Information));
        CollectionAssert.AreEqual(new[] { 4, 6 }, findings.Select(finding => finding.Range.Start.Line).ToArray());
        StringAssert.Contains(findings[0].Message, "'Total'");
    }

    [TestMethod]
    public async Task APropertyGetThatReadsItsResult_IsReported()
        => Assert.ContainsSingle(await FindingsAsync("Public Property Get Size() As Long", "    Size = 2", "    Size = Size * 2", "End Property"));

    [TestMethod]
    public async Task AnObjectResultWhoseMemberIsCalled_IsReported()
        => Assert.ContainsSingle(await FindingsAsync(
            "Public Function Items() As Collection", "    Set Items = New Collection", "    Items.Add 1", "End Function"));

    [TestMethod]
    public async Task AFunctionThatOnlyAssignsItsResult_IsNotReported()
        => Assert.IsEmpty(await FindingsAsync("Public Function Total() As Long", "    Dim t As Long", "    t = 3", "    Total = t", "End Function"));

    [TestMethod]
    public async Task ARecursiveCall_IsNotReported()
        => Assert.IsEmpty(await FindingsAsync(
            "Public Function Factorial(ByVal n As Long) As Long", "    If n <= 1 Then", "        Factorial = 1", "    Else", "        Factorial = n * Factorial(n - 1)",
            "    End If", "End Function"));

    [TestMethod]
    public async Task ACallOfTheFunctionFromAnother_IsNotReported()
        => Assert.IsEmpty(await FindingsAsync(
            "Public Function Total() As Long", "    Total = 1", "End Function", "Public Function Twice() As Long", "    Twice = Total * 2", "End Function"));

    [TestMethod]
    public async Task ASubOrAPropertyLetNamedTheSame_IsNotReported()
        => Assert.IsEmpty(await FindingsAsync(
            "Private mSize As Long", "Public Property Get Size() As Long", "    Size = mSize", "End Property",
            "Public Property Let Size(ByVal value As Long)", "    mSize = value", "    Debug.Print Size", "End Property"));
}
