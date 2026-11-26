using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Model;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00416</c>: in a document that allows file-level statements, <c>'@ModuleDescription</c> and <c>'@IgnoreModule</c> of a block's module, which are
/// written <c>'@Description</c> and <c>'@Ignore</c> above the block's header; the fix writes them there.
/// </summary>
[TestClass]
public sealed class ModuleAnnotationShortFormAnalyzerTests
{
    private static readonly Uri File = new("file:///c:/ws/Shapes.bas");

    private static string Source(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static ModuleParseResult Parse(string source, bool fileLevelStatements = true)
    {
        var options = Options.Create(new SdkWorkspaceOptions { FileLevelStatements = fileLevelStatements });
        var parse = new ModuleParser(workspaceOptions: options).Parse(File, source);
        Assert.IsEmpty(parse.SyntaxErrors, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse;
    }

    private static AnalyzerFinding[] Run(string source, bool fileLevelStatements = true)
        => [.. new ModuleAnnotationShortFormAnalyzer(Options.Create(new SdkWorkspaceOptions { FileLevelStatements = fileLevelStatements }))
            .Analyze(new ModuleAnalysisContext(File, Parse(source, fileLevelStatements), null))];

    // the one finding's one fix, applied: what the file says then, and that it leaves nothing to report.
    private static string Fixed(string source, string written)
    {
        var fix = Assert.ContainsSingle(Run(source)).Fixes?.Edits.SingleOrDefault();
        Assert.IsNotNull(fix);
        Assert.AreEqual(AnalyzerMessages.Format(RDCoreDiagnosticsResources.ModuleAnnotationHasAShortForm_Fix, written), fix.Title);

        var after = fix.Edits.ApplyTo(source);
        Assert.IsEmpty(Run(after), "the fix leaves nothing to report");
        return after;
    }

    [TestMethod]
    [DataRow("'@ModuleDescription(\"Shapes\")", "ModuleDescription", "Description", DisplayName = "'@ModuleDescription")]
    [DataRow("'@IgnoreModule RDC01001", "IgnoreModule", "Ignore", DisplayName = "'@IgnoreModule")]
    public void AModuleAnnotationInABlock_IsReported_AtTheAnnotation_NamingItsShortForm(string annotation, string name, string written)
    {
        var source = Source("Class Shape", $"    {annotation}", "    Public Sub Draw()", "    End Sub", "End Class");

        var finding = Assert.ContainsSingle(Run(source));

        Assert.AreEqual(RDCoreDiagnosticId.ModuleAnnotationHasAShortForm, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Information, finding.Severity);
        Assert.AreEqual(1, finding.Range.Start.Line);
        Assert.AreEqual("    ".Length + 1, finding.Range.Start.Character, "from the annotation's @");
        Assert.AreEqual(AnalyzerMessages.Format(RDCoreDiagnosticsResources.ModuleAnnotationHasAShortForm_Message, name, written), finding.Message);
    }

    // the description moved above the header is the block's module's, as the SDK binds it.
    [TestMethod]
    public void TheFix_MovesTheDescriptionAboveTheHeader_WhereItDescribesTheBlocksModule()
    {
        var after = Fixed(Source("Public Sub Own()", "End Sub", "Class Shape", "    '@ModuleDescription(\"Shapes\")", "    Public Sub Draw()", "    End Sub",
            "End Class"), "Description");

        Assert.AreEqual(Source("Public Sub Own()", "End Sub", "'@Description(\"Shapes\")", "Class Shape", "    Public Sub Draw()", "    End Sub",
            "End Class"), after);
        var parse = Parse(after);
        Assert.AreEqual("Shapes", DocStrings.GetModuleDescription(new RDCoreDocument(File, parse).ParseOf(parse.BlockModules[0].Uri)!));
    }

    // beside another annotation, only it goes; its arguments go with it.
    [TestMethod]
    public void TheFix_MovesIgnoreModuleWithItsCodes_AndLeavesTheOtherAnnotation()
        => Assert.AreEqual(
            Source("'@Ignore RDC01001, RDC01002", "Module Tools", "    ' @Folder(\"Tools\")", "    Public Sub Run()", "    End Sub", "End Module"),
            Fixed(Source("Module Tools", "    '@IgnoreModule RDC01001, RDC01002 @Folder(\"Tools\")", "    Public Sub Run()", "    End Sub",
                "End Module"), "Ignore"));

    [TestMethod]
    public void AboveTheHeader_TheFixRenamesIt()
        => Assert.AreEqual(Source("Public Sub Own()", "End Sub", "'@Ignore RDC01001", "Class Shape", "End Class"),
            Fixed(Source("Public Sub Own()", "End Sub", "'@IgnoreModule RDC01001", "Class Shape", "End Class"), "Ignore"));

    [TestMethod]
    public void NothingIsReported_WithoutFileLevelStatements_OrInAFileWithoutBlocks_OrForTheShortForm()
    {
        Assert.IsEmpty(Run(Source("Class Shape", "    '@ModuleDescription(\"Shapes\")", "End Class"), fileLevelStatements: false));
        Assert.IsEmpty(Run(Source("'@ModuleDescription(\"Shapes\")", "Public Sub Own()", "End Sub")));
        Assert.IsEmpty(Run(Source("'@Description(\"Shapes\")", "Class Shape", "    '@Description(\"Draws\")", "    Public Sub Draw()", "    End Sub",
            "End Class")));
    }
}
