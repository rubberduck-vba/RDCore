using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// The default member of a Visual Basic 6.0 control, which the code does not name: the member its class names for it, read and assigned as if the code named
/// it, early bound and late bound.
/// </summary>
[TestClass]
public sealed class VisualBasicFormDefaultMemberTests
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
        "   Begin VB.CheckBox Check1 ",
        "      Caption         =   \"Check1\"",
        "      TabIndex        =   1",
        "   End",
        "   Begin VB.Label Label1 ",
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

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static Task<string[]> RunAsync(params string[] body)
    {
        var form1 = Form1();
        var program = string.Join("\r\n", [
            "Attribute VB_Name = \"Program\"",
            "Public Sub Main()",
            "    Load Form1",
            .. body.Select(line => "    " + line),
            "    Unload Form1",
            "End Sub",
            string.Empty]);
        return ModuleWorkspace.RunAsync([], program, Libraries, [("Form1", form1, FormDesigner.Read(form1)!)], SupportedLanguages.VB6);
    }

    [TestMethod]
    [DataRow("Debug.Print Form1.Text1", "Text1", DisplayName = "TextBox read: Text")]
    [DataRow("Form1.Text1 = \"abc\": Debug.Print Form1.Text1.Text", "abc", DisplayName = "TextBox assigned: Text")]
    [DataRow("Debug.Print Form1.Label1", "Label1", DisplayName = "Label read: Caption")]
    [DataRow("Form1.Check1 = 1: Debug.Print Form1.Check1.Value", "1", DisplayName = "CheckBox assigned: Value")]
    [DataRow("Dim v As Variant: v = Form1.Text1: Debug.Print TypeName(v) & \" \" & v", "String Text1", DisplayName = "TextBox let-coerced to a Variant")]
    [DataRow("Dim o As Object: Set o = Form1.Text1: o = \"x\": Debug.Print o", "x", DisplayName = "TextBox late-bound, assigned and read")]
    [DataRow("Dim o As Object: Set o = Form1.Label1: Debug.Print o", "Label1", DisplayName = "Label late-bound read")]
    [DataRow("Dim t As TextBox: Set t = Form1.Text1: t = \"abc\": Debug.Print t.Text", "abc", DisplayName = "TextBox variable assigned: Text")]
    public async Task TheDefaultMember_IsTheMemberItsClassNames(string code, string expected)
    {
        var printed = await RunAsync(code);

        CollectionAssert.AreEqual(new[] { expected }, printed, string.Join(" | ", printed));
    }
}
