using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;
using MemberKind = RDCore.SDK.Runtime.Libraries.MemberKind;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// A control of a form used by the code of another module (<c>RDC00412</c>), through the form's name or a variable of its class; not by the form's own code,
/// and not a member of the form that its code declares.
/// </summary>
[TestClass]
public sealed class PublicControlFieldAccessAnalyzerTests
{
    // the part of Microsoft Forms 2.0 the tests use, as `rdc export-library` describes FM20.DLL
    private static readonly ModuleWorkspace.WorkspaceLibraries References = new(["MSForms"], new InMemoryLibrarySource(new LibraryDescription
    {
        Name = "MSForms",
        Classes =
        [
            new ClassDescription
            {
                Name = "UserForm",
                Members = [new MemberDescription { Name = "Caption", Kind = MemberKind.PropertyGet, Type = "String" }],
            },
            new ClassDescription
            {
                Name = "TextBox",
                Members =
                [
                    new MemberDescription { Name = "Text", Kind = MemberKind.PropertyGet, Type = "String", DispId = 0 },
                    new MemberDescription
                    {
                        Name = "Text", Kind = MemberKind.PropertyLet, DispId = 0, Parameters = [new ParameterDescription { Name = "Text", Type = "String" }],
                    },
                ],
            },
        ],
    }));

    private static readonly FormDescriptor Designer = new(
        "Form1", FormDesignerKind.MSForms, new FormControlClass("MSForms", "UserForm", FormDesigner.MSFormsClassId),
        new Dictionary<string, string> { ["Caption"] = "Form1" },
        [new FormControlDescriptor("TextBox1", new FormControlClass("MSForms", "TextBox"), TabIndex: null, Index: null, [])]);

    private static string Form(params string[] body)
        => "VERSION 5.00\r\n" +
           "Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} Form1 \r\n" +
           "   Caption         =   \"Form1\"\r\n" +
           "   OleObjectBlob   =   \"Form1.frx\":0000\r\n" +
           "End\r\n" +
           "Attribute VB_Name = \"Form1\"\r\n" +
           "Attribute VB_GlobalNameSpace = False\r\n" +
           "Attribute VB_Creatable = False\r\n" +
           "Attribute VB_PredeclaredId = True\r\n" +
           "Attribute VB_Exposed = False\r\n" +
           "Option Explicit\r\n" +
           string.Join("\r\n", body) + "\r\n";

    private static readonly string FormCode = Form(
        "Public Total As Long",
        "Private Sub UserForm_Initialize()",
        "    TextBox1.Text = \"a\"",
        "    Me.TextBox1.Text = \"b\"",
        "End Sub");

    // the findings in a module, as the control's name, its form's name and the line, in the order they are written.
    private static async Task<(string Control, string Form, int Line)[]> FoundAsync(string moduleName, params string[] main)
        => (await AnalyzedAsync(moduleName, main)).Findings;

    // the findings, and how many expressions of the module the host bound to a control.
    private static async Task<((string Control, string Form, int Line)[] Findings, int ControlUses)> AnalyzedAsync(string moduleName, params string[] main)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", main)}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program, libraries: References, forms: [("Form1", FormCode, Designer)]);
        var model = payload.Modules.Single(module => SymbolUri.ModuleNameOf(module.Module) == moduleName);
        var (file, source) = moduleName == "Form1" ? ("Form1.frm", FormCode) : ("Program.bas", program);
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, file));
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));

        var findings = new PublicControlFieldAccessAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, model)).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.PublicControlFieldAccess && finding.Severity == DiagnosticSeverity.Information));
        var controlUses = model.Procedures.SelectMany(procedure => procedure.Expressions)
            .Count(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.BoundToDesignerControl));
        return ([.. findings.Select(finding => (finding.Message.Split('\'')[1], finding.Message.Split('\'')[3], finding.Range.Start.Line))], controlUses);
    }

    [TestMethod]
    public async Task AControlUsedInAnotherModule_IsFound_ThroughTheFormOrAVariableOfItsClass()
        => CollectionAssert.AreEqual(
            new[] { ("TextBox1", "Form1", 4), ("TextBox1", "Form1", 5) },
            await FoundAsync("Program", "Dim f As New Form1", "Form1.TextBox1.Text = \"a\"", "Debug.Print f.TextBox1.Text"));

    [TestMethod]
    public async Task AMemberOfTheFormThatItsCodeDeclares_OrThatItsClassHas_IsNotFound()
        => Assert.IsEmpty(await FoundAsync("Program", "Form1.Total = 1", "Debug.Print Form1.Caption"));

    [TestMethod]
    public async Task TheFormsOwnCode_UsingItsControl_IsNotFound()
    {
        var (findings, controlUses) = await AnalyzedAsync("Form1", "Form1.Total = 1");

        // the premise: the form's code uses TextBox1, and the host says so; without it, nothing found would prove nothing.
        Assert.IsGreaterThan(0, controlUses, "the form's own uses of TextBox1 are no longer bound to a control: rewrite this test");
        Assert.IsEmpty(findings);
    }
}
