using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// A <c>Function</c> of the project called as a statement (<c>RDC00216</c>), which discards the value it returns: not a function of a library, and
/// not a call whose value is used.
/// </summary>
[TestClass]
public sealed class FunctionReturnValueDiscardedAnalyzerTests
{
    private static readonly (string, string) Counter = ("Counter", ModuleWorkspace.ClassModule("Counter",
        "Private mTotal As Long",
        "Public Function Add(ByVal n As Long) As Long", "mTotal = mTotal + n", "Add = mTotal", "End Function"));

    // the names the findings in the Program module are about, and the lines they are on, in the order they are written.
    private static async Task<(string Name, int Line)[]> FoundAsync(params string[] main)
    {
        string[] lines =
        [
            "Option Explicit",
            "Public Sub Main()",
            "    Dim x As Variant",
            "    Dim c As New Counter",
            .. main.Select(line => "    " + line),
            "End Sub",
            "Public Function Save(ByVal path As String) As Boolean",
            "    Save = Len(path) > 0",
            "End Function",
            "Public Sub Log(ByVal text As String)",
            "End Sub",
        ];
        var source = ModuleWorkspace.StandardModule("Program", lines);
        var payload = await ModuleWorkspace.SemanticsAsync([Counter], source);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));

        var findings = new FunctionReturnValueDiscardedAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, model)).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.FunctionReturnValueDiscarded && finding.Severity == DiagnosticSeverity.Hint));
        return [.. findings.Select(finding => (finding.Message.Split('\'')[1], finding.Range.Start.Line))];
    }

    [TestMethod]
    public async Task AFunctionOfTheProjectCalledAsAStatement_IsFound_WithOrWithoutCall()
        => CollectionAssert.AreEqual(
            new[] { ("Save", 5), ("Save", 6), ("Add", 7) },
            await FoundAsync("Save \"a\"", "Call Save(\"b\")", "c.Add 1"),
            "the standard module's source has one line of header");

    [TestMethod]
    public async Task AFunctionWhoseValueIsUsed_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("x = Save(\"a\")", "If Save(\"b\") Then x = c.Add(1)", "Log CStr(Save(\"c\"))"));

    [TestMethod]
    public async Task AFunctionOfALibrary_AndASub_AreNotFound()
        => Assert.IsEmpty(await FoundAsync("MsgBox \"hello\"", "Log \"x\""));
}
