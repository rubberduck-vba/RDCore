using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// A <c>Function</c> or a <c>Property Get</c> called in the <c>TruePart</c> or the <c>FalsePart</c> of <c>IIf</c> (<c>RDC11039</c>), which runs whatever the
/// condition: a function of the project or of a library, or a member bound late, but no function of the VBA library that changes nothing and raises no error.
/// </summary>
[TestClass]
public sealed class IIfSideEffectAnalyzerTests
{
    private static readonly (string, string) Counter = ("Counter", ModuleWorkspace.ClassModule("Counter",
        "Private mTotal As Long",
        "Public Property Get Total() As Long", "mTotal = mTotal + 1", "Total = mTotal", "End Property"));

    // the names the findings in the Program module are about, in the order they are written.
    private static async Task<string[]> FoundAsync(params string[] main)
    {
        string[] lines =
        [
            "Option Explicit",
            "Public Sub Main()",
            "    Dim x As Variant",
            "    Dim c As New Counter",
            "    Dim o As Object",
            .. main.Select(line => "    " + line),
            "End Sub",
            "Public Function Compute(ByVal n As Long) As Long",
            "    Compute = n * 2",
            "End Function",
            "Public Function IsReady() As Boolean",
            "    IsReady = True",
            "End Function",
        ];
        var source = ModuleWorkspace.StandardModule("Program", lines);
        var payload = await ModuleWorkspace.SemanticsAsync([Counter], source);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));

        var findings = new IIfSideEffectAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, model)).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.IIfSideEffect && finding.Severity == DiagnosticSeverity.Warning));
        return [.. findings.Select(finding => finding.Message.Split('\'')[1])];
    }

    [TestMethod]
    public async Task AFunctionOfTheProjectInEitherPart_IsFound_AndNotInTheCondition()
        => CollectionAssert.AreEqual(
            new[] { "Compute", "Compute" },
            await FoundAsync("x = IIf(IsReady(), Compute(1), 2)", "x = IIf(IsReady, 1, 1 + Compute(2))"));

    [TestMethod]
    public async Task APropertyGetInAPart_IsFound()
        => CollectionAssert.AreEqual(new[] { "Total" }, await FoundAsync("x = IIf(True, c.Total, 0)"));

    [TestMethod]
    public async Task APartGivenByName_IsTheOneItNames()
        => CollectionAssert.AreEqual(new[] { "Compute" }, await FoundAsync("x = IIf(Compute(3) > 0, FalsePart:=Compute(1), TruePart:=0)"));

    [TestMethod]
    public async Task ALibraryFunctionThatChangesNothing_IsNotFound_AndOneThatCanIs()
        => CollectionAssert.AreEqual(
            new[] { "MsgBox" },
            await FoundAsync("x = VBA.IIf(False, Len(\"a\"), UCase$(\"b\"))", "x = IIf(False, IsNumeric(x), MsgBox(\"c\"))"));

    [TestMethod]
    public async Task AMemberBoundLateInAPart_IsFound_AndNotInTheCondition()
        => CollectionAssert.AreEqual(
            new[] { "Compute", "Key", "Count" },
            await FoundAsync("x = IIf(o.IsReady, o.Compute(1), 0)", "x = IIf(o Is Nothing, 0, o!Key)", "x = IIf(True, x.Count, 0)"));

    [TestMethod]
    public async Task APartWithNoCall_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("x = IIf(IsReady(), 1, x)"));
}
