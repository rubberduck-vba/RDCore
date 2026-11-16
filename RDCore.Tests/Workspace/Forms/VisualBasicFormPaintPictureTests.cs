using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 program's <c>PaintPicture</c> on its forms and PictureBoxes, hosted by RDForms, headless: a picture of the program's
/// (<c>LoadPicture</c>), or a rectangle of it, drawn on the surface, read back with <c>Point</c>, and the errors of its arguments.
/// </summary>
// the active form is the UI thread's, which every RDForms window shares: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicFormPaintPictureTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), $"rdcore-vb-paint-picture-{Guid.NewGuid():N}");

    // a 24-bit bitmap of 20 x 20 pixels in four quadrants: &H563412 at the top left, &H3264C8 at the top right, &HFFFF at the bottom left and &HFF8000
    // at the bottom right, as VB colours.
    private static byte[] Quadrants()
    {
        const int side = 20;
        var bytes = new byte[54 + side * side * 3];
        "BM"u8.CopyTo(bytes);
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(side).CopyTo(bytes, 18);
        BitConverter.GetBytes(side).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        BitConverter.GetBytes(side * side * 3).CopyTo(bytes, 34);
        BitConverter.GetBytes(3780).CopyTo(bytes, 38);
        BitConverter.GetBytes(3780).CopyTo(bytes, 42);
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                (byte R, byte G, byte B) rgb = (y < 10, x < 10) switch
                {
                    (true, true) => (18, 52, 86),
                    (true, false) => (200, 100, 50),
                    (false, true) => (255, 255, 0),
                    _ => (0, 128, 255),
                };
                var at = 54 + ((side - 1 - y) * side + x) * 3;
                (bytes[at], bytes[at + 1], bytes[at + 2]) = (rgb.B, rgb.G, rgb.R);
            }
        }

        return bytes;
    }

    [ClassInitialize]
    public static void WritePictures(TestContext _)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(Path.Combine(Folder, "quadrants.bmp"), Quadrants());
    }

    [ClassCleanup]
    public static void DeletePictures() => Directory.Delete(Folder, recursive: true);

    // Form7, in pixels and grey, with Picture1, a grey PictureBox of 100 x 100 pixels in pixels with no border.
    private const string Form7 = "VERSION 5.00\r\n"
        + "Begin VB.Form Form7 \r\n"
        + "   BackColor       =   &H00808080&\r\n"
        + "   Caption         =   \"Form7\"\r\n"
        + "   ClientHeight    =   3000\r\n"
        + "   ClientWidth     =   5000\r\n"
        + "   ScaleHeight     =   200\r\n"
        + "   ScaleMode       =   3  'Pixel\r\n"
        + "   ScaleWidth      =   333\r\n"
        + "   Begin VB.PictureBox Picture1 \r\n"
        + "      BackColor       =   &H00808080&\r\n"
        + "      BorderStyle     =   0  'None\r\n"
        + "      Height          =   1500\r\n"
        + "      Left            =   150\r\n"
        + "      ScaleHeight     =   100\r\n"
        + "      ScaleMode       =   3  'Pixel\r\n"
        + "      ScaleWidth      =   100\r\n"
        + "      TabIndex        =   0\r\n"
        + "      Top             =   150\r\n"
        + "      Width           =   1500\r\n"
        + "   End\r\n"
        + "End\r\n"
        + "Attribute VB_Name = \"Form7\"\r\n"
        + "Attribute VB_GlobalNameSpace = False\r\n"
        + "Attribute VB_Creatable = False\r\n"
        + "Attribute VB_PredeclaredId = True\r\n"
        + "Attribute VB_Exposed = False\r\n"
        + "Option Explicit\r\n"
        + "Public Sub PaintOwn(ByVal p As StdPicture)\r\n"
        + "    Me.PaintPicture p, 240, 130\r\n"
        + "End Sub\r\n";

    private static string Program(string[] body) => string.Join("\r\n",
    [
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        $"Private Const D As String = \"{Folder}{Path.DirectorySeparatorChar}\"",
        "Private Function At(ByVal o As Object, ByVal x As Single, ByVal y As Single) As String",
        "    At = Hex$(o.Point(x, y))",
        "End Function",
        "Public Sub Main()",
        "    Dim p As StdPicture, n As StdPicture, vStr As Variant, vLng As Variant",
        "    vStr = \"abc\": vLng = 5",
        "    Set p = LoadPicture(D & \"quadrants.bmp\")",
        "    Form7.Show",
        "    DoEvents",
        "    On Error Resume Next",
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
    [DataRow("Form7.Picture1.PaintPicture p, 30, 30", "Form7.Picture1", "32, 32|45, 32|32, 45|45, 45|50, 50",
        "563412 3264C8 FFFF FF8000 808080", DisplayName = "the picture at its size, from its corner")]
    [DataRow("Form7.Picture1.PaintPicture p, 30, 30, 40, 40", "Form7.Picture1", "35, 35|55, 55|69, 69|70, 70",
        "563412 FF8000 FF8000 808080", DisplayName = "stretched to width1 and height1")]
    [DataRow("Form7.Picture1.PaintPicture p, 0, 0, 10, 10, 5, 5, 10, 10", "Form7.Picture1", "0, 0|4, 4|5, 5|9, 9|10, 10",
        "563412 563412 FF8000 FF8000 808080", DisplayName = "a rectangle of the picture, with all nine arguments")]
    [DataRow("Form7.Picture1.PaintPicture p, 30, 30, , , 10, 10", "Form7.Picture1", "32, 32|38, 38|41, 41",
        "FF8000 FF8000 808080", DisplayName = "the source left out is the rest of the picture")]
    [DataRow("Form7.Picture1.PaintPicture p, 50, 30, -20, 20", "Form7.Picture1", "32, 32|47, 32|50, 32",
        "3264C8 563412 563412", DisplayName = "a negative width1 mirrors it")]
    [DataRow("Form7.Picture1.PaintPicture p, 30, 30, , , , , , , vbSrcInvert", "Form7.Picture1", "32, 32|45, 45",
        "D6B492 7F0080", DisplayName = "the opcode combines it with what is there")]
    [DataRow("Set Form7.Picture = p: Form7.Picture1.PaintPicture Form7.Picture, 0, 0, 10, 10, 5, 5, 10, 10", "Form7.Picture1", "0, 0|5, 5|10, 10",
        "563412 FF8000 808080", DisplayName = "a slice of the form's Picture")]
    [DataRow("Form7.PaintPicture p, 240, 130", "Form7", "242, 132|255, 145|260, 150", "563412 FF8000 808080", DisplayName = "on the form")]
    [DataRow("Form7.PaintOwn p", "Form7", "242, 132", "563412", DisplayName = "Me.PaintPicture in the form's own code")]
    [DataRow("With Form7.Picture1: .PaintPicture p, 30, 30: End With", "Form7.Picture1", "32, 32", "563412", DisplayName = "the With block's object")]
    public async Task PaintPicture_DrawsThePicture_AsVB6Does(string paint, string target, string points, string expected)
    {
        var reads = string.Join(" & \" \" & ", points.Split('|').Select(point => $"At({target}, {point})"));

        var printed = await RunAsync(paint, "Debug.Print Err.Number", $"Debug.Print {reads}");

        CollectionAssert.AreEqual(new[] { "0", expected }, printed);
    }

    // A late-bound call raises 13 for a value other than an object, as VB6 does.
    [TestMethod]
    [DataRow("vStr", "13", DisplayName = "late-bound, a Variant holding a String is 13")]
    [DataRow("vLng", "13", DisplayName = "late-bound, a Variant holding a number is 13")]
    [DataRow("n", "91", DisplayName = "late-bound, Nothing is 91")]
    [DataRow("p", "0", DisplayName = "late-bound, the picture is drawn")]
    public async Task PaintPicture_LateBound_RaisesTheErrorsOfItsPicture(string picture, string expected)
    {
        var printed = await RunAsync("Dim o As Object: Set o = Form7.Picture1", $"o.PaintPicture {picture}, 30, 30", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { expected }, printed);
    }

    [TestMethod]
    [DataRow("n, 30, 30", "91", DisplayName = "Nothing is 91")]
    [DataRow("vStr, 30, 30", "424", DisplayName = "a Variant holding a String is 424")]
    [DataRow("vLng, 30, 30", "424", DisplayName = "a Variant holding a number is 424")]
    [DataRow("New Collection, 30, 30", "13", DisplayName = "a Collection is 13")]
    [DataRow("LoadPicture(), 30, 30", "481", DisplayName = "an empty picture is 481")]
    [DataRow("p, 30, 30, 0, 20", "5", DisplayName = "a width of 0 is 5")]
    [DataRow("p, 30, 30, 20, 20, 20, 0, 5, 5", "5", DisplayName = "a source at the picture's right edge is 5")]
    [DataRow("p, 30, 30, \"x\", 20", "13", DisplayName = "a size of x is 13")]
    [DataRow("n, 30, 30, \"x\", 20", "13", DisplayName = "the numbers are read before the picture")]
    [DataRow("n, 30, 30, 0, 20", "91", DisplayName = "the picture is checked before the sizes")]
    [DataRow("p, Null, 30", "94", DisplayName = "Null is 94")]
    [DataRow("p, 1E10, 30", "6", DisplayName = "a pixel past a Long is 6")]
    [DataRow("p, 30, 30, , , , , , , 1E10", "6", DisplayName = "an opcode past a Long is 6")]
    public async Task PaintPicture_RaisesTheErrorsOfItsArguments(string arguments, string expected)
    {
        var printed = await RunAsync($"Form7.Picture1.PaintPicture {arguments}", "Debug.Print Err.Number & \" \" & At(Form7.Picture1, 32, 32)");

        CollectionAssert.AreEqual(new[] { $"{expected} 808080" }, printed);
    }
}
