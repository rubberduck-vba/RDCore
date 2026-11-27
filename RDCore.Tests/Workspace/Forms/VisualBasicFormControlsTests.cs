using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 form's <c>Controls</c> collection, and the <c>Control</c> type a variable is declared as, as VB6 has them (measured in VB6). The form is
/// hosted by RDForms, headless, and is loaded, not shown.
/// </summary>
[TestClass]
public sealed class VisualBasicFormControlsTests
{
    // Command1, Text1, Frame1 with Text2 in it, the control array Command2, Text3, and the menu mnuFile with its item mnuOpen.
    private static string Form1()
        => string.Join("\r\n", [
            "VERSION 5.00",
            "Begin VB.Form Form1 ",
            "   Caption         =   \"Form1\"",
            "   ClientHeight    =   3000",
            "   ClientWidth     =   5000",
            "   Begin VB.CommandButton Command1 ",
            "      Caption         =   \"Command1\"",
            "      TabIndex        =   0",
            "   End",
            "   Begin VB.TextBox Text1 ",
            "      TabIndex        =   1",
            "      Text            =   \"Text1\"",
            "   End",
            "   Begin VB.Frame Frame1 ",
            "      Caption         =   \"Frame1\"",
            "      TabIndex        =   2",
            "      Begin VB.TextBox Text2 ",
            "         TabIndex        =   3",
            "         Text            =   \"Text2\"",
            "      End",
            "   End",
            "   Begin VB.CommandButton Command2 ",
            "      Caption         =   \"Command2\"",
            "      Index           =   0",
            "      TabIndex        =   4",
            "   End",
            "   Begin VB.CommandButton Command2 ",
            "      Caption         =   \"Command2\"",
            "      Index           =   1",
            "      TabIndex        =   5",
            "   End",
            "   Begin VB.TextBox Text3 ",
            "      TabIndex        =   6",
            "      Text            =   \"Text3\"",
            "   End",
            "   Begin VB.Menu mnuFile ",
            "      Caption         =   \"File\"",
            "      Begin VB.Menu mnuOpen ",
            "         Caption         =   \"Open\"",
            "      End",
            "   End",
            "End",
            "Attribute VB_Name = \"Form1\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False",
            "Option Explicit",
            string.Empty]);

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static Task<string[]> RunAsync(params string[] body)
    {
        var form1 = Form1();
        var program = "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\nLoad Form1\r\n"
            + $"{string.Join("\r\n", body)}\r\nUnload Form1\r\nEnd Sub\r\n";
        return ModuleWorkspace.RunAsync([], program, Libraries, [("Form1", form1, FormDesigner.Read(form1)!)], SupportedLanguages.VB6);
    }

    [TestMethod]
    public async Task TheName_OfAControlAndOfTheForm_IsTheOneTheFormsFileGivesIt()
    {
        var printed = await RunAsync("Dim o As Object", "Set o = Form1.Text1", "Debug.Print Form1.Name & \" \" & Form1.Text1.Name & \" \" & o.Name");

        CollectionAssert.AreEqual(new[] { "Form1 Text1 Text1" }, printed, string.Join(" | ", printed));
    }

    // ---- the collection ----

    [TestMethod]
    public async Task TheControls_AreEveryControlOfTheForm_MenusIncluded_InTheOrderOfItsFile_DepthFirst()
    {
        var printed = await RunAsync(
            "Debug.Print Form1.Controls.Count & \" \" & TypeName(Form1.Controls)",
            "Dim o As Object",
            "For Each o In Form1.Controls",
            "Debug.Print o.Name & \" \" & TypeName(o)",
            "Next");

        CollectionAssert.AreEqual(
            new[]
            {
                "9 Object", "Command1 CommandButton", "Text1 TextBox", "Frame1 Frame", "Text2 TextBox", "Command2 CommandButton", "Command2 CommandButton",
                "Text3 TextBox", "mnuFile Menu", "mnuOpen Menu",
            },
            printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task TheControls_ByPosition_AreTheOrderForEachGoesIn()
    {
        var printed = await RunAsync("Dim i As Long", "For i = 0 To Form1.Controls.Count - 1", "Debug.Print i & \" \" & Form1.Controls(i).Name", "Next");

        CollectionAssert.AreEqual(
            new[] { "0 Command1", "1 Text1", "2 Frame1", "3 Text2", "4 Command2", "5 Command2", "6 Text3", "7 mnuFile", "8 mnuOpen" },
            printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Form1.Controls(0)", "Command1|0|", DisplayName = "the first, at 0")]
    [DataRow("Form1.Controls(8)", "mnuOpen|0|", DisplayName = "the last, a menu's item")]
    [DataRow("Form1.Controls(1.5)", "Frame1|0|", DisplayName = "1.5 goes to even, 2")]
    [DataRow("Form1.Controls(\"2\")", "Frame1|0|", DisplayName = "a String that reads as a number is a position")]
    [DataRow("Form1.Controls(\"text1\")", "Text1|0|", DisplayName = "a name, whatever its case")]
    [DataRow("Form1.Controls.Item(\"Text1\")", "Text1|0|", DisplayName = "Item, by its name")]
    [DataRow("Form1.Controls(\"Text2\")", "Text2|0|", DisplayName = "a control in a Frame")]
    [DataRow("Form1.Controls(\"mnuOpen\")", "mnuOpen|0|", DisplayName = "a menu's item")]
    [DataRow("Form1.Controls(9)", "|9|Subscript out of range", DisplayName = "past the last is error 9")]
    [DataRow("Form1.Controls(-1)", "|9|Subscript out of range", DisplayName = "before the first is error 9")]
    [DataRow("Form1.Controls(\"Nope\")", "|730|Control 'Nope' not found", DisplayName = "a name no control has is error 730")]
    [DataRow("Form1.Controls(\"Form1\")", "|730|Control 'Form1' not found", DisplayName = "the form's own name is error 730")]
    [DataRow("Form1.Controls(Null)", "|13|Type mismatch", DisplayName = "Null is error 13")]
    public async Task AControl_OfTheCollection_IsReachedByItsPosition_OrByItsName(string item, string printed)
    {
        var lines = await RunAsync("Dim x As Variant", $"x = {item}.Name", "Debug.Print x & \"|\" & Err.Number & \"|\" & Err.Description");

        CollectionAssert.AreEqual(new[] { printed }, lines, string.Join(" | ", lines));
    }

    [TestMethod]
    [Ignore("not implemented: a control array of an RDForms form as an object of its own, reached by its name in Controls")]
    public async Task AControlArray_ReachedByItsName_IsTheArray_WhichHasNoName()
    {
        var lines = await RunAsync("Dim x As Variant", "x = Form1.Controls(\"Command2\").Name", "Debug.Print x & \"|\" & Err.Number & \"|\" & Err.Description");

        CollectionAssert.AreEqual(new[] { "|438|Object doesn't support this property or method" }, lines, string.Join(" | ", lines));
    }

    [TestMethod]
    public async Task AControl_OfTheCollection_IsTheControlItself()
    {
        var printed = await RunAsync("Debug.Print Form1.Controls(\"Text1\") Is Form1.Text1");

        CollectionAssert.AreEqual(new[] { "True" }, printed, string.Join(" | ", printed));
    }

    // ---- the Control type ----

    // each one set to an Object first, so that a refusal is raised when the program runs.
    [TestMethod]
    [DataRow("Form1.Text1", "0|True|True", DisplayName = "a TextBox")]
    [DataRow("Form1.Frame1", "0|True|True", DisplayName = "a Frame")]
    [DataRow("Form1.Controls(\"Text2\")", "0|True|True", DisplayName = "a control of the collection")]
    [DataRow("Form1.mnuFile", "0|True|True", DisplayName = "a menu")]
    [DataRow("Form1", "13|False|False", DisplayName = "the form is error 13")]
    [DataRow("Form1.Controls", "13|False|False", DisplayName = "the collection is error 13")]
    public async Task AVariableAsControl_TakesEveryControlOfTheForm_AndNothingElse(string source, string printed)
    {
        var lines = await RunAsync(
            "Dim o As Object, c As Control",
            $"Set o = {source}",
            "Set c = o",
            "Debug.Print Err.Number & \"|\" & (c Is o) & \"|\" & (TypeOf o Is Control)");

        CollectionAssert.AreEqual(new[] { printed }, lines, string.Join(" | ", lines));
    }

    [TestMethod]
    public async Task AMemberReachedThroughAControl_IsLateBound_AsThroughAnObject()
    {
        var printed = await RunAsync(
            "Dim c As Control, x As Variant",
            "Set c = Form1.Text1",
            "Debug.Print c.Text",
            "x = c.Nope",
            "Debug.Print Err.Number & \"|\" & Err.Description");

        CollectionAssert.AreEqual(new[] { "Text1", "438|Object doesn't support this property or method" }, printed, string.Join(" | ", printed));
    }
}
