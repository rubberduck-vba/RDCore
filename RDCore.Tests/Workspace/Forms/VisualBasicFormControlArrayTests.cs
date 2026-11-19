using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 form's control array of controls other than menus: the controls that share a name and each have an <c>Index</c>. The array's name is an
/// object with <c>Count</c>, <c>LBound</c>, <c>UBound</c> and a default member that takes the index of an element; <c>Load</c> of an element adds a hidden copy
/// of the element of the lowest index, and <c>Unload</c> takes out one the program loaded. The forms are hosted by RDForms, headless.
/// </summary>
// the active form is the UI thread's, which every RDForms window shares: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicFormControlArrayTests
{
    // Form4 holds the TextBox txt, of no array; the PictureBox array picCell, of index 0; the CommandButton array cmd, of indexes 2 and 0 in the file's order,
    // in the Frame fra; and the hidden Label array lbl, of index 1. Its code reads and loads elements by the array's name alone. The file's positions and
    // sizes are twips, as VB6 writes them whatever the form's ScaleMode; the program reads them in pixels.
    private static string Form4()
        => string.Join("\r\n", [
            "VERSION 5.00",
            "Begin VB.Form Form4 ",
            "   Caption         =   \"Form4\"",
            "   ClientHeight    =   4000",
            "   ClientWidth     =   6000",
            "   ScaleMode       =   3  'Pixel",
            "   Begin VB.TextBox txt ",
            "      Height          =   300",
            "      Left            =   3000",
            "      TabIndex        =   0",
            "      Text            =   \"plain\"",
            "      Top             =   3000",
            "      Width           =   900",
            "   End",
            "   Begin VB.PictureBox picCell ",
            "      BackColor       =   &H000000FF&",
            "      Height          =   450",
            "      Index           =   0",
            "      Left            =   150",
            "      ScaleHeight     =   450",
            "      ScaleWidth      =   450",
            "      TabIndex        =   1",
            "      Tag             =   \"cell\"",
            "      Top             =   150",
            "      Width           =   450",
            "   End",
            "   Begin VB.Frame fra ",
            "      Caption         =   \"fra\"",
            "      Height          =   1500",
            "      Left            =   3000",
            "      TabIndex        =   2",
            "      Top             =   150",
            "      Width           =   2400",
            "      Begin VB.CommandButton cmd ",
            "         Caption         =   \"C2\"",
            "         Height          =   300",
            "         Index           =   2",
            "         Left            =   150",
            "         TabIndex        =   4",
            "         Top             =   750",
            "         Width           =   750",
            "      End",
            "      Begin VB.CommandButton cmd ",
            "         Caption         =   \"C0\"",
            "         Height          =   300",
            "         Index           =   0",
            "         Left            =   150",
            "         TabIndex        =   3",
            "         Top             =   300",
            "         Width           =   750",
            "      End",
            "   End",
            "   Begin VB.Label lbl ",
            "      Caption         =   \"L1\"",
            "      Height          =   300",
            "      Index           =   1",
            "      Left            =   150",
            "      TabIndex        =   5",
            "      Top             =   1950",
            "      Visible         =   0   'False",
            "      Width           =   750",
            "   End",
            "End",
            "Attribute VB_Name = \"Form4\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False",
            "Option Explicit",
            "Public Sub AddCells(ByVal last As Integer)",
            "    Dim k As Integer",
            "    For k = 1 To last",
            "        Load picCell(k)",
            "        picCell(k).Left = picCell(k - 1).Left + 40",
            "        picCell(k).Visible = True",
            "    Next",
            "End Sub",
            "Public Sub RemoveCell(ByVal index As Integer)", "    Unload picCell(index)", "End Sub",
            "Private Sub cmd_Click(Index As Integer)", "    Debug.Print \"cmd Click \" & Index", "End Sub",
            string.Empty]);

    private static string Program(string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static async Task AssertPrintsAsync(string[] expected, params string[] body)
    {
        var form4 = Form4();
        var printed = await ModuleWorkspace.RunAsync([], Program([.. body, "Unload Form4"]), Libraries, [("Form4", form4, FormDesigner.Read(form4)!)],
            SupportedLanguages.VB6);
        printed = [.. printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal) && line != "Paint")];

        CollectionAssert.AreEqual(expected, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("picCell", "1,0,0", DisplayName = "an array of one element")]
    [DataRow("cmd", "2,0,2", DisplayName = "an array the file lists out of index order")]
    [DataRow("lbl", "1,1,1", DisplayName = "an array that starts at 1")]
    public Task TheArray_CountsItsElements_FromTheLeastIndexToTheGreatest(string array, string expected)
        => AssertPrintsAsync([expected], $"Debug.Print Form4.{array}.Count & \",\" & Form4.{array}.LBound & \",\" & Form4.{array}.UBound");

    [TestMethod]
    public Task AnElement_IsReachedByItsIndex_AndIsTheSameObjectEachTime()
        => AssertPrintsAsync(["10", "C0", "True,True", "PictureBox"],
            "Debug.Print Form4.picCell(0).Left",
            "Debug.Print Form4.cmd.Item(0).Caption",
            "Debug.Print (Form4.picCell Is Form4.picCell) & \",\" & (Form4.picCell(0) Is Form4.picCell(0))",
            "Debug.Print TypeName(Form4.picCell(0))");

    [TestMethod]
    public Task LateBound_TheArrayIsAnObject_AndAnElementIsReachedInOneCall()
        => AssertPrintsAsync(["C2", "2", "2"], "Dim f As Object", "Set f = Form4", "Debug.Print f.cmd(2).Caption", "Debug.Print f.cmd.Count",
            "Dim a As Object", "Set a = Form4.cmd", "Debug.Print a.UBound");

    [TestMethod]
    public Task ForEach_GoesThroughTheElements_InTheOrderOfTheirIndexes()
        => AssertPrintsAsync(["C0", "C2"], "Dim c As Object", "For Each c In Form4.cmd", "    Debug.Print c.Caption", "Next");

    [TestMethod]
    public Task Load_CopiesTheElementOfTheLeastIndex_AsItIsThen_Hidden_WithTheNextTabIndex()
        => AssertPrintsAsync(["25,30,cell,False,6", "C0 changed,False,7", "3,0,1 2"],
            "Form4.picCell(0).Left = 25",
            "Load Form4.picCell(1)",
            "Debug.Print Form4.picCell(1).Left & \",\" & Form4.picCell(1).Width & \",\" & Form4.picCell(1).Tag & \",\" & Form4.picCell(1).Visible & \",\" & Form4.picCell(1).TabIndex",
            "Form4.cmd(0).Caption = \"C0 changed\"",
            "Load Form4.cmd(1)",
            "Debug.Print Form4.cmd(1).Caption & \",\" & Form4.cmd(1).Visible & \",\" & Form4.cmd(1).TabIndex",
            "Dim c As Object, s As String",
            "For Each c In Form4.cmd: s = s & \" \" & c.Index: Next",
            "Debug.Print Form4.cmd.Count & \",\" & Mid$(s, 2, 1) & \",\" & Mid$(s, 4)");

    [TestMethod]
    public Task Load_PutsTheElementInTheContainerOfTheElementItCopies()
        => AssertPrintsAsync(["Form4", "fra"],
            "Load Form4.picCell(1)", "Debug.Print Form4.picCell(1).Container.Name",
            "Load Form4.cmd(1)", "Debug.Print Form4.cmd(1).Container.Name");

    [TestMethod]
    public Task TheFormsOwnCode_LoadsPlacesAndUnloadsByTheArraysNameAlone()
        => AssertPrintsAsync(["4,3,130,True", "3,2"],
            "Form4.AddCells 3",
            "Debug.Print Form4.picCell.Count & \",\" & Form4.picCell.UBound & \",\" & Form4.picCell(3).Left & \",\" & Form4.picCell(3).Visible",
            "Form4.RemoveCell 3",
            "Debug.Print Form4.picCell.Count & \",\" & Form4.picCell.UBound");

    [TestMethod]
    public Task AnElementTheProgramLoaded_RunsTheArraysHandler_WithItsIndex()
        => AssertPrintsAsync(["cmd Click 1", "cmd Click 2"],
            "Load Form4.cmd(1)", "Form4.cmd(1).Visible = True", "Form4.cmd(1).Value = True", "Form4.cmd(2).Value = True");

    [TestMethod]
    [DataRow("Load Form4.picCell(0)", "360", DisplayName = "Load of an element that is loaded")]
    [DataRow("Unload Form4.cmd(2)", "362", DisplayName = "Unload of an element of the form's file")]
    [DataRow("Unload Form4.picCell(9)", "340", DisplayName = "Unload of an index no element has")]
    [DataRow("Debug.Print Form4.picCell(9).Left", "340", DisplayName = "a member of an index no element has")]
    [DataRow("Load Form4.picCell(-1)", "341", DisplayName = "Load of a negative index")]
    [DataRow("Load Form4.picCell(32768)", "6", DisplayName = "Load of an index past Integer")]
    [DataRow("Load Form4.txt", "361", DisplayName = "Load of a control that is in no array")]
    [DataRow("Unload Form4.txt", "361", DisplayName = "Unload of a control that is in no array")]
    [DataRow("Load Form4.picCell", "361", DisplayName = "Load of the array")]
    [DataRow("Debug.Print CallByName(Form4.picCell, \"Name\", VbGet)", "438", DisplayName = "a member the array has not")]
    [DataRow("Debug.Print Form4.txt.Index", "343", DisplayName = "the index of a control that is in no array")]
    public Task WhatVB6Refuses_RaisesWhatVB6Raises(string statement, string number)
        => AssertPrintsAsync([number, "1"], "On Error Resume Next", statement, "Debug.Print Err.Number", "Debug.Print Form4.picCell.Count");
}
