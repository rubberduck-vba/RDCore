using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Model;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11030</c>: the host finds each <c>GoTo</c> and <c>Resume</c> that jumps into a <c>For</c> or a <c>For Each</c> loop from outside it when
/// it loads the code, and the analyzer reports it at the statement: a warning, as MS-VBAL has the loop's <c>Next</c> raise error 92 where MS-VBA
/// steps a counter that is not a <c>Variant</c>.
/// </summary>
[TestClass]
public sealed class JumpIntoForLoopAnalyzerTests
{
    private static string Module(params string[] body) => string.Join("\r\n",
    [
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Public Sub Work()",
        "    Dim i As Long, k As Variant, a(1 To 2) As Long, n As Long",
        .. body,
        "End Sub",
    ]) + "\r\n";

    private static async Task<AnalyzerFinding[]> FindAsync(string source)
    {
        var uri = TestUri.TestModuleUri();
        var payload = await ModuleWorkspace.SemanticsAsync([], source);

        return [.. new JumpIntoForLoopAnalyzer()
            .Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), payload.Modules.Single()))
            .OrderBy(finding => finding.Range.Start)];
    }

    // the line, counted from 0, that the module has text on.
    private static int LineOf(string source, string text)
        => Array.FindIndex(source.Split("\r\n"), line => line.Trim() == text);

    [TestMethod]
    [DataRow("    GoTo L", "    For i = 1 To 3", "L:", "        n = n + 1", "    Next i", DisplayName = "into the body of a Long counter's loop")]
    [DataRow("    GoTo L", "    For k = 1 To 3", "        n = n + 1", "L:", "    Next k", DisplayName = "onto the Next of a Variant counter's loop")]
    [DataRow("    GoTo L", "    For Each k In a", "L:", "        n = n + 1", "    Next k", DisplayName = "into the body of a For Each")]
    [DataRow("    If n = 0 Then GoTo L", "    For i = 1 To 3", "L:", "        n = n + 1", "    Next i", DisplayName = "from a single-line If")]
    public async Task AGoToIntoALoop_IsAWarning_AtTheGoTo(string jump, string opener, string first, string second, string next)
    {
        var source = Module(jump, opener, first, second, next);

        var finding = (await FindAsync(source)).Single();

        Assert.AreEqual(RDCoreDiagnosticId.JumpIntoForLoop.ToCode(), finding.Code);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(LineOf(source, jump.Trim()), finding.Range.Start.Line);
        Assert.AreEqual(AnalyzerMessages.Format(RDCoreDiagnosticsResources.JumpIntoForLoop_Message, "GoTo"), finding.Message);
    }

    [TestMethod]
    public async Task AResumeToALabelInALoop_FromAHandlerOutsideIt_IsAWarning_AtTheResume()
    {
        var source = Module(
            "    On Error GoTo H",
            "    Err.Raise 5",
            "    For i = 1 To 3",
            "L:",
            "        n = n + 1",
            "    Next i",
            "    Exit Sub",
            "H:",
            "    Resume L");

        var finding = (await FindAsync(source)).Single();

        Assert.AreEqual(LineOf(source, "Resume L"), finding.Range.Start.Line);
        Assert.AreEqual(AnalyzerMessages.Format(RDCoreDiagnosticsResources.JumpIntoForLoop_Message, "Resume"), finding.Message);
    }

    [TestMethod]
    [DataRow("    GoTo L", "L:", "    For i = 1 To 3", "        n = n + 1", "    Next i", DisplayName = "to the For statement")]
    [DataRow("    For i = 1 To 3", "        If n > 1 Then GoTo L", "        n = n + 1", "L:", "    Next i", DisplayName = "within the loop")]
    [DataRow("    For i = 1 To 3", "        GoTo L", "    Next i", "L:", "    n = n + 1", DisplayName = "out of the loop")]
    [DataRow("    For i = 1 To 3", "        n = n + 1", "    Next i", "    GoTo L", "L:", DisplayName = "after the loop")]
    public async Task AGoToThatDoesNotEnterALoopFromOutsideIt_IsNotReported(string first, string second, string third, string fourth, string fifth)
        => Assert.AreEqual(0, (await FindAsync(Module(first, second, third, fourth, fifth))).Length);

    [TestMethod]
    public async Task AGoToIntoAnInnerLoop_FromTheOuterLoopsBody_IsAWarning()
    {
        var source = Module(
            "    For n = 1 To 2",
            "        GoTo L",
            "        For i = 1 To 3",
            "L:",
            "        Next i",
            "    Next n");

        Assert.AreEqual(LineOf(source, "GoTo L"), (await FindAsync(source)).Single().Range.Start.Line);
    }
}
