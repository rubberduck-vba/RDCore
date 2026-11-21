using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A running program's <c>BackColor</c> on a transparent <c>Label</c> is dropped, as VB6 drops it: no error, the property reads back the color the label
/// had, and making the label opaque shows that color. Once opaque, the label takes the next one.
/// </summary>
[TestClass]
public sealed class VisualBasicLabelBackColorTests
{
    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    // a transparent label, red from the form's file.
    private static string Form1() => string.Join("\r\n", [
        "VERSION 5.00",
        "Begin VB.Form Form1 ",
        "   Caption         =   \"Form1\"",
        "   ClientHeight    =   3195",
        "   ClientWidth     =   4680",
        "   Begin VB.Label Label1 ",
        "      BackColor       =   &H000000FF&",
        "      BackStyle       =   0  'Transparent",
        "      Caption         =   \"Label1\"",
        "   End",
        "End",
        "Attribute VB_Name = \"Form1\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        string.Empty]);

    private static string Program(params string[] statements) => string.Join("\r\n", [
        "Attribute VB_Name = \"Program\"",
        "Public Sub Main()",
        "    Load Form1",
        "    On Error Resume Next",
        .. statements.Select(statement => $"    {statement}"),
        "    Unload Form1",
        "End Sub",
        string.Empty]);

    private static async Task<string> PrintedAsync(params string[] statements)
    {
        var form1 = Form1();
        var printed = await ModuleWorkspace.RunAsync(
            [], Program(statements), Libraries, [("Form1", form1, FormDesigner.Read(form1)!)], SupportedLanguages.VB6);
        return string.Join("|", printed);
    }

    [TestMethod]
    public async Task ABackColor_AssignedWhileTransparent_IsDropped_AndOpaqueTheLabelTakesTheNextOne()
        => Assert.AreEqual("255 0|255|65280", await PrintedAsync(
            "Form1.Label1.BackColor = vbBlue",
            "Debug.Print Form1.Label1.BackColor & \" \" & Err.Number",
            "Form1.Label1.BackStyle = 1",
            "Debug.Print Form1.Label1.BackColor",
            "Form1.Label1.BackColor = vbGreen",
            "Debug.Print Form1.Label1.BackColor"));

    [TestMethod]
    public async Task ABackColor_AssignedLateBoundWhileTransparent_IsDroppedToo()
        => Assert.AreEqual("255 0", await PrintedAsync(
            "Dim o As Object",
            "Set o = Form1.Label1",
            "o.BackColor = vbBlue",
            "Debug.Print o.BackColor & \" \" & Err.Number"));
}
