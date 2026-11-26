using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Model;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00417</c>: a <c>'@Folder</c> annotation that does not name the folder its file is in under the root of its project, which the fix writes, or
/// removes at the root.
/// </summary>
[TestClass]
public sealed class FolderAnnotationAnalyzerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-folder-ws");

    private static string Module(string source) => $"Attribute VB_Name = \"Module1\"\r\n{source.ReplaceLineEndings("\r\n")}\r\nOption Explicit\r\n";

    // the file at Root/<folders>/Module1.bas, and the module the host says it is, under Root.
    private static (Uri Document, ModuleSemanticsDto Semantics) FileIn(params string[] folders)
        => (new Uri(Path.Combine([Root, .. folders, "Module1.bas"])), new ModuleSemanticsDto(SymbolUri.Module(SymbolUri.Root(Root), "Module1"), true, [], [], []));

    private static AnalyzerFinding[] Run(string source, (Uri Document, ModuleSemanticsDto Semantics) file, bool withSemantics = true)
    {
        var parsed = new ModuleParser().Parse(file.Document, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. new FolderAnnotationAnalyzer().Analyze(new ModuleAnalysisContext(file.Document, parsed, withSemantics ? file.Semantics : null))];
    }

    private static string Text(string source, AnalyzerFinding finding)
        => source.ReplaceLineEndings("\n").Split('\n')[finding.Range.Start.Line][finding.Range.Start.Character..finding.Range.End.Character];

    // the one finding's one fix, applied: what the module says then, and that it leaves nothing to report.
    private static string Fixed(string source, (Uri Document, ModuleSemanticsDto Semantics) file, string title)
    {
        var fix = Assert.ContainsSingle(Run(source, file)).Fixes?.Edits.SingleOrDefault();
        Assert.IsNotNull(fix);
        Assert.AreEqual(title, fix.Title);

        var after = fix.Edits.ApplyTo(source);
        Assert.IsEmpty(Run(after, file), "the fix leaves nothing to report");
        return after;
    }

    [TestMethod]
    [DataRow("Other", DisplayName = "another folder")]
    [DataRow("Parent", DisplayName = "the folder above")]
    [DataRow("parent.child", DisplayName = "the folder, in another case")]
    public void AFolderThatIsNotTheFilesFolder_IsReported_AtTheAnnotation_NamingTheFilesFolder(string written)
    {
        var source = Module($"'@Folder(\"{written}\")");

        var finding = Assert.ContainsSingle(Run(source, FileIn("Parent", "Child")));

        Assert.AreEqual(RDCoreDiagnosticId.FolderAnnotationNotTheFilesFolder, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Information, finding.Severity);
        Assert.AreEqual($"@Folder(\"{written}\")", Text(source, finding));
        Assert.AreEqual(AnalyzerMessages.Format(RDCoreDiagnosticsResources.FolderAnnotationNotTheFilesFolder_Message, "Parent.Child"), finding.Message);
    }

    [TestMethod]
    public void TheFix_WritesTheFilesFolder()
    {
        var title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.FolderAnnotationNotTheFilesFolder_Fix, "Parent.Child");

        Assert.AreEqual(Module("'@Folder(\"Parent.Child\") @PredeclaredId"),
            Fixed(Module("'@Folder(\"Other\") @PredeclaredId"), FileIn("Parent", "Child"), title));
    }

    [TestMethod]
    [DataRow(new[] { "Parent", "Child" }, "Parent.Child", DisplayName = "two folders down")]
    [DataRow(new[] { "Parent" }, "Parent", DisplayName = "one folder down")]
    public void AFolderThatIsTheFilesFolder_IsNotReported(string[] folders, string written)
        => Assert.IsEmpty(Run(Module($"'@Folder(\"{written}\")"), FileIn(folders)));

    [TestMethod]
    public void AModuleWithNoFolderAnnotation_IsNotReported()
        => Assert.IsEmpty(Run(Module("'@PredeclaredId"), FileIn("Parent", "Child")));

    // with no semantics there is no project root to read the file's folder from; a folder named with a '.' is two to the annotation.
    [TestMethod]
    public void NothingIsSaid_WithoutTheModulesSemantics_OrUnderAFolderNamedWithADot()
    {
        Assert.IsEmpty(Run(Module("'@Folder(\"Other\")"), FileIn("Parent", "Child"), withSemantics: false));
        Assert.IsEmpty(Run(Module("'@Folder(\"Other\")"), FileIn("v1.2")));
    }

    [TestMethod]
    public void AFolderAnnotation_OfAFileAtTheRoot_IsReported_AsNamingNone()
    {
        var source = Module("'@Folder(\"Parent\")");

        var finding = Assert.ContainsSingle(Run(source, FileIn()));

        Assert.AreEqual(RDCoreDiagnosticsResources.FolderAnnotationNotTheFilesFolder_RootMessage, finding.Message);
        Assert.AreEqual("@Folder(\"Parent\")", Text(source, finding));
    }

    // the annotation alone on its comment line goes with the line; beside another annotation, only it goes.
    [TestMethod]
    [DataRow("'@Folder(\"Parent\")\n'@PredeclaredId", "'@PredeclaredId", DisplayName = "alone on its line")]
    [DataRow("'@Folder(\"Parent\") @PredeclaredId", "' @PredeclaredId", DisplayName = "beside another annotation")]
    public void TheFix_AtTheRoot_RemovesTheAnnotation(string before, string after)
        => Assert.AreEqual(Module(after), Fixed(Module(before), FileIn(), RDCoreDiagnosticsResources.FolderAnnotationNotTheFilesFolder_RootFix));
}
