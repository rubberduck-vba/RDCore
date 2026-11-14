using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The parameters and the type of a procedure that implements an interface member or handles an event are the member's or the event's, and are not the
/// module's to choose: neither <c>As Integer</c> nor the <c>%</c> type-declaration character (<c>RDC00201</c>), nor a parameter that states no mechanism
/// (<c>RDC00104</c>), is reported there. What its body declares is the module's, and is.
/// </summary>
[TestClass]
public sealed class SignatureOfAnotherMemberTests
{
    private static readonly (string, string) Shape = ("IShape", ModuleWorkspace.ClassModule("IShape",
        "Public Sub Grow(factor As Integer)", "End Sub", "Public Function Sides() As Integer", "End Function"));

    private static readonly (string, string) Button = ("Button", ModuleWorkspace.ClassModule("Button", "Public Event Pressed(Key As Integer)"));

    private static readonly string Square = ModuleWorkspace.ClassModule("Square",
        "Implements IShape",
        "Private WithEvents Source As Button",
        "Private Sub IShape_Grow(factor As Integer)",
        "    Dim local As Integer",
        "End Sub",
        "Private Function IShape_Sides() As Integer",
        "End Function",
        "Private Sub Source_Pressed(Key As Integer)",
        "End Sub",
        "Private Sub Own(n As Integer)",
        "End Sub");

    private static string Form(string body)
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
           body;

    private static readonly FormDescriptor Designer = new(
        "Form1", FormDesignerKind.MSForms, new FormControlClass("MSForms", "UserForm", FormDesigner.MSFormsClassId),
        new Dictionary<string, string> { ["Caption"] = "Form1" }, []);

    private const string Program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n";

    // each finding, as the text of the line it is on.
    private static string[] Findings(IModuleAnalyzer analyzer, string source, ModuleSemanticsDto model, string file)
    {
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, file));
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        var lines = source.Split("\r\n");
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, model)).OrderBy(finding => finding.Range.Start)
            .Select(finding => lines[finding.Range.Start.Line].Trim())];
    }

    private static async Task<ModuleSemanticsDto> ClassModelAsync()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([Shape, Button, ("Square", Square)], Program);
        return payload.Modules.Single(module => SymbolUri.ModuleNameOf(module.Module) == "Square");
    }

    private static async Task<(string Source, ModuleSemanticsDto Model)> FormModelAsync(string body)
    {
        var form = Form(body);
        var payload = await ModuleWorkspace.SemanticsAsync([], Program, forms: [("Form1", form, Designer)]);
        return (form, payload.Modules.Single(module => SymbolUri.ModuleNameOf(module.Module) == "Form1"));
    }

    [TestMethod]
    public async Task AsInteger_IsNotReported_InTheSignatureOfAnImplementationOrAHandler_ButIsInTheirBodies()
    {
        var findings = Findings(new IntegerDataTypeAnalyzer(), Square, await ClassModelAsync(), "Square.cls");

        CollectionAssert.AreEqual(new[] { "Dim local As Integer", "Private Sub Own(n As Integer)" }, findings, string.Join("\n", findings));
    }

    [TestMethod]
    public async Task TheIntegerTypeCharacter_IsNotReported_InTheSignatureOfAnImplementation_ButIsInItsBody()
    {
        var circle = ModuleWorkspace.ClassModule("Circle",
            "Implements IShape",
            "Private Sub IShape_Grow(factor%)",
            "    Dim local%",
            "End Sub",
            "Private Function IShape_Sides%()",
            "End Function",
            "Private Sub Own(n%)",
            "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([Shape, ("Circle", circle)], Program);
        var model = payload.Modules.Single(module => SymbolUri.ModuleNameOf(module.Module) == "Circle");

        var findings = Findings(new IntegerDataTypeAnalyzer(), circle, model, "Circle.cls");

        CollectionAssert.AreEqual(new[] { "Dim local%", "Private Sub Own(n%)" }, findings, string.Join("\n", findings));
    }

    [TestMethod]
    public async Task AnImplicitByRef_IsNotReported_InTheSignatureOfAnImplementationOrAHandler()
    {
        var findings = Findings(new ImplicitByRefModifierAnalyzer(), Square, await ClassModelAsync(), "Square.cls");

        CollectionAssert.AreEqual(new[] { "Private Sub Own(n As Integer)" }, findings, string.Join("\n", findings));
    }

    [TestMethod]
    public async Task AUserFormsHandlerOfItsOwnEvent_HasItsSignature_AndOneThatDoesNotMatchItsEvent_IsAProcedureLikeAnyOther()
    {
        var (form, model) = await FormModelAsync(
            "Private Sub UserForm_QueryClose(Cancel As Integer, CloseMode As Integer)\r\nEnd Sub\r\n" +
            "Private Sub UserForm_Initialize(n As Integer)\r\nEnd Sub\r\n");

        CollectionAssert.AreEqual(new[] { "Private Sub UserForm_Initialize(n As Integer)" }, Findings(new IntegerDataTypeAnalyzer(), form, model, "Form1.frm"));
        CollectionAssert.AreEqual(new[] { "Private Sub UserForm_Initialize(n As Integer)" }, Findings(new ImplicitByRefModifierAnalyzer(), form, model, "Form1.frm"));
    }

    [TestMethod]
    public void WithoutTheHostsModel_EveryParameterIsReported()
    {
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Square.cls"));
        var parsed = new ModuleParser().Parse(uri, Square);

        Assert.HasCount(3, new ImplicitByRefModifierAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, null)).ToArray(), "nothing says which procedure handles what");
    }
}
