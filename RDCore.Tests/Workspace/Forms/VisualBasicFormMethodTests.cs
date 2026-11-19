using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// The methods a Visual Basic 6.0 program calls on its forms and their controls, as VB6 has them (measured in VB6): <c>Move</c>, <c>SetFocus</c>,
/// <c>ZOrder</c> and <c>Refresh</c>. The forms are hosted by RDForms, headless.
/// </summary>
// the active form and the keyboard's focus are the UI thread's, which every RDForms window shares: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicFormMethodTests
{
    // Form2 prints each Resize, and each focus event of its text boxes while Logging is set. Text3 is disabled.
    private static string Form2()
        => string.Join("\r\n", [
            "VERSION 5.00",
            "Begin VB.Form Form2 ",
            "   Caption         =   \"Form2\"",
            "   ClientHeight    =   3195",
            "   Begin VB.TextBox Text1 ",
            "      TabIndex        =   0",
            "      Text            =   \"Text1\"",
            "   End",
            "   Begin VB.TextBox Text2 ",
            "      TabIndex        =   1",
            "      Text            =   \"Text2\"",
            "   End",
            "   Begin VB.TextBox Text3 ",
            "      Enabled         =   0   'False",
            "      TabIndex        =   2",
            "      Text            =   \"Text3\"",
            "   End",
            "End",
            "Attribute VB_Name = \"Form2\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False",
            "Option Explicit",
            "Public Logging As Boolean",
            "Private Sub Form_Resize()", "    Debug.Print \"Resize \" & Width & \" \" & Height", "End Sub",
            "Private Sub Form_Paint()", "    Debug.Print \"Paint\"", "End Sub",
            "Private Sub Text1_GotFocus()", "    If Logging Then Debug.Print \"Text1 GotFocus\"", "End Sub",
            "Private Sub Text1_LostFocus()", "    If Logging Then Debug.Print \"Text1 LostFocus\"", "End Sub",
            "Private Sub Text2_GotFocus()", "    If Logging Then Debug.Print \"Text2 GotFocus\"", "End Sub",
            "Private Sub Text2_LostFocus()", "    If Logging Then Debug.Print \"Text2 LostFocus\"", "End Sub",
            string.Empty]);

    private static string Program(string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static Task<string[]> RunAsync(params string[] body)
    {
        var form2 = Form2();
        return ModuleWorkspace.RunAsync([], Program(body), Libraries, [("Form2", form2, FormDesigner.Read(form2)!)], SupportedLanguages.VB6);
    }

    // where a form or a control is, and its size, in twips.
    private static string Where(string target) => $"Debug.Print {target}.Left & \" \" & {target}.Top & \" \" & {target}.Width & \" \" & {target}.Height";

    // a shown form, moved to 1200, 1200, 6000 x 4500 before the case: what the case prints follows "base", except its Paint.
    private static async Task<(string[] Printed, string All)> AfterTheBaseMoveAsync(params string[] body)
    {
        var lines = await RunAsync(["Form2.Show", "DoEvents", "Form2.Move 1200, 1200, 6000, 4500", "Debug.Print \"base\"", .. body, "Unload Form2"]);
        var start = Array.IndexOf(lines, "base");
        Assert.IsGreaterThanOrEqualTo(0, start, string.Join(" | ", lines));
        return ([.. lines[(start + 1)..].Where(line => line != "Paint")], string.Join(" | ", lines));
    }

    // what a shown form prints after its first focus, except its Resize and its Paint.
    private static async Task<string[]> ShownAsync(params string[] body)
    {
        var lines = await RunAsync(["Form2.Show", "DoEvents", "Form2.Logging = True", .. body, "Form2.Logging = False", "Unload Form2"]);
        return [.. lines.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal) && line != "Paint")];
    }

    [TestMethod]
    [DataRow("Form2.Move 600", new[] { "600 1200 6000 4500" }, DisplayName = "Left alone")]
    [DataRow("Form2.Move 600, 900", new[] { "600 900 6000 4500" }, DisplayName = "Left and Top")]
    [DataRow("Form2.Move 600, 900, 4800", new[] { "Resize 4800 4500", "600 900 4800 4500" }, DisplayName = "Left, Top and Width, with one Resize")]
    [DataRow("Form2.Move 600, 900, 4800, 3600", new[] { "Resize 4800 3600", "600 900 4800 3600" }, DisplayName = "both sizes, with one Resize")]
    [DataRow("Form2.Move 1200, 1200, 6000, 4500", new[] { "1200 1200 6000 4500" }, DisplayName = "the size it has, with no Resize")]
    [DataRow("Form2.Move 600.5, 900.5, 4800.5, 3600.5", new[] { "Resize 4800 3600", "600 900 4800 3600" }, DisplayName = "a half twip goes to even")]
    [DataRow("Form2.Move \"600\", \"900\"", new[] { "600 900 6000 4500" }, DisplayName = "a String that is a number")]
    public async Task AFormsMove_SetsWhatItIsGiven_WithOneResizeForItsSize(string move, string[] printed)
    {
        var (after, all) = await AfterTheBaseMoveAsync(move, Where("Form2"));

        CollectionAssert.AreEqual(printed, after, all);
    }

    [TestMethod]
    [DataRow("Form2.Move 600, 900, 4800, -10", "5", DisplayName = "a negative size is 5")]
    [DataRow("Form2.Move 3E+9", "6", DisplayName = "a Left past a Long in twips is 6")]
    [DataRow("Form2.Move 600, 900, 3E+9", "6", DisplayName = "a Width past a Long in twips is 6")]
    [DataRow("Form2.Move Null", "94", DisplayName = "a Null Left is 94")]
    [DataRow("Form2.Move 600, , 4800", "448", DisplayName = "a Top left out before a Width given is 448")]
    public async Task AFormsMove_ChecksEveryValue_BeforeItMovesAnything(string move, string number)
    {
        var (after, all) = await AfterTheBaseMoveAsync("On Error Resume Next", move, "Debug.Print Err.Number", Where("Form2"));

        CollectionAssert.AreEqual(new[] { number, "1200 1200 6000 4500" }, after, all);
    }

    [TestMethod]
    [Ignore("RDForms raises no Resize for a form that was never shown")]
    public async Task AHiddenFormsMove_RaisesItsResize_InsideTheStatement()
    {
        var printed = await RunAsync("Load Form2", "Form2.Move 1200, 1200, 6000, 4500", "Debug.Print \"moved\"", "Unload Form2");

        CollectionAssert.AreEqual(new[] { "Resize 6000 4500", "moved" }, printed[^2..], string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AControlsMove_SetsItsPlaceAndSize_InItsContainersTwips()
    {
        var printed = await RunAsync("Load Form2", "Form2.Text1.Move 120, 240, 1200, 600", Where("Form2.Text1"), "Unload Form2");

        CollectionAssert.AreEqual(new[] { "120 240 1200 600" }, printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal)).ToArray(), string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task SetFocus_OnAFormThatIsNotShown_Is5_OnAControl_AndOnTheForm()
    {
        var printed = await RunAsync("Load Form2", "On Error Resume Next", "Form2.Text2.SetFocus", "Debug.Print Err.Number", "Err.Clear", "Form2.SetFocus",
            "Debug.Print Err.Number", "Unload Form2");

        CollectionAssert.AreEqual(new[] { "5", "5" }, printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal)).ToArray(), string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task SetFocus_OnADisabledControl_Is5_AndMovesNoFocus()
    {
        var printed = await ShownAsync("On Error Resume Next", "Form2.Text3.SetFocus", "Debug.Print Err.Number", "DoEvents");

        CollectionAssert.AreEqual(new[] { "5" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task SetFocus_MovesTheFocusAfterTheCall_LostFocusFirst()
    {
        var printed = await ShownAsync("Form2.Text2.SetFocus", "Debug.Print \"after SetFocus\"", "DoEvents");

        CollectionAssert.AreEqual(new[] { "after SetFocus", "Text1 LostFocus", "Text2 GotFocus" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task SetFocus_OnTheFocusedControl_RaisesNothing()
    {
        var printed = await ShownAsync("Form2.Text1.SetFocus", "Form2.SetFocus", "Debug.Print \"after SetFocus\"", "DoEvents");

        CollectionAssert.AreEqual(new[] { "after SetFocus" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("", "0", DisplayName = "none brings it to the front")]
    [DataRow("2", "5", DisplayName = "2 is 5")]
    [DataRow("-1", "5", DisplayName = "-1 is 5")]
    [DataRow("0.6", "0", DisplayName = "0.6 is 1")]
    [DataRow("\"1\"", "0", DisplayName = "a String that is a number")]
    [DataRow("-0.5", "0", DisplayName = "-0.5 goes to even, 0")]
    [DataRow("Empty", "0", DisplayName = "Empty is 0")]
    [DataRow("1.5", "5", DisplayName = "1.5 goes to even, 2, and is 5")]
    [DataRow("\"x\"", "13", DisplayName = "a String that is no number is 13")]
    [DataRow("32768", "6", DisplayName = "past an Integer is 6")]
    [DataRow("Null", "13", DisplayName = "Null is 13")]
    public async Task ZOrder_TakesAPositionOf0Or1_AsAnInteger(string position, string number)
    {
        var printed = await ShownAsync("On Error Resume Next", $"Form2.Text2.ZOrder {position}", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { number }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task ZOrder_DoesNotReorderTheControlsCollection()
    {
        var printed = await ShownAsync("Form2.Text1.ZOrder 1", "Form2.Text3.ZOrder 0", "Dim c As Object", "For Each c In Form2.Controls", "Debug.Print c.Name", "Next");

        CollectionAssert.AreEqual(new[] { "Text1", "Text2", "Text3" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task Refresh_RunsOnAControl()
    {
        var printed = await ShownAsync("On Error Resume Next", "Form2.Text1.Refresh", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "0" }, printed, string.Join(" | ", printed));
    }

    // RDForms' own rule, not measured with VB6: Refresh on a form whose AutoRedraw is False raises Paint inside the call.
    [TestMethod]
    public async Task Refresh_OnAFormWhoseAutoRedrawIsFalse_RaisesPaintInsideTheCall()
    {
        var printed = await RunAsync("Form2.Show", "DoEvents", "Debug.Print \"before\"", "Form2.Refresh", "Debug.Print \"after\"", "Unload Form2");

        CollectionAssert.AreEqual(new[] { "before", "Paint", "after" }, printed.SkipWhile(line => line != "before").Take(3).ToArray(), string.Join(" | ", printed));
    }
}
