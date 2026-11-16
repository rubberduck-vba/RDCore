using RDCore.External;
using RDCore.External.Automation;
using RDCore.External.Native;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 form, where no form host of the platform hosts one: a class that cannot be created (429), which a program traps to fall back on
/// something else, as it does where Microsoft Forms is not there for a UserForm.
/// </summary>
[TestClass]
public sealed class VisualBasicFormWithoutHostTests
{
    private const string Frm = "VERSION 5.00\r\n"
        + "Begin VB.Form Form1\r\n"
        + "   Caption         =   \"Form1\"\r\n"
        + "   ClientHeight    =   3000\r\n"
        + "   ClientWidth     =   4000\r\n"
        + "End\r\n"
        + "Attribute VB_Name = \"Form1\"\r\n"
        + "Attribute VB_GlobalNameSpace = False\r\n"
        + "Attribute VB_Creatable = False\r\n"
        + "Attribute VB_PredeclaredId = True\r\n"
        + "Attribute VB_Exposed = False\r\n";

    private static ILibrarySource Shipped => ShippedLibrariesTests.Platform;

    private static Task<string[]> RunAsync(params string[] body)
    {
        var world = new ExternalWorld(UnavailableAutomationServer.Instance, UnavailableNativeLibraryHost.Instance);
        var program = $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        return ModuleWorkspace.RunAsync([], program, new ModuleWorkspace.WorkspaceLibraries(["VB"], Shipped, Outside: world),
            [("Form1", Frm + "Option Explicit\r\n", FormDesigner.Read(Frm)!)], SupportedLanguages.VB6);
    }

    [TestMethod]
    [DataRow("Form1.Show", DisplayName = "Show")]
    [DataRow("Form1.Show vbModal", DisplayName = "Show vbModal")]
    [DataRow("Debug.Print Form1.Visible", DisplayName = "Visible, which loads it")]
    [DataRow("Load Form1", DisplayName = "Load")]
    public async Task AVisualBasic6Form_IsAClassThatCannotBeCreated(string statement)
    {
        var printed = await RunAsync("On Error Resume Next", statement, "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "429" }, printed);
    }

    // New makes the form: where no host can, the New raises 429, the form's Initialize does not run, and the variable is not assigned. A variable declared
    // As New is made again by each reference to it, which raises 429 again: its Is Nothing is never printed.
    [TestMethod]
    [DataRow("Dim f As Form1", "Set f = New Form1", new[] { "429", "True" }, DisplayName = "Set f = New Form1")]
    [DataRow("Dim f As New Form1", "f.Caption = \"x\"", new[] { "429" }, DisplayName = "Dim f As New Form1")]
    public async Task ANewVisualBasic6Form_IsAClassThatCannotBeCreated(string declaration, string making, string[] expected)
    {
        var printed = await RunInitializedAsync(declaration, "On Error Resume Next", making, "Debug.Print Err.Number", "Debug.Print f Is Nothing");

        CollectionAssert.AreEqual(expected, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task ANewVisualBasic6Form_WithNothingToTrapIt_StopsTheProgramThere()
    {
        ExecuteResult? result = null;
        await ModuleWorkspace.InspectAsync([], Program("Dim f As Form1", "Debug.Print \"before\"", "Set f = New Form1", "Debug.Print \"after\""),
            (_, run, _) =>
            {
                result = run;
                return Task.CompletedTask;
            },
            libraries: Libraries, language: SupportedLanguages.VB6, forms: InitializedForm);

        var said = $"{result!.Outcome} | {string.Join(" ; ", result.Output)} | {result.ErrorNumber} {result.ErrorMessage} @{result.ErrorLine}";
        Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, said);
        Assert.AreEqual(429, result.ErrorNumber, said);
        Assert.AreEqual(4, result.ErrorLine, said);
        CollectionAssert.AreEqual(new[] { "before" }, result.Output.Select(line => line.Trim()).ToArray(), said);
    }

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new(["VB"], Shipped, Outside: new ExternalWorld(UnavailableAutomationServer.Instance, UnavailableNativeLibraryHost.Instance));

    private static string Program(params string[] body) => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    // the form with an Initialize that says it ran.
    private static (string, string, FormDescriptor)[] InitializedForm
        => [("Form1", Frm + "Option Explicit\r\nPrivate Sub Form_Initialize()\r\n    Debug.Print \"Initialize\"\r\nEnd Sub\r\n", FormDesigner.Read(Frm)!)];

    private static Task<string[]> RunInitializedAsync(params string[] body)
        => ModuleWorkspace.RunAsync([], Program(body), Libraries, InitializedForm, SupportedLanguages.VB6);
}
