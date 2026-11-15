using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Pictures;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A picture of a Visual Basic 6.0 program's (<c>LoadPicture</c>) given to a form, a PictureBox and an Image, hosted by RDForms, headless: the control takes
/// the picture, gives the program its own picture back, and lets go of it for <c>Nothing</c>.
/// </summary>
// the active form is the UI thread's, which every RDForms window shares: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicFormPictureTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), $"rdcore-vb-form-pictures-{Guid.NewGuid():N}");

    [ClassInitialize]
    public static void WritePictures(TestContext _)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(Path.Combine(Folder, "pic.gif"), TestPictureFiles.Gif(7, 5));
        File.WriteAllBytes(Path.Combine(Folder, "other.gif"), TestPictureFiles.Gif(3, 4));
    }

    [ClassCleanup]
    public static void DeletePictures() => Directory.Delete(Folder, recursive: true);

    // Form6, with Picture1, a PictureBox, and Image1, an Image.
    private const string Form6 = "VERSION 5.00\r\n"
        + "Begin VB.Form Form6 \r\n"
        + "   Caption         =   \"Form6\"\r\n"
        + "   ClientHeight    =   3195\r\n"
        + "   ClientWidth     =   4680\r\n"
        + "   Begin VB.PictureBox Picture1 \r\n"
        + "      Height          =   1500\r\n"
        + "      Left            =   120\r\n"
        + "      TabIndex        =   0\r\n"
        + "      Top             =   120\r\n"
        + "      Width           =   1500\r\n"
        + "   End\r\n"
        + "   Begin VB.Image Image1 \r\n"
        + "      Height          =   1500\r\n"
        + "      Left            =   2400\r\n"
        + "      Top             =   120\r\n"
        + "      Width           =   1500\r\n"
        + "   End\r\n"
        + "End\r\n"
        + "Attribute VB_Name = \"Form6\"\r\n"
        + "Attribute VB_GlobalNameSpace = False\r\n"
        + "Attribute VB_Creatable = False\r\n"
        + "Attribute VB_PredeclaredId = True\r\n"
        + "Attribute VB_Exposed = False\r\n"
        + "Option Explicit\r\n";

    private static string Program(string[] body) => string.Join("\r\n",
    [
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        $"Private Const D As String = \"{Folder}{Path.DirectorySeparatorChar}\"",
        "Public Sub Main()",
        "    Dim p As StdPicture, q As StdPicture",
        "    On Error Resume Next",
        "    Form6.Show",
        "    DoEvents",
        .. body.Select(line => "    " + line),
        "    Unload Form6",
        "End Sub",
        string.Empty,
    ]);

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static async Task<string[]> RunAsync(params string[] body)
    {
        var printed = await ModuleWorkspace.RunAsync([], Program(body), Libraries, [("Form6", Form6, FormDesigner.Read(Form6)!)], SupportedLanguages.VB6);
        return [.. printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal) && line != "Paint")];
    }

    [TestMethod]
    [DataRow("Form6.Picture1", DisplayName = "a PictureBox")]
    [DataRow("Form6.Image1", DisplayName = "an Image")]
    [DataRow("Form6", DisplayName = "the form")]
    public async Task APictureSetToAControl_IsTheProgramsOwnPictureWhenItIsReadBack(string target)
    {
        var printed = await RunAsync(
            "Set p = LoadPicture(D & \"pic.gif\")",
            $"Set {target}.Picture = p",
            $"Debug.Print Err.Number & \" \" & ({target}.Picture Is p)");

        CollectionAssert.AreEqual(new[] { "0 True" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task APictureLetToAControl_IsTakenAsASetIs()
    {
        var printed = await RunAsync(
            "Set p = LoadPicture(D & \"pic.gif\")",
            "Form6.Picture1.Picture = p",
            "Debug.Print Err.Number & \" \" & (Form6.Picture1.Picture Is p)");

        CollectionAssert.AreEqual(new[] { "0 True" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AnotherPicture_ReplacesTheFirst()
    {
        var printed = await RunAsync(
            "Set p = LoadPicture(D & \"pic.gif\")",
            "Set q = LoadPicture(D & \"other.gif\")",
            "Set Form6.Picture1.Picture = p",
            "Set Form6.Picture1.Picture = q",
            "Debug.Print Err.Number & \" \" & (Form6.Picture1.Picture Is q) & \" \" & (Form6.Picture1.Picture Is p)");

        CollectionAssert.AreEqual(new[] { "0 True False" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Form6.Picture1", DisplayName = "a PictureBox")]
    [DataRow("Form6.Image1", DisplayName = "an Image")]
    [DataRow("Form6", DisplayName = "the form")]
    public async Task Nothing_TakesThePictureAway(string target)
    {
        var printed = await RunAsync(
            "Set p = LoadPicture(D & \"pic.gif\")",
            $"Set {target}.Picture = p",
            $"Set {target}.Picture = Nothing",
            $"Debug.Print Err.Number & \" \" & ({target}.Picture Is p)");

        CollectionAssert.AreEqual(new[] { "0 False" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AnObjectThatIsNoPicture_IsATypeMismatch()
    {
        var printed = await RunAsync("Set Form6.Picture1.Picture = New Collection", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "13" }, printed, string.Join(" | ", printed));
    }
}
