using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Server.Configuration;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00220</c> and <c>RDC00221</c>: what <c>Option Base 1</c> does not apply to. A param array is 0-based (MS-VBAL §5.3.1.11), and so is the array of
/// the library's <c>VBA.Array</c>; the unqualified <c>Array</c> keyword follows <c>Option Base</c> (RD-VBAL §6.1.2.13).
/// </summary>
[TestClass]
public sealed class ArrayOptionBaseAnalyzerTests
{
    private static readonly Uri Document = new(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

    // blocks exist only in a file that may have statements outside its procedures.
    private static AnalyzerFinding[] ParamArrayFindings(string program, bool fileLevelStatements = false)
    {
        var parser = new ModuleParser(workspaceOptions: Options.Create(new SdkWorkspaceOptions { FileLevelStatements = fileLevelStatements }));
        var parse = parser.Parse(Document, program);
        Assert.IsEmpty(parse.SyntaxErrors, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return [.. new ParamArrayOptionBaseAnalyzer().Analyze(new ModuleAnalysisContext(Document, parse, null))];
    }

    private static async Task<AnalyzerFinding[]> QualifiedArrayFindingsAsync(string options, string body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\n{options}\r\nPublic Sub Probe()\r\n{body}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        return [.. new QualifiedArrayOptionBaseAnalyzer().Analyze(new ModuleAnalysisContext(Document, new ModuleParser().Parse(Document, program), model))];
    }

    // the host's model of a module of parts has the Option Base that any part states (ModuleAssembly), which the file's own tree may not state: here the
    // model of a file that states none is given the Option Base of another part.
    private static async Task<AnalyzerFinding[]> PartsFindingsAsync(int optionBaseOfTheParts)
    {
        var program = Lines("Attribute VB_Name = \"Program\"", "Public Sub Probe(ParamArray values())", "Debug.Print LBound(VBA.Array(1, 2))", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        Assert.AreEqual(0, model.Directives?.Base, "the host states the Option Base of the file's tree");

        var parts = model with { Directives = model.Directives!.Value with { Base = optionBaseOfTheParts } };
        var context = new ModuleAnalysisContext(Document, new ModuleParser().Parse(Document, program), parts);
        return [.. new ParamArrayOptionBaseAnalyzer().Analyze(context), .. new QualifiedArrayOptionBaseAnalyzer().Analyze(context)];
    }

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    [TestMethod]
    [DataRow("Public Sub Probe(ParamArray values() As Variant)", DisplayName = "a Sub's")]
    [DataRow("Public Function Probe(ByVal first As Long, ParamArray values()) As Long", DisplayName = "a Function's, after a parameter")]
    public void UnderOptionBase1_AParamArray_IsAHint(string signature)
    {
        var finding = Assert.ContainsSingle(ParamArrayFindings(Lines("Option Base 1", signature, "End " + signature.Split(' ')[1])));

        Assert.AreEqual(RDCoreDiagnosticId.ParamArrayIgnoresOptionBase, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Hint, finding.Severity);
        Assert.AreEqual(1, finding.Range.Start.Line);
        Assert.AreEqual(signature.IndexOf("ParamArray", StringComparison.Ordinal), finding.Range.Start.Character);
        StringAssert.Contains(finding.Message, "'values'");
    }

    [TestMethod]
    [DataRow("", DisplayName = "no Option Base")]
    [DataRow("Option Base 0", DisplayName = "Option Base 0")]
    public void WithoutOptionBase1_AParamArray_IsNotReported(string options)
        => Assert.IsEmpty(ParamArrayFindings(Lines(options, "Public Sub Probe(ParamArray values() As Variant)", "End Sub")));

    [TestMethod]
    public void UnderOptionBase1_AParameterThatIsNoParamArray_IsNotReported()
        => Assert.IsEmpty(ParamArrayFindings(Lines("Option Base 1", "Public Sub Probe(values() As Variant, Optional ByVal other As Variant)", "End Sub")));

    [TestMethod]
    public void EachModuleOfAFile_ReadsItsOwnOptionBase()
    {
        // the file's module states Option Base 1 and the Helpers block does not; the Other block states its own.
        var program = Lines("Option Base 1", "Public Sub InTheFile(ParamArray a())", "End Sub",
            "Module Helpers", "Public Sub InHelpers(ParamArray b())", "End Sub", "End Module",
            "Module Other", "Option Base 1", "Public Sub InOther(ParamArray c())", "End Sub", "End Module");

        var findings = ParamArrayFindings(program, fileLevelStatements: true);

        CollectionAssert.AreEquivalent(new[] { 1, 9 }, findings.Select(finding => finding.Range.Start.Line).ToArray());
    }

    [TestMethod]
    public async Task UnderOptionBase1_ACallOfVBAArray_IsAHint()
    {
        var finding = Assert.ContainsSingle(await QualifiedArrayFindingsAsync("Option Base 1", "Debug.Print LBound(VBA.Array(1, 2))"));

        Assert.AreEqual(RDCoreDiagnosticId.QualifiedArrayIgnoresOptionBase, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Hint, finding.Severity);
        Assert.AreEqual(3, finding.Range.Start.Line);
        Assert.AreEqual("Debug.Print LBound(".Length, finding.Range.Start.Character);
        Assert.AreEqual("Debug.Print LBound(VBA.Array(1, 2)".Length, finding.Range.End.Character);
    }

    [TestMethod]
    [DataRow("Option Base 1", "Debug.Print LBound(Array(1, 2))", DisplayName = "the unqualified keyword under Option Base 1")]
    [DataRow("", "Debug.Print LBound(VBA.Array(1, 2))", DisplayName = "VBA.Array with no Option Base")]
    [DataRow("Option Base 0", "Debug.Print LBound(VBA.Array(1, 2))", DisplayName = "VBA.Array under Option Base 0")]
    [DataRow("Option Base 1", "Debug.Print VBA.Len(\"ab\")", DisplayName = "another member of the library")]
    public async Task ArrayThatFollowsOptionBase_OrNoOptionBase1_IsNotReported(string options, string body)
        => Assert.IsEmpty(await QualifiedArrayFindingsAsync(options, body));

    // RD-VBAL §5.1: a module of parts has one set of options (MS-VBAL §5.2.1), which any of its parts states, in whichever file.
    [TestMethod]
    public async Task UnderAnOptionBase1ThatAnotherPartStates_TheParamArrayAndTheCallOfVBAArray_AreHints()
    {
        var findings = await PartsFindingsAsync(optionBaseOfTheParts: 1);

        CollectionAssert.AreEquivalent(
            new[] { RDCoreDiagnosticId.ParamArrayIgnoresOptionBase, RDCoreDiagnosticId.QualifiedArrayIgnoresOptionBase },
            findings.Select(finding => finding.Id).ToArray());
    }

    [TestMethod]
    public async Task WhenNoPartStatesOptionBase1_TheParamArrayAndTheCallOfVBAArray_AreNotReported()
        => Assert.IsEmpty(await PartsFindingsAsync(optionBaseOfTheParts: 0));
}
