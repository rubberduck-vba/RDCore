using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 program's graphics statements and methods on its forms and PictureBoxes: <c>Line</c>, <c>Circle</c>, <c>PSet</c> and <c>Scale</c>,
/// written with the object they draw on, and <c>Cls</c>, <c>Point</c>, <c>CurrentX</c> and <c>CurrentY</c>. The forms are hosted by RDForms, headless, and
/// what is drawn is read back with <c>Point</c>.
/// </summary>
// the active form is the UI thread's, which every RDForms window shares: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicFormGraphicsTests
{
    // Form4 is white, in twips, 4680 x 3195 inside; Picture1 is a white PictureBox in it, 1440 x 1440 inside, and Text1 a TextBox, which has
    // no surface. autoRedraw: the form and Picture1 keep what is drawn on them.
    private static string Form4(bool autoRedraw = false)
        => string.Join("\r\n", [
            "VERSION 5.00",
            "Begin VB.Form Form4 ",
            .. AutoRedraw("   ", autoRedraw),
            "   BackColor       =   &H00FFFFFF&",
            "   Caption         =   \"Form4\"",
            "   ClientHeight    =   3195",
            "   ClientWidth     =   4680",
            "   Begin VB.PictureBox Picture1 ",
            .. AutoRedraw("      ", autoRedraw),
            "      BackColor       =   &H00FFFFFF&",
            "      Height          =   1500",
            "      Left            =   2400",
            "      ScaleHeight     =   1440",
            "      ScaleWidth      =   1440",
            "      TabIndex        =   1",
            "      Top             =   1200",
            "      Width           =   1500",
            "   End",
            "   Begin VB.TextBox Text1 ",
            "      Height          =   285",
            "      Left            =   120",
            "      TabIndex        =   0",
            "      Text            =   \"Text1\"",
            "      Top             =   2700",
            "      Width           =   1215",
            "   End",
            "End",
            "Attribute VB_Name = \"Form4\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False",
            "Option Explicit",
            "Public Sub DrawOwnLine()", "    Me.Line (0, 0)-(600, 300)", "End Sub",
            "Public Sub DrawBare(ByVal which As Integer)",
            "    Select Case which",
            "        Case 1: Line (0, 0)-(600, 300)",
            "        Case 2: PSet (100, 200)",
            "        Case 3: Circle (1000, 1200), 100",
            "        Case 4: Scale (0, 0)-(100, 50)",
            "    End Select",
            "End Sub",
            string.Empty]);

    private static string[] AutoRedraw(string indent, bool autoRedraw) => autoRedraw ? [$"{indent}AutoRedraw      =   -1  'True"] : [];

    private static string Program(string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    // what a shown Form4 prints, except its Resize and its Paint.
    private static async Task<string[]> ShownAsync(params string[] body)
    {
        var form4 = Form4();
        var printed = await ModuleWorkspace.RunAsync([], Program(["Form4.Show", "DoEvents", .. body, "Unload Form4"]), Libraries,
            [("Form4", form4, FormDesigner.Read(form4)!)], SupportedLanguages.VB6);
        return [.. printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal) && line != "Paint")];
    }

    // what Form4 prints when it is loaded and not shown, and the body is run, except its Resize and its Paint.
    private static async Task<string[]> HiddenAsync(bool autoRedraw, params string[] body)
    {
        var form4 = Form4(autoRedraw);
        var printed = await ModuleWorkspace.RunAsync([], Program(["Load Form4", .. body, "Unload Form4"]), Libraries,
            [("Form4", form4, FormDesigner.Read(form4)!)], SupportedLanguages.VB6);
        return [.. printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal) && line != "Paint")];
    }

    // where the next drawing of an object starts.
    private static string Current(string target) => $"Debug.Print {target}.CurrentX & \" \" & {target}.CurrentY";

    [TestMethod]
    [DataRow("Form4.PSet (300, 450)", "300 450", DisplayName = "PSet ends at its point")]
    [DataRow("Form4.Line (0, 0)-(1500, 750)", "1500 750", DisplayName = "Line ends at its second point")]
    [DataRow("Form4.CurrentX = 100: Form4.CurrentY = 100: Form4.Line -Step(200, 300)", "300 400", DisplayName = "Line with no first point starts where the last one ended")]
    [DataRow("Form4.Line (100, 100)-Step(200, 300)", "300 400", DisplayName = "a second point with Step is measured from the first")]
    [DataRow("Form4.CurrentX = 100: Form4.CurrentY = 100: Form4.PSet Step(50, 50)", "150 150", DisplayName = "PSet with Step is measured from where the last one ended")]
    [DataRow("Form4.Circle (1000, 1200), 500", "1000 1200", DisplayName = "Circle ends at its center")]
    [DataRow("With Form4: .Line (0, 0)-(20, 30): End With", "20 30", DisplayName = "a Line of the With block's object")]
    [DataRow("Form4.DrawOwnLine", "600 300", DisplayName = "Me.Line in the form's own code")]
    [DataRow("Form4.DrawBare 1", "600 300", DisplayName = "a Line with no object in the form's own code is the form's")]
    [DataRow("Form4.DrawBare 2", "100 200", DisplayName = "a PSet with no object in the form's own code is the form's")]
    [DataRow("Form4.DrawBare 3", "1000 1200", DisplayName = "a Circle with no object in the form's own code is the form's")]
    public async Task ADrawingStatement_EndsWhereItsFigureEnds(string statement, string current)
    {
        var printed = await ShownAsync(statement, Current("Form4"));

        CollectionAssert.AreEqual(new[] { current }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Form4.PSet (1500, 1500), &HFF&", "1500, 1500", "255", DisplayName = "PSet draws its point in its color")]
    [DataRow("Form4.Line (300, 300)-(900, 900), &HFF0000, BF", "600, 600", "16711680", DisplayName = "a filled box is filled with its color")]
    [DataRow("Form4.Line (300, 300)-(900, 900), &HFF0000, B", "600, 600", "16777215", DisplayName = "a box is not filled")]
    [DataRow("Form4.ForeColor = &HFF00&: Form4.Line (0, 600)-(4680, 600)", "1500, 600", "65280", DisplayName = "a color left out is ForeColor")]
    [DataRow("Form4.PSet (1500, 1500), &HFF&: Form4.Cls", "1500, 1500", "16777215", DisplayName = "Cls clears what was drawn")]
    [DataRow("", "1500, 1500", "16777215", DisplayName = "Point where nothing was drawn is BackColor")]
    [DataRow("", "-10, -10", "-1", DisplayName = "Point outside the form is -1")]
    public async Task Point_ReadsWhatWasDrawn(string drawing, string point, string color)
    {
        var printed = await ShownAsync(drawing, $"Debug.Print Form4.Point({point})");

        CollectionAssert.AreEqual(new[] { color }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task Cls_SetsCurrentXAndCurrentYTo0()
    {
        var printed = await ShownAsync("Form4.PSet (300, 450)", "Form4.Cls", Current("Form4"));

        CollectionAssert.AreEqual(new[] { "0 0" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task Scale_SetsAUserScale()
    {
        var printed = await ShownAsync("Form4.Scale (0, 0)-(100, 50)", "Debug.Print Form4.ScaleWidth & \" \" & Form4.ScaleHeight & \" \" & Form4.ScaleMode");

        CollectionAssert.AreEqual(new[] { "100 50 0" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AScaleWithNoObject_InTheFormsOwnCode_SetsTheFormsScale()
    {
        var printed = await ShownAsync("Form4.DrawBare 4", "Debug.Print Form4.ScaleWidth & \" \" & Form4.ScaleHeight");

        CollectionAssert.AreEqual(new[] { "100 50" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [Ignore("not implemented: the parser reads Scale written with no points as a call of the method, whose Flags is not optional")]
    public async Task ScaleWithNoPoints_SetsTwipsBack()
    {
        var printed = await ShownAsync("Form4.Scale (0, 0)-(100, 50)", "Form4.Scale", "Debug.Print Form4.ScaleWidth & \" \" & Form4.ScaleMode");

        CollectionAssert.AreEqual(new[] { "4680 1" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task APictureBox_DrawsOnItsOwnSurface()
    {
        var printed = await ShownAsync("Form4.Picture1.PSet (100, 200), &HFF&", Current("Form4.Picture1"), "Debug.Print Form4.Picture1.Point(100, 200)",
            Current("Form4"));

        CollectionAssert.AreEqual(new[] { "100 200", "255", "0 0" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Form4.PSet (\"x\", 0)", "13", DisplayName = "a coordinate that is no number is 13")]
    [DataRow("Form4.PSet (0, 0), \"x\"", "13", DisplayName = "a color that is no number is 13")]
    [DataRow("Form4.Text1.Line (0, 0)-(10, 10)", "438", DisplayName = "a TextBox has no Line")]
    public async Task ADrawingStatement_ThatCannotDraw_RaisesItsError(string statement, string number)
    {
        var printed = await ShownAsync("On Error Resume Next", statement, "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { number }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Form4.Line (0, 0)-(1000, 1000)", false)]
    [DataRow("Form4.Circle (500, 500), 300", false)]
    [DataRow("Form4.PSet (10, 10)", false)]
    [DataRow("Form4.Cls", false)]
    [DataRow("Form4.Picture1.Line (0, 0)-(1000, 1000)", false)]
    [DataRow("Form4.Picture1.Cls", false)]
    [DataRow("Form4.Line (0, 0)-(1000, 1000)", true)]
    [DataRow("Form4.Picture1.PSet (10, 10)", true)]
    public async Task ADrawingStatement_OnAFormThatIsNotShown_RaisesNothing(string statement, bool autoRedraw)
    {
        var printed = await HiddenAsync(autoRedraw, "On Error Resume Next", statement, "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "0" }, printed, string.Join(" | ", printed));
    }

    // Point while the form is not shown, then once it is.
    private static string[] DrawnThenShown(string target)
        => [$"{target}.PSet (700, 500), &HFF&", $"Debug.Print {target}.Point(700, 500)", "Form4.Show", "DoEvents", $"Debug.Print {target}.Point(700, 500)"];

    [TestMethod]
    [DataRow("Form4", DisplayName = "on the form")]
    [DataRow("Form4.Picture1", DisplayName = "on its PictureBox")]
    public async Task WhatIsDrawnOnAFormThatIsNotShown_IsLost_WithoutAutoRedraw(string target)
    {
        var printed = await HiddenAsync(false, DrawnThenShown(target));

        CollectionAssert.AreEqual(new[] { "-1", "16777215" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [Ignore("not implemented: RDForms gives a form its drawing surface when it is shown, so a form that is loaded and not shown keeps nothing drawn on it, whatever its AutoRedraw")]
    [DataRow("Form4", DisplayName = "on the form")]
    [DataRow("Form4.Picture1", DisplayName = "on its PictureBox")]
    public async Task WhatIsDrawnOnAFormThatIsNotShown_IsKept_WithAutoRedraw(string target)
    {
        var printed = await HiddenAsync(true, DrawnThenShown(target));

        CollectionAssert.AreEqual(new[] { "255", "255" }, printed, string.Join(" | ", printed));
    }
}
