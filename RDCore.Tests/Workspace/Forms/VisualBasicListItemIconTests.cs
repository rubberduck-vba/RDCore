using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Pictures;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// RD-VBA's <c>ItemIcon(Index)</c> of a Visual Basic 6.0 ListBox and ComboBox, hosted by RDForms, headless: a picture of the program's (<c>LoadPicture</c>)
/// given to an item is the program's own when it is read back, and follows its item through <c>AddItem</c> and <c>RemoveItem</c>; an item with none reads
/// <c>Nothing</c>.
/// </summary>
// the active form is the UI thread's, which every RDForms window shares: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicListItemIconTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), $"rdcore-vb-item-icons-{Guid.NewGuid():N}");

    [ClassInitialize]
    public static void WritePictures(TestContext _)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(Path.Combine(Folder, "pic.gif"), TestPictureFiles.Gif(7, 5));
    }

    [ClassCleanup]
    public static void DeletePictures() => Directory.Delete(Folder, recursive: true);

    // Form7, with List1, a ListBox, and Combo1, a ComboBox.
    private const string Form7 = "VERSION 5.00\r\n"
        + "Begin VB.Form Form7 \r\n"
        + "   Caption         =   \"Form7\"\r\n"
        + "   ClientHeight    =   3195\r\n"
        + "   ClientWidth     =   4680\r\n"
        + "   Begin VB.ListBox List1 \r\n"
        + "      Height          =   1035\r\n"
        + "      Left            =   120\r\n"
        + "      TabIndex        =   0\r\n"
        + "      Top             =   120\r\n"
        + "      Width           =   2055\r\n"
        + "   End\r\n"
        + "   Begin VB.ComboBox Combo1 \r\n"
        + "      Height          =   315\r\n"
        + "      Left            =   2400\r\n"
        + "      TabIndex        =   1\r\n"
        + "      Top             =   120\r\n"
        + "      Width           =   2055\r\n"
        + "   End\r\n"
        + "End\r\n"
        + "Attribute VB_Name = \"Form7\"\r\n"
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
        "    Dim p As StdPicture",
        "    On Error Resume Next",
        "    Form7.Show",
        "    DoEvents",
        "    Set p = LoadPicture(D & \"pic.gif\")",
        .. body.Select(line => "    " + line),
        "    Unload Form7",
        "End Sub",
        string.Empty,
    ]);

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static async Task<string[]> RunAsync(params string[] body)
    {
        var printed = await ModuleWorkspace.RunAsync([], Program(body), Libraries, [("Form7", Form7, FormDesigner.Read(Form7)!)], SupportedLanguages.VB6);
        return [.. printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal) && line != "Paint")];
    }

    [TestMethod]
    [DataRow("Form7.List1", DisplayName = "a ListBox")]
    [DataRow("Form7.Combo1", DisplayName = "a ComboBox")]
    public async Task AnItemsIcon_IsTheProgramsOwnPicture_AndAnItemWithNoneIsNothing(string list)
    {
        var printed = await RunAsync(
            $"{list}.AddItem \"a\": {list}.AddItem \"b\"",
            $"Set {list}.ItemIcon(1) = p",
            $"Debug.Print Err.Number & \" \" & ({list}.ItemIcon(1) Is p) & \" \" & ({list}.ItemIcon(0) Is Nothing)");

        CollectionAssert.AreEqual(new[] { "0 True True" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Form7.List1", DisplayName = "a ListBox")]
    [DataRow("Form7.Combo1", DisplayName = "a ComboBox")]
    public async Task AnItemsIcon_FollowsItsItem_ThroughAddItemAtAnIndex_RemoveItem_AndClear(string list)
    {
        var printed = await RunAsync(
            $"{list}.AddItem \"a\": {list}.AddItem \"b\"",
            $"Set {list}.ItemIcon(1) = p",
            $"{list}.AddItem \"x\", 0",
            $"Debug.Print ({list}.ItemIcon(2) Is p) & \" \" & ({list}.ItemIcon(1) Is Nothing)",
            $"{list}.RemoveItem 0",
            $"Debug.Print ({list}.ItemIcon(1) Is p) & \" \" & ({list}.ItemIcon(0) Is Nothing)",
            $"{list}.Clear: {list}.AddItem \"y\"",
            $"Debug.Print Err.Number & \" \" & ({list}.ItemIcon(0) Is Nothing)");

        CollectionAssert.AreEqual(new[] { "True True", "True True", "0 True" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task Nothing_TakesAnItemsIconAway()
    {
        var printed = await RunAsync(
            "Form7.List1.AddItem \"a\"",
            "Set Form7.List1.ItemIcon(0) = p",
            "Set Form7.List1.ItemIcon(0) = Nothing",
            "Debug.Print Err.Number & \" \" & (Form7.List1.ItemIcon(0) Is Nothing)");

        CollectionAssert.AreEqual(new[] { "0 True" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AnIndexOutsideTheItems_Is381_AndAnObjectThatIsNoPicture_IsATypeMismatch()
    {
        var printed = await RunAsync(
            "Dim q As StdPicture",
            "Form7.List1.AddItem \"a\"",
            "Set q = Form7.List1.ItemIcon(1): Debug.Print Err.Number: Err.Clear",
            "Set Form7.List1.ItemIcon(1) = p: Debug.Print Err.Number: Err.Clear",
            "Set Form7.List1.ItemIcon(0) = New Collection: Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "381", "381", "13" }, printed, string.Join(" | ", printed));
    }
}
