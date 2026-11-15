using RDCore.External;
using RDCore.External.Automation;
using RDCore.External.Native;
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

    private static DirectoryLibrarySource Shipped => new(new System.IO.Abstractions.FileSystem(), ShippedLibrariesTests.Symbols);

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
    public async Task AVisualBasic6Form_IsAClassThatCannotBeCreated(string statement)
    {
        var printed = await RunAsync("On Error Resume Next", statement, "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "429" }, printed);
    }

    [TestMethod]
    public async Task AVisualBasic6Form_ThatIsNotShown_IsNotVisible()
    {
        var printed = await RunAsync("Debug.Print Form1.Visible");

        CollectionAssert.AreEqual(new[] { "False" }, printed);
    }
}
