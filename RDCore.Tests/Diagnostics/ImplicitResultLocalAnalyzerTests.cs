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
    private static async Task<AnalyzerFinding[]> FindingsAsync(params string[] lines)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new ImplicitResultLocalAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

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
