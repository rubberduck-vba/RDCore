using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Handlers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;
using MemberKind = RDCore.SDK.Runtime.Libraries.MemberKind;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00208</c> in the code of a UserForm: a local or a parameter named like a member the form's designer declares on its class (the form object's
/// <c>Width</c>, Microsoft Forms' <c>Caption</c>) hides it (<strong>MS-VBAL §5.6.10</strong>). The parameters of those members are not declarations
/// of the module's code.
/// </summary>
[TestClass]
public sealed class FormShadowedDeclarationTests
{
    // Microsoft Forms' UserForm, as `rdc export-library` describes FM20.DLL: a property whose Let has a parameter of the property's name.
    private static readonly ModuleWorkspace.WorkspaceLibraries References = new(["MSForms"], new InMemoryLibrarySource(new LibraryDescription
    {
        Name = "MSForms",
        Classes =
        [
            new ClassDescription
            {
                Name = "UserForm",
                Members =
                [
                    new MemberDescription { Name = "Caption", Kind = MemberKind.PropertyGet, Type = "String" },
                    new MemberDescription { Name = "Caption", Kind = MemberKind.PropertyLet, Parameters = [new ParameterDescription { Name = "Caption", Type = "String" }] },
                    new MemberDescription { Name = "InsideHeight", Kind = MemberKind.PropertyGet, Type = "Single" },
                ],
            },
        ],
    }));

    private static readonly FormDescriptor Designer = new(
        "Form1", FormDesignerKind.MSForms, new FormControlClass("MSForms", "UserForm", FormDesigner.MSFormsClassId),
        new Dictionary<string, string> { ["Caption"] = "Form1" }, []);

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
           body.Replace(" ;; ", "\r\n") + "\r\n";

    // what the diagnostics server answers the language server's request for the form's diagnostics.
    private static async Task<Diagnostic[]> ShadowedAsync(string body)
    {
        var form = Form(body);
        var payload = await ModuleWorkspace.SemanticsAsync(
            [], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n", libraries: References, forms: [("Form1", form, Designer)]);
        var semantics = payload.Modules.Single(module => SDK.Model.Symbols.SymbolUri.ModuleNameOf(module.Module) == "Form1");
        var uri = Platform.Wire.FileUri(Path.Combine(ModuleWorkspace.Root, "Form1.frm"));
        var parse = new ModuleParser().Parse(uri, form);

        var response = await new DiagnoseDocumentHandler(new DiagnosticFactory(), [new ShadowedDeclarationAnalyzer()], NullLogger<DiagnoseDocumentHandler>.Instance).Handle(
            new DiagnoseDocumentParams { Json = PlatformJson.Serialize(new DiagnoseDocumentPayload(uri, 1, parse, semantics)) }, CancellationToken.None);
        return [.. response.Diagnostics.Where(diagnostic => diagnostic.Code?.String == "RDC00208")];
    }

    private static string Described(IEnumerable<Diagnostic> diagnostics)
        => string.Join("\n", diagnostics.Select(diagnostic => $"{diagnostic.Range.Start.Line}: {diagnostic.Message}"));

    [TestMethod]
    [DataRow("", DisplayName = "a form with no code")]
    [DataRow("Public Sub Probe() ;; Debug.Print Me.InsideHeight; Me.Width; Caption ;; Me.Width = 300 ;; Me.Move 0, 0, 100 ;; End Sub",
        DisplayName = "code that reads and assigns the form's members")]
    public async Task AFormWhoseCodeDeclaresNoLocalOfAMembersName_HidesNothing(string body)
    {
        var diagnostics = await ShadowedAsync(body);

        Assert.IsEmpty(diagnostics, Described(diagnostics));
    }

    [TestMethod]
    [DataRow("Public Sub Probe() ;; Dim Width As Single ;; End Sub", "Width", 12, DisplayName = "a local, a member of the form object")]
    [DataRow("Public Sub Probe(ByVal Caption As String) ;; End Sub", "Caption", 11, DisplayName = "a parameter, a member of Microsoft Forms' UserForm")]
    public async Task ALocalOrAParameterOfAFormMembersName_HidesIt(string body, string name, int line)
    {
        var diagnostics = await ShadowedAsync(body);

        var hidden = Assert.ContainsSingle(diagnostics, Described(diagnostics));
        StringAssert.StartsWith(hidden.Message, $"'{name}' hides 'Form1.{name}'");
        Assert.AreEqual(line, hidden.Range.Start.Line);
        Assert.AreEqual(DiagnosticSeverity.Information, hidden.Severity);
    }
}
