using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 program's <c>PopupMenu</c>: the form pops up the items of one of its menus, and the call returns once the menu closes. The forms are hosted
/// by RDForms, headless; what a user chooses from the menu is driven by the RDForms host's own rows.
/// </summary>
// the active form is the UI thread's, which every RDForms window shares: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicFormPopupMenuTests
{
    // Form3 keeps two hidden top-level menus to pop up: mnuEdit holds Copy, and mnuEmpty holds none. Each menu's Click prints its name.
    private static string Form3()
        => string.Join("\r\n", [
            "VERSION 5.00",
            "Begin VB.Form Form3 ",
            "   Caption         =   \"Form3\"",
            "   ClientHeight    =   3195",
            "   ClientWidth     =   4680",
            "   Begin VB.Menu mnuEdit ",
            "      Caption         =   \"&Edit\"",
            "      Visible         =   0   'False",
            "      Begin VB.Menu mnuCopy ",
            "         Caption         =   \"&Copy\"",
            "      End",
            "   End",
            "   Begin VB.Menu mnuEmpty ",
            "      Caption         =   \"E&mpty\"",
            "      Visible         =   0   'False",
            "   End",
            "End",
            "Attribute VB_Name = \"Form3\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False",
            "Option Explicit",
            "Private Sub mnuEdit_Click()", "    Debug.Print \"mnuEdit Click\"", "End Sub",
            "Private Sub mnuCopy_Click()", "    Debug.Print \"mnuCopy Click\"", "End Sub",
            "Private Sub mnuEmpty_Click()", "    Debug.Print \"mnuEmpty Click\"", "End Sub",
            string.Empty]);

    private static string Program(string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static async Task<string[]> RunAsync(params string[] body)
    {
        var form3 = Form3();
        var printed = await ModuleWorkspace.RunAsync([], Program(body), Libraries, [("Form3", form3, FormDesigner.Read(form3)!)], SupportedLanguages.VB6);
        return [.. printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal) && line != "Paint")];
    }

    [TestMethod]
    public async Task PopupMenu_OfAMenuThatHoldsNoItems_PopsUpNothing_AndReturns()
    {
        var printed = await RunAsync("Form3.Show", "DoEvents", "Form3.PopupMenu Form3.mnuEmpty", "Debug.Print \"back\"", "Unload Form3");

        CollectionAssert.AreEqual(new[] { "back" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Form3.PopupMenu Form3.mnuEmpty, 4", DisplayName = "centered")]
    [DataRow("Form3.PopupMenu Form3.mnuEmpty, 8, 120, 240", DisplayName = "right-aligned, at a point")]
    [DataRow("Form3.PopupMenu Form3.mnuEmpty, 2, , , Form3.mnuEmpty", DisplayName = "the right button, with a bold item")]
    public async Task PopupMenu_TakesItsOptionalArguments(string call)
    {
        var printed = await RunAsync("Form3.Show", "DoEvents", "On Error Resume Next", call, "Debug.Print Err.Number", "Unload Form3");

        CollectionAssert.AreEqual(new[] { "0" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [Ignore("not measured with VB6: PopupMenu of an object that is not a menu of the form; RD-VBA raises 5")]
    [DataRow("Form3.PopupMenu Form3", DisplayName = "the form itself")]
    [DataRow("Form3.PopupMenu Form3.mnuEdit, , , , Form3", DisplayName = "a bold item that is not a menu")]
    public async Task PopupMenu_OfWhatIsNotAMenu_Is5(string call)
    {
        var printed = await RunAsync("Form3.Show", "DoEvents", "On Error Resume Next", call, "Debug.Print Err.Number", "Unload Form3");

        CollectionAssert.AreEqual(new[] { "5" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [Ignore("not measured with VB6: PopupMenu on a form that is loaded but not shown; RD-VBA pops up nothing, raises nothing, and returns")]
    public async Task PopupMenu_OnAFormThatIsNotShown_PopsUpNothing()
    {
        var printed = await RunAsync("Load Form3", "On Error Resume Next", "Form3.PopupMenu Form3.mnuEdit", "Debug.Print Err.Number", "Unload Form3");

        CollectionAssert.AreEqual(new[] { "0" }, printed, string.Join(" | ", printed));
    }
}
