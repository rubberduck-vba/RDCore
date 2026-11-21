using Microsoft.Extensions.Options;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;
using MemberKind = RDCore.SDK.Runtime.Libraries.MemberKind;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The names of handlers and implementations that <c>RDC01001</c> and <c>RDC01002</c> judge: those bound to an event, an interface or a control of the
/// workspace, which are renamed with it. Those bound to what a library declares are named by the library, and are not reported.
/// </summary>
[TestClass]
public sealed class BoundNameAnalyzerTests
{
    private static readonly IOptions<DiagnosticsOptions> Defaults = Options.Create(new DiagnosticsOptions());

    // a type library: an application object that raises an event, and an interface a class of the workspace can implement.
    private static readonly ModuleWorkspace.WorkspaceLibraries Host = new(["Host"], new InMemoryLibrarySource(new LibraryDescription
    {
        Name = "Host",
        Classes =
        [
            new ClassDescription { Name = "Application", Events = [new EventDescription { Name = "NewWorkbook" }] },
            new ClassDescription { Name = "ICallback", Members = [new MemberDescription { Name = "Invoke", Kind = MemberKind.Method }] },
        ],
    }));

    private static readonly (string, string)[] Classes =
    [
        ("IA", ModuleWorkspace.ClassModule("IA", "Public Sub B1()", "End Sub")),
        ("Field", ModuleWorkspace.ClassModule("Field", "Public Event Change()")),
    ];

    private static string[] Names(IEnumerable<AnalyzerFinding> findings) => [.. findings.Select(finding => finding.Message.Split('\'')[1])];

    private static async Task<AnalyzerFinding[]> FindingsAsync(IModuleAnalyzer analyzer, params string[] lines)
    {
        var source = ModuleWorkspace.ClassModule("Bound", lines);
        var payload = await ModuleWorkspace.SemanticsAsync(
            [.. Classes, ("Bound", source)], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n", libraries: Host);
        var model = payload.Modules.Single(module => SymbolUri.ModuleNameOf(module.Module) == "Bound");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Bound.cls"));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model)).OrderBy(finding => finding.Range.Start)];
    }

    [TestMethod]
    public async Task TheImplementationOfAWorkspaceInterface_IsJudged_AndOffersNoRenameOfItsOwn()
    {
        var finding = Assert.ContainsSingle(
            await FindingsAsync(new UseMeaningfulIdentifierNamesAnalyzer(Defaults), "Implements IA", "Private Sub IA_B1()", "End Sub"));

        Assert.AreEqual("IA_B1", Names([finding])[0]);
        Assert.IsNull(finding.Fixes?.Rename);
    }

    [TestMethod]
    public async Task TheHandlerOfAWorkspaceEvent_IsJudged_AtTheHandler()
        => CollectionAssert.AreEqual(
            new[] { "txtName", "txtName_Change" },
            Names(await FindingsAsync(new HungarianNotationAnalyzer(Defaults),
                "Private WithEvents txtName As Field", "Private Sub txtName_Change()", "End Sub")));

    [TestMethod]
    public async Task TheHandlerOfALibraryObjectsEvent_IsNotJudged()
        => CollectionAssert.AreEqual(
            new[] { "objApp" },
            Names(await FindingsAsync(new HungarianNotationAnalyzer(Defaults),
                "Private WithEvents objApp As Host.Application", "Private Sub objApp_NewWorkbook()", "End Sub")));

    [TestMethod]
    public async Task TheImplementationOfALibraryInterface_IsNotJudged()
        => Assert.IsEmpty(await FindingsAsync(new HungarianNotationAnalyzer(Defaults), "Implements ICallback", "Private Sub ICallback_Invoke()", "End Sub"));

    [TestMethod]
    public async Task ALifecycleHandler_IsNotJudged()
        => Assert.IsEmpty(await FindingsAsync(new UseMeaningfulIdentifierNamesAnalyzer(Defaults), "Private Sub Class_Initialize()", "End Sub"));

    // a Visual Basic 6.0 form with a command button: its Click is the VB library's event, and the control is the form's own, renamed with its handlers.
    [TestMethod]
    public async Task TheHandlerOfAFormsControl_IsJudged_ThoughItsEventIsALibrarys()
    {
        var form = string.Join("\r\n", [
            "VERSION 5.00",
            "Begin VB.Form Form1 ",
            "   Caption         =   \"Form1\"",
            "   Begin VB.CommandButton cmdSave ",
            "      Caption         =   \"Save\"",
            "   End",
            "End",
            "Attribute VB_Name = \"Form1\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False",
            "Option Explicit",
            "Private Sub cmdSave_Click()",
            "End Sub",
            "Private Sub Form_Load()",
            "End Sub",
            string.Empty]);
        var designer = FormDesigner.Read(form)!;
        var payload = await ModuleWorkspace.SemanticsAsync(
            [], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n", language: SupportedLanguages.VB6,
            libraries: new ModuleWorkspace.WorkspaceLibraries([], ShippedLibrariesTests.Platform), forms: [("Form1", form, designer)]);
        var model = payload.Modules.Single(module => SymbolUri.ModuleNameOf(module.Module) == "Form1");
        Assert.IsTrue(model.Declarations.Any(declaration => declaration.Name == "cmdSave_Click" && declaration.Binding is not null), "the handler is bound");

        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Form1.frm"));
        var parse = new ModuleParser().Parse(uri, form) with { Designer = designer };
        var findings = new HungarianNotationAnalyzer(Defaults).Analyze(new ModuleAnalysisContext(uri, parse, model)).ToArray();

        CollectionAssert.AreEqual(new[] { "cmdSave_Click" }, Names(findings));
    }
}
