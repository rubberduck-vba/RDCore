using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Handlers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli;
using System.IO.Abstractions;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// What is said of the code of a host's document, which RD-VBA analyzes and does not host: <c>RDC01005</c> at the module, with each place that presumes the host,
/// and at each name of it in another module; <c>RDC01006</c> and <c>RDC01007</c> when its host's class is not known; <c>RDC11024</c> at its <c>Implements</c>.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.6.4 Rubberduck Core Diagnostics")]
public sealed class DocumentModuleDiagnosticsTests
{
    private static ModuleWorkspace.WorkspaceLibraries Excel
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            return new(["Excel"], new DirectoryLibrarySource(new FileSystem(), Path.Combine(directory!.FullName, "Symbols")));
        }
    }

    private static readonly string GameSheet = string.Join("\r\n",
        "'@Folder(\"Battleship.View\")",
        "Option Explicit",
        "Private Sub Worksheet_Change(ByVal Target As Range)",
        "    If Target.Row = 1 Then Exit Sub",
        "End Sub",
        "Private Sub Worksheet_Foo()",
        "End Sub",
        "Public Sub Reset()",
        "    Me.Range(\"A1\").Value = 1",
        "    Cells(1, 2).Value = 2",
        "End Sub",
        "");

    private const string Program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n";

    private static async Task<(ModuleAnalysisContext Context, IReadOnlyList<Diagnostic> Diagnostics)> DiagnoseAsync(
        string name, string source, DocClassType? super, ModuleWorkspace.WorkspaceLibraries? libraries, string program = Program,
        IReadOnlyList<(string Name, string Source)>? classes = null, string? diagnosed = null)
    {
        // the document module is the one named; the module diagnosed is it, or the program.
        var payload = await ModuleWorkspace.SemanticsAsync(classes ?? [], program, libraries: libraries, documents: [(name, source, super)]);
        var module = diagnosed ?? name;
        var (file, text) = module == "Program" ? ("Program.bas", program) : ($"{name}.doccls", source);
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, file));
        var semantics = payload.Modules.Single(candidate => candidate.Module.Fragment.TrimStart('#') == module);
        var parse = new ModuleParser().Parse(uri, text);

        IModuleAnalyzer[] analyzers = [new HostedDocumentCodeAnalyzer(), new DocumentModuleHostAnalyzer(), new ImplementsInDocumentModuleAnalyzer()];
        var response = await new DiagnoseDocumentHandler(new DiagnosticFactory(), analyzers, NullLogger<DiagnoseDocumentHandler>.Instance).Handle(
            new DiagnoseDocumentRequest { Json = PlatformJson.Serialize(new DiagnoseDocumentPayload(uri, 1, parse, semantics)) }, CancellationToken.None);
        return (new ModuleAnalysisContext(uri, parse, semantics), [.. response.Diagnostics]);
    }

    private static string CodeOf(Diagnostic diagnostic) => diagnostic.Code?.String ?? string.Empty;

    [TestMethod]
    public async Task AWorksheetsCode_IsReportedOnce_WithEachPlaceThatPresumesItsHost()
    {
        var (_, diagnostics) = await DiagnoseAsync("GameSheet", GameSheet, DocClassType.ExcelWorksheet, Excel);

        var hosted = diagnostics.Single(diagnostic => CodeOf(diagnostic) == "RDC01005");
        Assert.AreEqual(DiagnosticSeverity.Error, hosted.Severity);
        Assert.AreEqual(1, hosted.Range.Start.Line, "at the module's first line of code: the '@Folder comment above it is no statement");
        StringAssert.Contains(hosted.Message, "Excel.Worksheet");

        var related = hosted.RelatedInformation!.Select(information => information.Message).ToArray();
        Assert.IsTrue(related.Any(message => message.StartsWith("'Worksheet_Change'", StringComparison.Ordinal)), string.Join("\n", related));
        Assert.IsFalse(related.Any(message => message.StartsWith("'Worksheet_Foo'", StringComparison.Ordinal)), "Foo is no event of a worksheet");
        Assert.IsTrue(related.Any(message => message.StartsWith("'Range'", StringComparison.Ordinal)), "Me.Range: " + string.Join("\n", related));
        Assert.IsTrue(related.Any(message => message.StartsWith("'Cells'", StringComparison.Ordinal)), "an unqualified Cells: " + string.Join("\n", related));
        Assert.IsFalse(diagnostics.Any(diagnostic => CodeOf(diagnostic) is "RDC01006" or "RDC01007"));
    }

    [TestMethod]
    public async Task ANameOfADocumentInAnotherModule_IsReportedAsOne_InPlaceOfVariableNotDefined()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    GameSheet.Reset\r\nEnd Sub\r\n";

        var (_, diagnostics) = await DiagnoseAsync("GameSheet", GameSheet, DocClassType.ExcelWorksheet, Excel, program, diagnosed: "Program");

        var reference = diagnostics.Single(diagnostic => CodeOf(diagnostic) == "RDC01005");
        Assert.AreEqual((3, 4), (reference.Range.Start.Line, reference.Range.Start.Character),
            string.Join(" | ", diagnostics.Select(diagnostic => $"{CodeOf(diagnostic)} {diagnostic.Range} {diagnostic.Message}")));
        StringAssert.Contains(reference.Message, "'GameSheet'");
        Assert.IsFalse(diagnostics.Any(diagnostic => CodeOf(diagnostic).StartsWith("VBC", StringComparison.Ordinal) && diagnostic.Range == reference.Range),
            string.Join("\n", diagnostics.Select(diagnostic => $"{CodeOf(diagnostic)} {diagnostic.Message}")));
    }

    [TestMethod]
    public async Task ANameOfADocument_DeclaredByBeingNamed_IsReportedAsOne()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n    GameSheet.Reset\r\nEnd Sub\r\n";

        var (_, diagnostics) = await DiagnoseAsync("GameSheet", GameSheet, DocClassType.ExcelWorksheet, Excel, program, diagnosed: "Program");

        Assert.ContainsSingle(diagnostics.Where(diagnostic => CodeOf(diagnostic) == "RDC01005"));
    }

    [TestMethod]
    [DataRow(DocClassType.Unknown)]
    public async Task ADocumentOfAHostRDVBADoesNotKnow_IsUnsupported(DocClassType super)
    {
        var (_, diagnostics) = await DiagnoseAsync("GameSheet", GameSheet, super, Excel);

        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics.Single(diagnostic => CodeOf(diagnostic) == "RDC01006").Severity);
        Assert.IsFalse(diagnostics.Any(diagnostic => CodeOf(diagnostic) == "RDC01005"));
    }

    [TestMethod]
    public async Task ADocclsTheProjectSaysNothingOf_IsUnsupported()
    {
        var (_, diagnostics) = await DiagnoseAsync("GameSheet", GameSheet, null, Excel);

        StringAssert.Contains(diagnostics.Single(diagnostic => CodeOf(diagnostic) == "RDC01006").Message, "Super");
    }

    [TestMethod]
    public async Task AWorksheetsCode_InAProjectThatDoesNotReferenceExcel_SaysSo()
    {
        var (_, diagnostics) = await DiagnoseAsync("GameSheet", GameSheet, DocClassType.ExcelWorksheet, libraries: null);

        StringAssert.Contains(diagnostics.Single(diagnostic => CodeOf(diagnostic) == "RDC01007").Message, "Excel");
    }

    [TestMethod]
    public async Task AnImplementsDirectiveOfADocumentModule_IsAWarning_AndNoCompileError()
    {
        // MS-VBA compiles and runs it (oracle\s49 D22).
        var iface = ("IGridView", "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"IGridView\"\r\nPublic Sub Refresh()\r\nEnd Sub\r\n");
        var source = "Option Explicit\r\nImplements IGridView\r\nPrivate Sub IGridView_Refresh()\r\nEnd Sub\r\n";

        var (_, diagnostics) = await DiagnoseAsync("GameSheet", source, DocClassType.ExcelWorksheet, Excel, classes: [iface]);

        var warning = diagnostics.Single(diagnostic => CodeOf(diagnostic) == "RDC11024");
        Assert.AreEqual(DiagnosticSeverity.Warning, warning.Severity);
        Assert.AreEqual(1, warning.Range.Start.Line);
        Assert.IsFalse(diagnostics.Any(diagnostic => CodeOf(diagnostic) == ((int)VBCompileErrorId.InvalidImplementsDirective).ToString("VBC00000", System.Globalization.CultureInfo.InvariantCulture)),
            string.Join("\n", diagnostics.Select(diagnostic => $"{CodeOf(diagnostic)} {diagnostic.Message}")));
    }
}
