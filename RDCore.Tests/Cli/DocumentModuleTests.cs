using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A document module - the code of an Excel worksheet, as legacy Rubberduck exported it to a <c>.doccls</c> - is what the project says it is (<c>Super</c>): a
/// class module whose code names the members of its host's class, analyzed and never run. RD-VBA hosts no document: nothing makes one of its objects, and no
/// name stands for the document in another module.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.2.3.3 RDCoreModule")]
public sealed class DocumentModuleTests
{
    private static WorkspaceLibraries Excel
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

    // what legacy Rubberduck exported of a worksheet's module: the text its code pane shows, no header and no attribute.
    private static readonly string GameSheet = string.Join("\r\n",
        "'@Folder(\"Battleship.View\")",
        "Option Explicit",
        "Private Sub Worksheet_Change(ByVal Target As Range)",
        "    If Target.Row = 1 Then Exit Sub",
        "End Sub",
        "Public Sub Reset()",
        "    Me.Range(\"A1\").Value = 1",
        "    Range(\"A2\").Value = Name",
        "    Cells(1, 2).Value = Me.Name",
        "End Sub",
        "");

    private const string Program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n";

    private static string ProgramThat(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n" + string.Join("\r\n", body) + "\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task AWorksheetsCode_NamesTheMembersOfItsWorksheet_ThroughMeAndUnqualified_AndLoads()
    {
        var errors = await LoadDocumentsAsync([("GameSheet", GameSheet, DocClassType.ExcelWorksheet)], Program, Excel);

        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [TestMethod]
    public void InTheLanguageServersComposition_AWorksheetsCode_NamesItsWorksheetsMembers_AfterItsOwn_AndNotAsItsOwn()
    {
        var root = new Uri(Path.Combine(Root, "doc") + Path.DirectorySeparatorChar);
        var moduleUri = new UriBuilder(root) { Fragment = "GameSheet" }.Uri;
        // a member the worksheet has too, which the module declares itself.
        var source = GameSheet + "Public Function Visible() As Boolean\r\nEnd Function\r\n";
        var parse = new RDCore.Parsing.ModuleParser().Parse(new Uri(Path.Combine(Root, "doc", "GameSheet.doccls")), source);
        var excel = Excel;
        var libraries = new LibrarySymbolProvider(root, LibrarySymbolProvider.Load(excel.Source, [new RDCoreReference { Name = "Excel" }])).ProvideSymbols().ToList();

        var resolver = RDCore.Symbols.WorkspaceSymbolResolver.Compose(root, [(moduleUri, SDK.Model.AST.Declarations.ModuleType.DocumentModule, parse)],
            new RDCore.Symbols.IntrinsicSymbolResolver(), "Battleship", libraries: libraries,
            documentHosts: new Dictionary<string, DocClassType> { ["GameSheet"] = DocClassType.ExcelWorksheet });
        var reset = resolver.ResolveValue("Reset", SDK.Model.Symbols.Abstract.ScopeKind.Global, moduleUri).Symbol!;

        var range = resolver.ResolveValue("Range", SDK.Model.Symbols.Abstract.ScopeKind.Local, reset.Uri).Symbol;
        var visible = resolver.ResolveValue("Visible", SDK.Model.Symbols.Abstract.ScopeKind.Local, reset.Uri).Symbol;
        var module = (SDK.Model.Symbols.VBClassModuleSymbol)resolver.ResolveType("GameSheet", SDK.Model.Symbols.Abstract.ScopeKind.Global, moduleUri).Symbol!;

        Assert.IsNotNull(range);
        StringAssert.EndsWith(range.ParentUri.Fragment, "Worksheet", "a member of the library's class, not one declared again as the module's");
        Assert.AreEqual(moduleUri.AbsoluteUri, visible!.ParentUri.AbsoluteUri, "the module's own member comes first");
        Assert.IsTrue(module.IsDocument);
        Assert.AreEqual(new DocumentHostClass("Excel", "Worksheet"), module.HostClass);
        Assert.IsFalse(module.Members.Any(member => member.Name == "Range"), "nothing of the worksheet is the module's");
    }

    [TestMethod]
    public async Task AWorkbooksCode_NamesTheMembersOfItsWorkbook()
    {
        var thisWorkbook = "Option Explicit\r\nPrivate Sub Workbook_BeforeClose(Cancel As Boolean)\r\n    Debug.Print Name, Worksheets.Count\r\nEnd Sub\r\n";

        var errors = await LoadDocumentsAsync([("ThisWorkbook", thisWorkbook, DocClassType.ExcelWorkbook)], Program, Excel);

        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [TestMethod]
    public async Task AWorkspaceWithDocumentModules_RunsItsProgram()
        => CollectionAssert.AreEqual(new[] { "1" }, (await RunWithDocumentsAsync([("GameSheet", GameSheet, DocClassType.ExcelWorksheet)], Program, Excel)).Select(line => line.Trim()).ToArray());

    [TestMethod]
    public async Task ADocclsTheProjectSaysNothingOf_IsTheStandardModuleItsTextMakesIt_WhereMeIsNoName()
    {
        // no name, no extension and no handler makes a module a document module: only the project does.
        var errors = await LoadDocumentsAsync([("GameSheet", GameSheet, null)], Program, Excel);

        Assert.IsTrue(errors.Any(error => error.StartsWith("Invalid use of Me keyword", StringComparison.Ordinal)), string.Join("\n", errors));
    }

    [TestMethod]
    public async Task TheCodeOfAWorksheet_ThatTheProjectGivesNoExcel_NamesNothingOfAWorksheet()
    {
        var errors = await LoadDocumentsAsync([("GameSheet", GameSheet, DocClassType.ExcelWorksheet)], Program, libraries: null);

        Assert.IsNotEmpty(errors, "Range and Cells are names of nothing here, under Option Explicit");
        Assert.IsFalse(errors.Any(error => error.Contains("Invalid use of Me", StringComparison.OrdinalIgnoreCase)), "the module is a class module all the same: " + string.Join("\n", errors));
    }

    [TestMethod]
    [DataRow("    Dim sheet As New GameSheet")]
    [DataRow("    Dim sheet As Object\r\n    Set sheet = New GameSheet")]
    public async Task ADocumentModule_IsNoClassThatAProgramCreates_InItsOwnProjectEither(string creates)
    {
        // MS-VBA: Invalid use of New keyword, of a worksheet's module.
        var errors = await LoadDocumentsAsync([("GameSheet", GameSheet, DocClassType.ExcelWorksheet)], ProgramThat(creates), Excel);

        Assert.IsTrue(errors.Any(error => error.Contains("GameSheet", StringComparison.Ordinal) && error.Contains("host", StringComparison.Ordinal)),
            string.Join("\n", errors) + "|");
    }

    [TestMethod]
    public async Task TheNameOfADocumentModule_StandsForNoDocument_InAnotherModule()
    {
        // MS-VBA's host makes the document the module's default instance; RD-VBA hosts none.
        var errors = await LoadDocumentsAsync([("GameSheet", GameSheet, DocClassType.ExcelWorksheet)], ProgramThat("    GameSheet.Reset"), Excel);

        Assert.IsTrue(errors.Any(error => error == "Variable not defined: GameSheet"), string.Join("\n", errors));
    }
}
