using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// The selection of a Visual Basic 6.0 TextBox set and read from code: <c>SelStart</c>, <c>SelLength</c> and <c>SelText</c>, kept within the text, with what
/// setting each does to the others. Each row is what Visual Basic 6.0 does (observed, native and p-code).
/// </summary>
[TestClass]
[Ignore("not implemented: RDForms keeps a TextBox's selection in its view, which the form host cannot reach, and its class has no SelStart, SelLength or SelText (438)")]
public sealed class VisualBasicFormTextSelectionTests
{
    private static string Form1() => string.Join("\r\n", [
        "VERSION 5.00",
        "Begin VB.Form Form1 ",
        "   Caption         =   \"Form1\"",
        "   ClientHeight    =   3195",
        "   ClientWidth     =   4680",
        "   Begin VB.TextBox Text1 ",
        "      Text            =   \"Text1\"",
        "      TabIndex        =   0",
        "   End",
        "   Begin VB.TextBox Text2 ",
        "      MultiLine       =   -1  'True",
        "      Text            =   \"Text2\"",
        "      TabIndex        =   1",
        "   End",
        "   Begin VB.TextBox Text3 ",
        "      Locked          =   -1  'True",
        "      Text            =   \"Text3\"",
        "      TabIndex        =   2",
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

    // t is the box the row sets; what it prints is its text, SelStart, SelLength, SelText and the error, a line break shown as <CR><LF>.
    private static Task<string[]> RunAsync(string box, string code)
    {
        var form1 = Form1();
        var program = string.Join("\r\n", [
            "Attribute VB_Name = \"Program\"",
            "Public Sub Main()",
            "    Load Form1",
            $"    Dim t As TextBox: Set t = Form1.{box}",
            "    On Error Resume Next",
            "    " + code,
            "    Debug.Print Shown(t.Text) & \"|\" & t.SelStart & \"|\" & t.SelLength & \"|\" & Shown(t.SelText) & \"|\" & Err.Number",
            "    Unload Form1",
            "End Sub",
            "Private Function Shown(ByVal s As String) As String",
            "    Shown = Replace(Replace(s, vbCr, \"<CR>\"), vbLf, \"<LF>\")",
            "End Function",
            string.Empty]);
        return ModuleWorkspace.RunAsync([], program, Libraries, [("Form1", form1, FormDesigner.Read(form1)!)], SupportedLanguages.VB6);
    }

    private const string Hello = "t.Text = \"Hello there\": ";

    [TestMethod]
    [DataRow("Err.Clear", "Text1|0|0||0", DisplayName = "at design time")]
    [DataRow("t.Text = \"Hello world\"", "Hello world|0|0||0", DisplayName = "setting Text resets the selection")]
    [DataRow("t.Text = \"Hello world\": t.SelStart = 6", "Hello world|6|0||0", DisplayName = "SelStart")]
    [DataRow("t.Text = \"Hello world\": t.SelStart = 6: t.SelLength = 5", "Hello world|6|5|world|0", DisplayName = "SelLength selects")]
    [DataRow("t.Text = \"Hello world\": t.SelStart = 6: t.SelLength = 5: t.SelText = \"there\"", "Hello there|11|0||0",
        DisplayName = "SelText replaces the selection, and the caret ends after it")]
    [DataRow(Hello + "t.SelStart = 2: t.SelLength = 3: t.SelStart = 4", "Hello there|4|0||0", DisplayName = "setting SelStart drops the selection")]
    [DataRow(Hello + "t.SelStart = 100", "Hello there|11|0||0", DisplayName = "SelStart past the end is the end")]
    [DataRow(Hello + "t.SelStart = 11: t.SelStart = -1", "Hello there|11|0||380", DisplayName = "a negative SelStart is 380")]
    [DataRow(Hello + "t.SelStart = 6: t.SelLength = 100", "Hello there|6|5|there|0", DisplayName = "SelLength past the end selects to the end")]
    [DataRow(Hello + "t.SelStart = 6: t.SelLength = 5: t.SelLength = -1", "Hello there|6|5|there|380", DisplayName = "a negative SelLength is 380")]
    [DataRow(Hello + "t.SelStart = 0: t.SelText = \">\"", ">Hello there|1|0||0", DisplayName = "SelText with nothing selected inserts")]
    [DataRow(Hello + "t.SelStart = 3: t.SelLength = 2: t.Text = \"new\"", "new|0|0||0", DisplayName = "setting Text after a selection resets it")]
    [DataRow(Hello + "t.SelStart = 1.6", "Hello there|2|0||0", DisplayName = "SelStart rounds")]
    [DataRow(Hello + "t.SelStart = \"2\"", "Hello there|2|0||0", DisplayName = "SelStart from a String")]
    [DataRow(Hello + "t.SelStart = 6: t.SelLength = 5: t.SelText = Null", "Hello there|6|5|there|94", DisplayName = "a Null SelText is 94")]
    [DataRow(Hello + "t.SelStart = 6: t.SelStart = Null", "Hello there|6|0||94", DisplayName = "a Null SelStart is 94")]
    [DataRow(Hello + "Dim o As Object: Set o = t: o.SelStart = 6: o.SelLength = 5", "Hello there|6|5|there|0", DisplayName = "late-bound")]
    public Task TheSelection_OfASingleLineTextBox(string code, string expected) => PrintsAsync(RunAsync("Text1", code), expected);

    [TestMethod]
    [DataRow("t.Text = \"ab\" & vbCrLf & \"cd\": t.SelStart = 3: t.SelLength = 2", "ab<CR><LF>cd|3|2|<LF>c|0", DisplayName = "the caret between the CR and the LF")]
    [DataRow("t.Text = \"ab\" & vbCrLf & \"cd\": t.SelStart = 4: t.SelLength = 1", "ab<CR><LF>cd|4|1|c|0", DisplayName = "after the line break")]
    [DataRow("t.Text = \"ab\" & vbCrLf & \"cd\": t.SelStart = Len(t.Text): t.SelText = vbCrLf & \"ef\"", "ab<CR><LF>cd<CR><LF>ef|10|0||0",
        DisplayName = "SelText at the end")]
    public Task TheSelection_OfAMultiLineTextBox(string code, string expected) => PrintsAsync(RunAsync("Text2", code), expected);

    [TestMethod]
    public Task SelText_OfALockedTextBox_IsSetFromCode()
        => PrintsAsync(RunAsync("Text3", "t.Text = \"locked\": t.SelStart = 0: t.SelLength = 1: t.SelText = \"L\""), "Locked|1|0||0");

    [TestMethod]
    public Task SelText_KeepsToMaxLength()
        => PrintsAsync(RunAsync("Text1", "t.MaxLength = 5: t.Text = \"12345\": t.SelStart = 5: t.SelText = \"678\""), "12345|5|0||0");

    [TestMethod]
    public Task SelStartAndSelLength_AreLongs_AndSelText_IsAString()
    {
        var form1 = Form1();
        var program = string.Join("\r\n", [
            "Attribute VB_Name = \"Program\"",
            "Public Sub Main()",
            "    Load Form1",
            "    Debug.Print TypeName(Form1.Text1.SelStart) & \" \" & TypeName(Form1.Text1.SelLength) & \" \" & TypeName(Form1.Text1.SelText)",
            "    Unload Form1",
            "End Sub",
            string.Empty]);
        return PrintsAsync(ModuleWorkspace.RunAsync([], program, Libraries, [("Form1", form1, FormDesigner.Read(form1)!)], SupportedLanguages.VB6), "Long Long String");
    }

    private static async Task PrintsAsync(Task<string[]> run, string expected)
    {
        var printed = await run;
        Assert.AreEqual(expected, string.Join(" | ", printed));
    }
}
