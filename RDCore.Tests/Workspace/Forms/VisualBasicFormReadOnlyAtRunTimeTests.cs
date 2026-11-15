using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// The properties of a Visual Basic 6.0 control or form that a running program cannot set, which only a form's file and a designer set, and those it can
/// neither read nor set (measured in VB6). An early-bound assignment of a <c>TextBox</c>'s <c>MultiLine</c> and <c>ScrollBars</c>, which VB's library declares
/// with no Property Let, is a compile error. A late-bound assignment of such a property is error 382, and a late-bound read of one a program cannot reach, such
/// as a <c>Timer</c>'s <c>Left</c> or a <c>Menu</c>'s <c>Shortcut</c>, is error 393.
/// </summary>
[TestClass]
public sealed class VisualBasicFormReadOnlyAtRunTimeTests
{
    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    [TestMethod]
    [DataRow("MultiLine", "True")]
    [DataRow("ScrollBars", "2")]
    public async Task AnEarlyBoundAssignment_IsCantAssignToReadOnlyProperty(string property, string value)
    {
        var program = string.Join("\r\n", "Attribute VB_Name = \"Program\"", "Public Sub Main()", "    Dim t As VB.TextBox", $"    t.{property} = {value}", "End Sub", string.Empty);
        var errors = await ModuleWorkspace.LoadErrorsAsync([], program, Libraries, SupportedLanguages.VB6);

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        StringAssert.StartsWith(errors[0], "Can't assign to read-only property");
    }

    // a control of each class that has a property a program cannot set or cannot reach.
    private static string Form1() => string.Join("\r\n", [
        "VERSION 5.00",
        "Begin VB.Form Form1 ",
        "   Caption         =   \"Form1\"",
        "   ClientHeight    =   3195",
        "   ClientWidth     =   4680",
        "   Begin VB.TextBox Text1 ",
        "      TabIndex        =   0",
        "      Text            =   \"Text1\"",
        "   End",
        "   Begin VB.CommandButton Command1 ",
        "      Caption         =   \"Command1\"",
        "      TabIndex        =   1",
        "   End",
        "   Begin VB.Timer Timer1 ",
        "      Left            =   120",
        "      Top             =   120",
        "   End",
        "   Begin VB.Menu mnuFile ",
        "      Caption         =   \"&File\"",
        "   End",
        "End",
        "Attribute VB_Name = \"Form1\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        string.Empty]);

    // o is the object named, late-bound; the row's statement runs on it, and the error it raises is printed.
    private static string Program(string target, string statement) => string.Join("\r\n", [
        "Attribute VB_Name = \"Program\"",
        "Public Sub Main()",
        "    Load Form1",
        "    Dim o As Object",
        $"    Set o = {target}",
        "    On Error Resume Next",
        $"    {statement}",
        "    Debug.Print Err.Number & \" \" & Err.Description",
        "    Unload Form1",
        "End Sub",
        string.Empty]);

    [TestMethod]
    [DataRow("Form1.Text1", "o.MultiLine = True", "382 Set not supported at runtime", DisplayName = "TextBox.MultiLine, set")]
    [DataRow("Form1.Text1", "o.ScrollBars = 2", "382 Set not supported at runtime", DisplayName = "TextBox.ScrollBars, set")]
    [DataRow("Form1.Text1", "Debug.Print o.MultiLine & \" \" & o.ScrollBars", "False 0|0", DisplayName = "TextBox.MultiLine and ScrollBars, read")]
    [DataRow("Form1.Command1", "o.Style = 1", "382 Set not supported at runtime", DisplayName = "CommandButton.Style, set")]
    [DataRow("Form1", "o.StartUpPosition = 1", "382 Set not supported at runtime", DisplayName = "Form.StartUpPosition, set")]
    [DataRow("Form1", "o.ShowInTaskbar = False", "382 Set not supported at runtime", DisplayName = "Form.ShowInTaskbar, set")]
    [DataRow("Form1", "o.MaxButton = False", "382 Set not supported at runtime", DisplayName = "Form.MaxButton, set")]
    [DataRow("Form1.Timer1", "Debug.Print o.Left", "393 Get not supported at runtime", DisplayName = "Timer.Left, read")]
    [DataRow("Form1.Timer1", "o.Top = 10", "382 Set not supported at runtime", DisplayName = "Timer.Top, set")]
    [DataRow("Form1.Timer1", "Debug.Print o.Interval", "0|0", DisplayName = "Timer.Interval, read")]
    [DataRow("Form1.mnuFile", "Debug.Print o.Shortcut", "393 Get not supported at runtime", DisplayName = "Menu.Shortcut, read")]
    [DataRow("Form1.mnuFile", "o.WindowList = True", "382 Set not supported at runtime", DisplayName = "Menu.WindowList, set")]
    public async Task ALateBoundUse_IsVB6sRunTimeError(string target, string statement, string expected)
    {
        var form1 = Form1();
        var printed = await ModuleWorkspace.RunAsync([], Program(target, statement), Libraries, [("Form1", form1, FormDesigner.Read(form1)!)], SupportedLanguages.VB6);

        Assert.AreEqual(expected, string.Join("|", printed));
    }
}
