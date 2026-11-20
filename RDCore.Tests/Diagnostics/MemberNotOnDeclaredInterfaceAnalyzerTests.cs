using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using System.IO.Abstractions;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11051</c>: a member access on an object declared as a class of a referenced library that the class does not declare (MS-VBAL §5.6.12): the
/// access is bound when the code runs, and the host's object may have the member. Reported at Warning severity, from the access the host states
/// <c>NotOnDeclaredInterface</c>; a class of the VBA library is <c>RDC11010</c>'s.
/// </summary>
[TestClass]
public sealed class MemberNotOnDeclaredInterfaceAnalyzerTests
{
    private static readonly (string, string) Open = ("Open", ModuleWorkspace.ClassModule("Open", "Attribute VB_Extensible = True", "Option Explicit",
        "Public Tag As Long"));

    // the body's lines, separated by '|', follow the three lines that open the module and its procedure
    private const int FirstBodyLine = 3;

    private static ModuleWorkspace.WorkspaceLibraries Libraries
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            return new(["Excel", "MSForms"], new DirectoryLibrarySource(new FileSystem(), Path.Combine(directory!.FullName, "Symbols")));
        }
    }

    private static string ProgramOf(string parameters, string body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main({parameters})\r\n{string.Join("\r\n", body.Split('|'))}\r\nEnd Sub\r\n";

    private static async Task<(AnalyzerFinding[] Findings, AnalyzerFinding[] Unresolved)> FindingsAsync(string parameters, string body)
    {
        var program = ProgramOf(parameters, body);
        var payload = await ModuleWorkspace.SemanticsAsync([Open], program, libraries: Libraries);
        var model = payload.Modules.Single(module => SymbolUri.ModuleNameOf(module.Module) == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var context = new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model);
        return ([.. new MemberNotOnDeclaredInterfaceAnalyzer().Analyze(context)], [.. new UnresolvedLateBoundMemberAccessAnalyzer().Analyze(context)]);
    }

    [TestMethod]
    [DataRow("", "Dim ws As Worksheet|ws.Rnage(\"A1\").Value = 1", "Rnage", "Worksheet", DisplayName = "a Worksheet")]
    [DataRow("", "Dim ws As Excel.Worksheet|Debug.Print ws.Rnage(\"A1\").Value", "Rnage", "Worksheet", DisplayName = "an Excel.Worksheet")]
    [DataRow("", "Dim f As UserForm|Debug.Print f.MyProp", "MyProp", "UserForm", DisplayName = "a UserForm")]
    [DataRow("", "Dim f As MSForms.UserForm|f.MyProp = 1", "MyProp", "UserForm", DisplayName = "an MSForms.UserForm")]
    [DataRow("ByVal ws As Worksheet", "ws.Rnage(\"A1\").Value = 1", "Rnage", "Worksheet", DisplayName = "a parameter")]
    public async Task AMemberThatALibrarysClassDoesNotDeclare_IsReported(string parameters, string body, string member, string declared)
    {
        var (findings, unresolved) = await FindingsAsync(parameters, body);

        var finding = Assert.ContainsSingle(findings);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, $"'{member}'");
        StringAssert.Contains(finding.Message, $"'{declared}'");
        var lines = body.Split('|');
        Assert.AreEqual(FirstBodyLine + Array.FindIndex(lines, line => line.Contains($".{member}", StringComparison.Ordinal)), finding.Range.Start.Line);
        Assert.IsEmpty(unresolved, "the host's object may have the member: it is not RDC11010's");
    }

    [TestMethod]
    [Ignore("the analysis states run-time error 91 for a member of the With block of a parameter, which is not known to be Nothing: "
        + "the access is the error's")]
    public async Task AMemberThatALibrarysClassDoesNotDeclare_OnTheObjectOfAWithBlock_IsReported()
    {
        var (findings, _) = await FindingsAsync("ByVal ws As Worksheet", "With ws|.Rnage(\"A1\").Value = 1|End With");

        var finding = Assert.ContainsSingle(findings);
        StringAssert.Contains(finding.Message, "'Worksheet'");
        Assert.AreEqual(FirstBodyLine + 1, finding.Range.Start.Line);
    }

    // a class of the VBA library has the members it declares and no other: the access raises 438 every time, which RDC11010 says.
    [TestMethod]
    public async Task AMemberThatACollectionDoesNotDeclare_IsUnresolved_AndNotThisCode()
    {
        var (findings, unresolved) = await FindingsAsync(string.Empty, "Dim c As Collection|Dim x As Variant|Set c = New Collection|x = c.Nope");

        Assert.IsEmpty(findings);
        Assert.ContainsSingle(unresolved);
    }

    [TestMethod]
    [DataRow("Dim o As Object|Debug.Print o.Rnage", DisplayName = "an Object")]
    [DataRow("Dim v As Variant|Debug.Print v.Rnage", DisplayName = "a Variant")]
    [DataRow("Dim ws As Worksheet|ws.Range(\"A1\").Value = 1", DisplayName = "a member the library's class declares")]
    [DataRow("Dim f As UserForm|Debug.Print f.Caption", DisplayName = "a member a UserForm declares")]
    [DataRow("Dim b As Open|Debug.Print b.Nope", DisplayName = "a VB_Extensible class module")]
    public async Task AnAccessNotOnALibrarysClass_IsNotReported(string body)
        => Assert.IsEmpty((await FindingsAsync(string.Empty, body)).Findings);

    // the object of a control is wrapped in its extender (MSForms.Control), whose members its class does not declare.
    [TestMethod]
    public async Task AMemberOfAFormsControl_IsNotReported()
    {
        var form = "VERSION 5.00\r\n" +
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
                   "Public Sub Work()\r\n" +
                   "    Ok.Nope = 1\r\n" +
                   "    Debug.Print Me.Ok.Nope\r\n" +
                   "End Sub\r\n";
        var designer = new FormDescriptor(
            "Form1", FormDesignerKind.MSForms, new FormControlClass("MSForms", "UserForm", FormDesigner.MSFormsClassId),
            new Dictionary<string, string> { ["Caption"] = "Form1" },
            [new FormControlDescriptor("Ok", new FormControlClass("MSForms", "CommandButton"), TabIndex: null, Index: null, [])]);
        var payload = await ModuleWorkspace.SemanticsAsync(
            [], ProgramOf(string.Empty, "Debug.Print 1"), libraries: Libraries, forms: [("Form1", form, designer)]);
        var model = payload.Modules.Single(module => SymbolUri.ModuleNameOf(module.Module) == "Form1");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Form1.frm"));

        var findings = new MemberNotOnDeclaredInterfaceAnalyzer()
            .Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, form), model))
            .ToArray();

        Assert.IsEmpty(findings, string.Join("; ", findings.Select(finding => finding.Message)));
    }
}
