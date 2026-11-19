using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// The <c>Print</c> method of a Visual Basic 6.0 program's forms and PictureBoxes: written with its object, with <c>Me</c>, or with no object in a form's own
/// code. The forms are hosted by RDForms, headless; where the output ends is read back with <c>CurrentX</c> and <c>CurrentY</c>.
/// </summary>
// the active form is the UI thread's, which every RDForms window shares: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicFormPrintTests
{
    // Form5 is in twips; Picture1 is a PictureBox in it, and Text1 a TextBox, which has no Print method.
    private static readonly string Form5 = string.Join("\r\n", [
        "VERSION 5.00",
        "Begin VB.Form Form5 ",
        "   Caption         =   \"Form5\"",
        "   ClientHeight    =   3195",
        "   ClientWidth     =   4680",
        "   Begin VB.PictureBox Picture1 ",
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
        "Attribute VB_Name = \"Form5\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        "Public Sub PrintOwn()", "    Me.Print \"Score\";", "End Sub",
        "Public Sub PrintBare()", "    Print \"Score\";", "End Sub",
        string.Empty]);

    private static string Program(string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static async Task<string[]> RunAsync(string start, params string[] body)
    {
        var printed = await ModuleWorkspace.RunAsync([], Program([start, "DoEvents", .. body, "Unload Form5"]), Libraries,
            [("Form5", Form5, FormDesigner.Read(Form5)!)], SupportedLanguages.VB6);
        return [.. printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal) && line != "Paint")];
    }

    private static Task<string[]> ShownAsync(params string[] body) => RunAsync("Form5.Show", body);

    // where the output ended: whether CurrentX and CurrentY moved from 0.
    private static string Moved(string target) => $"Debug.Print ({target}.CurrentX > 0) & \" \" & ({target}.CurrentY > 0)";

    [TestMethod]
    [DataRow("Form5.Print \"Score\"", "Form5", "False True", DisplayName = "an output that ends the line goes to the start of the next")]
    [DataRow("Form5.Print \"Score\";", "Form5", "True False", DisplayName = "an output that ends with a semicolon stays on the line")]
    [DataRow("Form5.Print \"Score\",", "Form5", "True False", DisplayName = "an output that ends with a comma stays on the line")]
    [DataRow("Form5.Print", "Form5", "False True", DisplayName = "no output list prints a blank line")]
    [DataRow("Form5.Picture1.Print \"Score\";", "Form5.Picture1", "True False", DisplayName = "a PictureBox prints")]
    [DataRow("Form5.PrintOwn", "Form5", "True False", DisplayName = "Me.Print in the form's own code")]
    [DataRow("Form5.PrintBare", "Form5", "True False", DisplayName = "a Print with no object in the form's own code is the form's")]
    public async Task APrint_EndsWhereItsOutputEnds(string statement, string target, string moved)
    {
        var printed = await ShownAsync(statement, Moved(target));

        CollectionAssert.AreEqual(new[] { moved }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [Ignore("A Print of the With block's object is a syntax error: the grammar has no .Print, and the object of a Print expression cannot be left out.")]
    public async Task APrintOfTheWithBlocksObject_PrintsOnIt()
    {
        var printed = await ShownAsync("With Form5", ".Print \"Score\";", "End With", Moved("Form5"));

        CollectionAssert.AreEqual(new[] { "True False" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task TabToAColumn_AndSpc_CountColumnsAlike()
    {
        var printed = await ShownAsync("Form5.Print Tab(5);", "Dim tabbed As Single: tabbed = Form5.CurrentX", "Form5.CurrentX = 0",
            "Form5.Print Spc(4);", "Debug.Print (tabbed > 0) & \" \" & (tabbed = Form5.CurrentX)");

        CollectionAssert.AreEqual(new[] { "True True" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AFormThatIsNotShown_PrintsNothing()
    {
        var printed = await RunAsync("Load Form5", "Form5.Print \"Score\";", Moved("Form5"));

        CollectionAssert.AreEqual(new[] { "False False" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AControlThatHasNoPrintMethod_Raises438()
    {
        var printed = await ShownAsync("On Error Resume Next", "Form5.Text1.Print \"Score\"", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "438" }, printed, string.Join(" | ", printed));
    }
}
