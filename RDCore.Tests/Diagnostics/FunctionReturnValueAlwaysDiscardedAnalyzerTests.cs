using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00217</c>: a <c>Friend</c> <c>Function</c> that every call of the project calls as a statement, so that no caller uses the value it returns.
/// </summary>
[TestClass]
public sealed class FunctionReturnValueAlwaysDiscardedAnalyzerTests
{
    private static readonly string Counter = ModuleWorkspace.ClassModule("Counter",
        "Private mTotal As Long",
        "Friend Function Add(ByVal n As Long) As Long", "mTotal = mTotal + n", "Add = mTotal", "End Function",
        "Friend Function Total() As Long", "Total = mTotal", "End Function",
        "Friend Sub Reset()", "mTotal = 0", "End Sub",
        "Friend Function Unused() As Long", "End Function");

    // the names the findings in the Counter module are about, and the lines they are on.
    private static async Task<(string Name, int Line)[]> FoundAsync(params string[] main)
    {
        var program = ModuleWorkspace.StandardModule("Program", ["Public Sub Main()", "Dim c As New Counter", .. main, "End Sub"]);
        var payload = await ModuleWorkspace.SemanticsAsync([("Counter", Counter)], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Counter");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Counter.cls"));

        var findings = new FunctionReturnValueAlwaysDiscardedAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, Counter), model)).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.FunctionReturnValueAlwaysDiscarded && finding.Severity == DiagnosticSeverity.Information));
        return [.. findings.Select(finding => (finding.Message.Split('\'')[1], finding.Range.Start.Line))];
    }

    [TestMethod]
    public async Task AFunctionEveryCallDiscards_IsFound_WhereItIsDeclared()
        => CollectionAssert.AreEqual(new[] { ("Add", 6) }, await FoundAsync("c.Add 1", "Call c.Add(2)", "Debug.Print c.Total", "c.Reset"));

    [TestMethod]
    public async Task AFunctionWhoseValueOneCallUses_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("c.Add 1", "Debug.Print c.Add(2)", "Debug.Print c.Total"));

    [TestMethod]
    public async Task AFunctionNoCodeCalls_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Debug.Print c.Add(1)", "Debug.Print c.Total"));
}
