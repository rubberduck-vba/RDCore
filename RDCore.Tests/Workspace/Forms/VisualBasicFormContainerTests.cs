using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 control's <c>Container</c>: the form, Frame or PictureBox that holds it, read as its object and changed with <c>Set</c>, as VB6 does. The
/// forms are hosted by RDForms, headless.
/// </summary>
// the active form is the UI thread's, which every RDForms window shares: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicFormContainerTests
{
    // Form1, in twips, holds the CommandButton cmd at 200, 300, the TextBox txt, the Frame fra, the PictureBox pic of ScaleMode 3, and the Timer tmr.
    private static string Form1()
        => string.Join("\r\n", [
            "VERSION 5.00",
            "Begin VB.Form Form1 ",
            "   Caption         =   \"Form1\"",
            "   ClientHeight    =   4000",
            "   ClientWidth     =   6000",
            "   Begin VB.CommandButton cmd ",
            "      Caption         =   \"cmd\"",
            "      Height          =   300",
            "      Left            =   200",
            "      TabIndex        =   0",
            "      Top             =   300",
            "      Width           =   900",
            "   End",
            "   Begin VB.TextBox txt ",
            "      Height          =   300",
            "      Left            =   200",
            "      TabIndex        =   1",
            "      Top             =   3000",
            "      Width           =   900",
            "   End",
            "   Begin VB.Frame fra ",
            "      Caption         =   \"fra\"",
            "      Height          =   1500",
            "      Left            =   3000",
            "      TabIndex        =   2",
            "      Top             =   150",
            "      Width           =   2400",
            "   End",
            "   Begin VB.PictureBox pic ",
            "      Height          =   1500",
            "      Left            =   3000",
            "      ScaleHeight     =   96",
            "      ScaleMode       =   3  'Pixel",
            "      ScaleWidth      =   156",
            "      TabIndex        =   3",
            "      Top             =   2000",
            "      Width           =   2400",
            "   End",
            "   Begin VB.Timer tmr ",
            "      Left            =   0",
            "      Top             =   0",
            "   End",
            "End",
            "Attribute VB_Name = \"Form1\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False",
            "Option Explicit",
            string.Empty]);

    private static string Program(string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static async Task AssertPrintsAsync(string[] expected, params string[] body)
    {
        var form1 = Form1();
        var printed = await ModuleWorkspace.RunAsync([], Program([.. body, "Unload Form1"]), Libraries, [("Form1", form1, FormDesigner.Read(form1)!)],
            SupportedLanguages.VB6);
        printed = [.. printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal) && line != "Paint")];

        CollectionAssert.AreEqual(expected, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public Task AControlsContainer_IsTheObjectThatHoldsIt()
        => AssertPrintsAsync(["True,Form1"], "Debug.Print (Form1.cmd.Container Is Form1) & \",\" & Form1.cmd.Container.Name");

    // VB6 keeps the control's place in pixels: 200, 300 twips in the form read 13.33333, 20 in a PictureBox of ScaleMode 3.
    [TestMethod]
    public Task SetContainer_PlacesTheControl_AndItsPlaceReadsInTheNewScale()
        => AssertPrintsAsync(["True,13.333,20", "fra,200,300"],
            "Set Form1.cmd.Container = Form1.pic",
            "Debug.Print (Form1.cmd.Container Is Form1.pic) & \",\" & Round(Form1.cmd.Left, 3) & \",\" & Form1.cmd.Top",
            "Set Form1.cmd.Container = Form1.fra",
            "Debug.Print Form1.cmd.Container.Name & \",\" & Form1.cmd.Left & \",\" & Form1.cmd.Top");

    [TestMethod]
    public Task SetContainer_LateBound_PlacesTheControl()
        => AssertPrintsAsync(["fra"], "Dim c As Object, f As Object", "Set c = Form1.cmd", "Set f = Form1.fra", "Set c.Container = f",
            "Debug.Print c.Container.Name");

    [TestMethod]
    [DataRow("Set Form1.cmd.Container = Form1.txt", "425", DisplayName = "a control that holds none")]
    [DataRow("Set Form1.cmd.Container = Nothing", "425", DisplayName = "Nothing")]
    [DataRow("Set Form1.fra.Container = Form1.fra", "425", DisplayName = "the control itself")]
    [DataRow("Form1.cmd.Container = Form1.fra", "438", DisplayName = "an assignment without Set")]
    [DataRow("Debug.Print Form1.tmr.Container.Name", "438", DisplayName = "a Timer's")]
    public Task WhatVB6Refuses_RaisesWhatVB6Raises_AndTheControlStays(string statement, string number)
        => AssertPrintsAsync([number, "Form1"], "On Error Resume Next", statement, "Debug.Print Err.Number", "Debug.Print Form1.cmd.Container.Name");
}
