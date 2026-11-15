using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli;
using System.IO.Abstractions;
using static RDCore.Tests.Runtime.Pictures.TestPictureFiles;

namespace RDCore.Tests.Runtime.Pictures;

/// <summary>
/// stdole's <c>LoadPicture</c> and <c>SavePicture</c>, and the <c>StdPicture</c> objects they make, as MS-VBA has them (observed in Excel, 64-bit): what each
/// picture file loads as, the errors of a file that is missing or no picture, the image of an icon a size picks, and what is saved.
/// </summary>
/// <remarks>
/// A program reads and writes the machine's files: the pictures are written to a folder of their own, which the program names as <c>D</c>.
/// </remarks>
[TestClass]
public sealed class LoadPictureTests
{
    private static readonly Dictionary<string, byte[]> Pictures = new()
    {
        ["bmp24.bmp"] = Bitmap(10, 20),
        ["bmp300dpi.bmp"] = Bitmap(10, 20, pixelsPerMeter: 11811),
        ["pic.gif"] = Gif(7, 5),
        ["pic.jpg"] = Jpeg(9, 11),
        ["pic.png"] = Png(6, 4),
        ["three.ico"] = Icon(1, 16, 32, 48),
        ["one32.ico"] = Icon(1, 32),
        ["arrow.cur"] = Icon(2, 32),
        ["placeable.wmf"] = Metafile(placeable: true),
        ["plain.wmf"] = Metafile(placeable: false),
        ["frame.emf"] = EnhancedMetafile(4964, 2964),
        ["text.txt"] = "not a picture"u8.ToArray(),
        ["empty.bmp"] = [],
        ["cut.bmp"] = Bitmap(10, 20)[..30],
        ["reversed.ico"] = Icon(1, 48, 32, 16),
        ["bmp1.bmp"] = Bitmap(13, 3, bits: 1, pixelsPerMeter: 2835),
        ["bmp4.bmp"] = Bitmap(5, 3, bits: 4, pixelsPerMeter: 2835),
        ["bmp8.bmp"] = Bitmap(5, 3, bits: 8, pixelsPerMeter: 2835),
        ["bmp32.bmp"] = Bitmap(4, 3, bits: 32, pixelsPerMeter: 2835),
        ["topdown.bmp"] = Bitmap(5, 3, pixelsPerMeter: 2835, topDown: true),
        ["core.bmp"] = Bitmap(5, 3, core: true),
    };

    private static readonly string Folder = Path.Combine(Path.GetTempPath(), $"rdcore-pictures-{Guid.NewGuid():N}");

    [ClassInitialize]
    public static void WritePictures(TestContext _)
    {
        Directory.CreateDirectory(Path.Combine(Folder, "folder.bmp"));
        foreach (var (name, bytes) in Pictures)
        {
            File.WriteAllBytes(Path.Combine(Folder, name), bytes);
        }
    }

    [ClassCleanup]
    public static void DeletePictures() => Directory.Delete(Folder, recursive: true);

    private static ModuleWorkspace.WorkspaceLibraries Stdole => new(
        ["stdole"], new DirectoryLibrarySource(new FileSystem(), RDCore.Tests.Runtime.Libraries.ShippedLibrariesTests.Symbols));

    // Main, then what it needs: R prints a picture's Type, Width, Height and whether its Handle is set, or the error its loading raised; Head, a file's length
    // and its first 16 bytes.
    private static Task<string[]> RunAsync(params string[] lines) => ModuleWorkspace.RunAsync([], Source(lines), Stdole);

    // a Visual Basic 6.0 project, which references the Visual Basic objects and procedures (VB.Global's LoadPicture), and stdole, without listing them.
    private static async Task<string[]> RunVB6Async(params string[] lines)
    {
        ExecuteResult? ran = null;
        await ModuleWorkspace.InspectAsync([], Source(lines), (_, result, _) =>
        {
            ran = result;
            return Task.CompletedTask;
        }, libraries: new([], Stdole.Source), language: SupportedLanguages.VB6);

        Assert.AreEqual(ExecutionOutcome.Completed, ran!.Outcome, $"{ran.ErrorMessage} {string.Join("; ", ran.Diagnostics)}");
        return [.. ran.Output.Select(line => line.Trim())];
    }

    private static string Source(string[] lines) => string.Join("\r\n",
    [
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        $"Private Const D As String = \"{Folder}{Path.DirectorySeparatorChar}\"",
        "Public Sub Main()",
        "    Dim p As StdPicture, p2 As StdPicture",
        "    On Error Resume Next",
        .. lines.Select(line => "    " + line),
        "End Sub",
        "Private Sub R(ByVal p As StdPicture)",
        "    If Err.Number <> 0 Then Debug.Print \"err \" & Err.Number: Err.Clear: Exit Sub",
        "    Debug.Print p.Type & \" \" & p.Width & \" \" & p.Height & \" \" & (p.Handle <> 0)",
        "End Sub",
        "Private Function Head(ByVal path As String) As String",
        "    Dim n As Long, f As Integer, b As Byte, i As Long",
        "    n = FileLen(path)",
        "    Head = \"len \" & n",
        "    If n = 0 Then Exit Function",
        "    Head = Head & \" \"",
        "    f = FreeFile: Open path For Binary Access Read As #f",
        "    For i = 1 To IIf(n < 16, n, 16): Get #f, i, b: Head = Head & Right$(\"0\" & Hex$(b), 2): Next",
        "    Close #f",
        "End Function",
        "Private Function ByteAt(ByVal path As String, ByVal at As Long) As Byte",
        "    Dim f As Integer, b As Byte: f = FreeFile: Open path For Binary Access Read As #f: Get #f, at + 1, b: Close #f",
        "    ByteAt = b",
        "End Function",
        "",
    ]);

    // what the program printed, all of it in the message when it is not what was expected.
    private static void Printed(string[] expected, string[] printed)
        => Assert.IsTrue(expected.SequenceEqual(printed), $"printed [{string.Join(" | ", printed)}], expected [{string.Join(" | ", expected)}]");

    private static string Load(string arguments) => $"Set p = Nothing: Err.Clear: Set p = LoadPicture({arguments}): R p";

    [TestMethod]
    [DataRow("D & \"bmp24.bmp\"", "1 265 529 True", DisplayName = "a bitmap, 10 by 20")]
    [DataRow("D & \"bmp300dpi.bmp\"", "1 265 529 True", DisplayName = "a bitmap of 300 dpi, whose resolution changes nothing")]
    [DataRow("D & \"pic.gif\"", "1 185 132 True", DisplayName = "a GIF, 7 by 5, which is a bitmap")]
    [DataRow("D & \"pic.jpg\"", "1 238 291 True", DisplayName = "a JPEG, 9 by 11, which is a bitmap")]
    [DataRow("D & \"three.ico\"", "3 847 847 True", DisplayName = "an icon of 16, 32 and 48, its 32")]
    [DataRow("D & \"one32.ico\"", "3 847 847 True", DisplayName = "an icon of 32")]
    [DataRow("D & \"arrow.cur\"", "3 847 847 True", DisplayName = "a cursor, which is an icon")]
    [DataRow("D & \"placeable.wmf\"", "2 2540 1270 True", DisplayName = "a placeable metafile, an inch by half an inch")]
    [DataRow("D & \"frame.emf\"", "4 4964 2964 True", DisplayName = "an enhanced metafile, its frame")]
    public async Task APictureFile_LoadsAsItsType_InHimetric(string arguments, string expected)
        => Printed(new[] { expected }, await RunAsync(Load(arguments)));

    [TestMethod]
    [DataRow("", DisplayName = "no file name")]
    [DataRow("\"\"", DisplayName = "an empty one")]
    [DataRow("Empty", DisplayName = "Empty")]
    public async Task NoFileName_IsAnEmptyPicture(string arguments)
        => Printed(new[] { "0 0 0 False" }, await RunAsync(Load(arguments)));

    [TestMethod]
    [DataRow("D & \"missing.bmp\"", "err 53", DisplayName = "a missing file: 53")]
    [DataRow("5", "err 53", DisplayName = "a number, the name of a missing file: 53")]
    [DataRow("Null", "err 13", DisplayName = "Null: 13")]
    [DataRow("D & \"text.txt\"", "err 481", DisplayName = "a text file: 481")]
    [DataRow("D & \"empty.bmp\"", "err 481", DisplayName = "an empty file: 481")]
    [DataRow("D & \"pic.png\"", "err 481", DisplayName = "a PNG: 481")]
    [DataRow("D & \"cut.bmp\"", "err -2147418113", DisplayName = "a bitmap cut short: E_UNEXPECTED")]
    [DataRow("D & \"plain.wmf\"", "err -2147467259", DisplayName = "a metafile that is not placeable: E_FAIL")]
    [DataRow("D & \"folder.bmp\"", "err 75", DisplayName = "a folder: 75")]
    [DataRow("D", "err 76", DisplayName = "a path ending in a separator: 76")]
    [DataRow("D & \"*.bmp\"", "err 75", DisplayName = "a pattern: 75")]
    public async Task AFileThatIsNoPicture_RaisesItsError(string arguments, string expected)
        => Printed(new[] { expected }, await RunAsync(Load(arguments)));

    [TestMethod]
    [DataRow("D & \"three.ico\", 16, 16", "423", DisplayName = "16 by 16: its 16")]
    [DataRow("D & \"three.ico\", 48, 48", "1270", DisplayName = "48 by 48: its 48")]
    [DataRow("D & \"three.ico\", 20, 20", "423", DisplayName = "20 by 20: its first")]
    [DataRow("D & \"three.ico\", 44, 44", "423", DisplayName = "44 by 44: its first, not its 48")]
    [DataRow("D & \"three.ico\", 100, 100", "423", DisplayName = "100 by 100: its first")]
    [DataRow("D & \"three.ico\", 16, 48", "423", DisplayName = "16 by 48: its first")]
    [DataRow("D & \"three.ico\", 48, 16", "423", DisplayName = "48 by 16: its first")]
    [DataRow("D & \"three.ico\", 32", "423", DisplayName = "a width of 32 alone: its first")]
    [DataRow("D & \"three.ico\", , 48", "423", DisplayName = "a height of 48 alone: its first")]
    [DataRow("D & \"three.ico\", 0, 0", "847", DisplayName = "0 by 0: the system's icon size")]
    [DataRow("D & \"three.ico\", -1, -1", "423", DisplayName = "-1 by -1: its first")]
    [DataRow("D & \"reversed.ico\"", "847", DisplayName = "an icon of 48, 32 and 16: its 32")]
    [DataRow("D & \"reversed.ico\", 16, 16", "423", DisplayName = "an icon of 48, 32 and 16, at 16 by 16: its 16")]
    [DataRow("D & \"reversed.ico\", 0, 48", "1270", DisplayName = "an icon of 48, 32 and 16, at 0 by 48: its first")]
    [DataRow("D & \"reversed.ico\", 32, 0", "1270", DisplayName = "an icon of 48, 32 and 16, at 32 by 0: its first")]
    [DataRow("D & \"reversed.ico\", 40, 24", "1270", DisplayName = "an icon of 48, 32 and 16, at 40 by 24: its first")]
    [DataRow("D & \"three.ico\", , , 1", "847", DisplayName = "flags: monochrome, which changes nothing")]
    [DataRow("D & \"three.ico\", , , 4", "847", DisplayName = "flags: colour")]
    [DataRow("D & \"three.ico\", 16, 16, 8", "423", DisplayName = "flags no constant names")]
    [DataRow("D & \"three.ico\", \"16\", \"16\"", "423", DisplayName = "sizes as text")]
    [DataRow("D & \"one32.ico\", 16, 16", "847", DisplayName = "an icon of 32 alone: its 32")]
    [DataRow("D & \"arrow.cur\", 16, 16", "847", DisplayName = "a cursor of 32 alone: its 32")]
    [DataRow("D & \"bmp24.bmp\", 5, 5", "265", DisplayName = "a bitmap, which no size changes")]
    public async Task AnIconsImage_IsTheOneOfTheSizeAskedFor_OrItsFirst(string arguments, string width)
        => Printed(new[] { width }, await RunAsync($"Set p = LoadPicture({arguments}): Debug.Print p.Width"));

    [TestMethod]
    public async Task APicturesHandle_IsItsDefaultMember_AndTellsOnePictureFromAnother()
        => Printed(new[] { "True", "True True", "False False" }, await RunAsync(
            "Set p = LoadPicture(D & \"bmp24.bmp\"): Debug.Print (p & \"\") = CStr(p.Handle)",
            "Set p2 = p: Debug.Print (p2 Is p) & \" \" & (p2.Handle = p.Handle)",
            "Set p2 = LoadPicture(D & \"bmp24.bmp\"): Debug.Print (p2 Is p) & \" \" & (p2.Handle = p.Handle)"));

    [TestMethod]
    public async Task APicture_IsAStdPictureAndAPicture_AndNoIPicture()
        => Printed(new[] { "True True False" }, await RunAsync(
            "Set p = LoadPicture(D & \"bmp24.bmp\"): Debug.Print (TypeOf p Is StdPicture) & \" \" & (TypeOf p Is Picture) & \" \" & (TypeOf p Is IPicture)"));

    [TestMethod]
    public async Task LoadPicture_IsReachedQualified()
        => Printed(new[] { "1 265 529 True", "1 265 529 True" }, await RunAsync(
            "Set p = stdole.LoadPicture(D & \"bmp24.bmp\"): R p", "Set p = stdole.StdFunctions.LoadPicture(D & \"bmp24.bmp\"): R p"));

    [TestMethod]
    public async Task ANewStdPicture_HoldsNoPicture()
        => Printed(new[] { "0 0 0 False" }, await RunAsync("Set p = New StdPicture: R p"));

    [TestMethod]
    public async Task ABitmapsPalette_IsAssignable()
        => Printed(new[] { "0", "5" }, await RunAsync(
            "Set p = LoadPicture(D & \"bmp24.bmp\"): Debug.Print p.hPal", "p.hPal = 5: Debug.Print p.hPal"));

    [TestMethod]
    [DataRow("LoadPicture(D & \"three.ico\")", DisplayName = "an icon")]
    [DataRow("LoadPicture()", DisplayName = "an empty picture")]
    [DataRow("New StdPicture", DisplayName = "a new StdPicture")]
    [DataRow("LoadPicture(D & \"placeable.wmf\")", DisplayName = "a metafile")]
    public async Task APictureThatIsNoBitmap_FailsToReadOrAssignItsPalette(string picture)
        => Printed(new[] { "-2147467259 -1", "-2147467259" }, await RunAsync(
            "Dim x As Long",
            $"Set p = {picture}: Err.Clear: x = -1: x = p.hPal: Debug.Print Err.Number & \" \" & x",
            "Err.Clear: p.hPal = 5: Debug.Print Err.Number"));

    [TestMethod]
    public async Task APalettesFailure_IsTheMethodsOfThePicture()
        => Printed(new[] { "Method 'hPal' of object 'Picture' failed" }, await RunAsync(
            "Dim x As Long", "Set p = LoadPicture(D & \"three.ico\"): x = p.hPal: Debug.Print Err.Description"));

    [TestMethod]
    public async Task ANewStdPicture_HasNoHandleTypeOrSize()
        => Printed(new[] { "0 0 0 0" }, await RunAsync("Set p = New StdPicture: Debug.Print p.Handle & \" \" & p.Type & \" \" & p.Width & \" \" & p.Height"));

    [TestMethod]
    [DataRow("bmp1.bmp", "1 344 79", DisplayName = "13 by 3, of 1 bit per pixel")]
    [DataRow("bmp32.bmp", "1 106 79", DisplayName = "4 by 3, of 32 bits")]
    [DataRow("topdown.bmp", "1 132 79", DisplayName = "5 by 3, top-down")]
    [DataRow("core.bmp", "1 132 79", DisplayName = "5 by 3, under a core header")]
    public async Task ABitmapOfAnyLayout_LoadsAtItsSize(string file, string expected)
        => Printed(new[] { expected + " True" }, await RunAsync(Load($"D & \"{file}\"")));

    // the bitmap Excel saves from each, byte for byte: under a BITMAPINFOHEADER that states no resolution and the size of the pixels, the rows from the bottom
    // up, the bits past a row's last pixel and its padding cleared; 32 bits per pixel are saved as 24.
    [TestMethod]
    [DataRow("bmp1.bmp", "424D4A000000000000003E000000280000000D0000000300000001000100000000000C000000000000000000000000000000000000000000FF00255BFE00000000000D1000001A200000", DisplayName = "1 bit per pixel")]
    [DataRow("bmp4.bmp", "424D82000000000000007600000028000000050000000300000001000400000000000C000000000000000000000000000000000000000000FF00255BFE004AB6FD006F11FC00946CFB00B9C7FA00DE22F900037DF80028D8F7004D33F600728EF50097E9F400BC44F300E19FF20006FAF1002B55F000000700000D1410001A212000", DisplayName = "4 bits per pixel")]
    [DataRow("bmp32.bmp", "424D5A00000000000000360000002800000004000000030000000100180000000000240000000000000000000000000000000000000000070E1C232A383F46545B620D141B293037454C5361686F1A2128363D445259606E757C", DisplayName = "32 bits per pixel, saved as 24")]
    [DataRow("topdown.bmp", "424D660000000000000036000000280000000500000003000000010018000000000030000000000000000000000000000000000000001A21282F363D444B525960676E757C000D141B222930373E454C535A61686F0000070E151C232A31383F464D545B6200", DisplayName = "top-down, saved bottom-up")]
    [DataRow("core.bmp", "424D6600000000000000360000002800000005000000030000000100180000000000300000000000000000000000000000000000000000070E151C232A31383F464D545B62000D141B222930373E454C535A61686F001A21282F363D444B525960676E757C00", DisplayName = "a core header, saved under an info header")]
    public async Task ASavedBitmap_IsTheBitmapMSVBAWrites(string file, string expected)
    {
        var saved = Path.Combine(Folder, $"saved-{file}");
        Printed(new[] { "0" }, await RunAsync($"SavePicture LoadPicture(D & \"{file}\"), D & \"saved-{file}\": Debug.Print Err.Number"));
        Assert.AreEqual(expected, Convert.ToHexString(File.ReadAllBytes(saved)));
    }

    [TestMethod]
    public async Task ASaved8BitBitmap_KeepsItsColourTable_AndClearsItsPadding()
    {
        var saved = Path.Combine(Folder, "saved-bmp8.bmp");
        Printed(new[] { "0" }, await RunAsync("SavePicture LoadPicture(D & \"bmp8.bmp\"), D & \"saved-bmp8.bmp\": Debug.Print Err.Number"));

        var bytes = File.ReadAllBytes(saved);
        Assert.AreEqual(1102, bytes.Length);
        Assert.AreEqual("424D4E040000000000003604000028000000050000000300000001000800000000001800000000000000000000000000000000000000",
            Convert.ToHexString(bytes[..54]));
        CollectionAssert.AreEqual(Pictures["bmp8.bmp"][54..1078], bytes[54..1078]);
        Assert.AreEqual("00070E151C0000000D141B22290000001A21282F36000000", Convert.ToHexString(bytes[1078..]));
    }

    [TestMethod]
    public async Task ASavedBitmap_IsItsPixels_UnderAHeaderOfNoResolution_WithItsRowsPaddedWithZeros()
    {
        var printed = await RunAsync("SavePicture LoadPicture(D & \"bmp300dpi.bmp\"), D & \"saved.bmp\": Debug.Print Err.Number & \" \" & Head(D & \"saved.bmp\")",
            "Dim at As Variant, s As String: For Each at In Array(38, 42, 54, 84, 85, 86): s = s & \" \" & ByteAt(D & \"saved.bmp\", at): Next",
            "Debug.Print Mid$(s, 2)");

        // 694 bytes: a 54-byte header, and 20 rows of 30 bytes padded to 32. The pixels are as they were: 0, then the padding (0, 0), then the next row's first.
        Printed(new[] { "0 len 694 424DB602000000000000360000002800", "0 0 0 0 0 13" }, printed);
    }

    [TestMethod]
    [DataRow("placeable.wmf", DisplayName = "a placeable metafile")]
    [DataRow("frame.emf", DisplayName = "an enhanced metafile")]
    public async Task ASavedMetafile_IsTheFileItWasLoadedFrom(string file)
    {
        var printed = await RunAsync($"SavePicture LoadPicture(D & \"{file}\"), D & \"saved-{file}\": Debug.Print Err.Number & \" \" & Head(D & \"saved-{file}\")");

        var bytes = Pictures[file];
        Printed(new[] { $"0 len {bytes.Length} {Convert.ToHexString(bytes[..16])}" }, printed);
    }

    [TestMethod]
    public async Task ANewStdPicture_IsSavedAsAFileOfNoBytes()
        => Printed(new[] { "0 len 0" }, await RunAsync(
            "SavePicture New StdPicture, D & \"saved-new.out\": Debug.Print Err.Number & \" \" & Head(D & \"saved-new.out\")"));

    [TestMethod]
    [DataRow("LoadPicture(), D & \"saved.out\"", "380", DisplayName = "an empty picture: 380")]
    [DataRow("Nothing, D & \"saved.out\"", "380", DisplayName = "Nothing: 380")]
    [DataRow("LoadPicture(D & \"bmp24.bmp\"), \"\"", "53", DisplayName = "no file name: 53")]
    [DataRow("LoadPicture(D & \"bmp24.bmp\"), D & \"nofolder\\saved.out\"", "76", DisplayName = "a missing folder: 76")]
    [DataRow("LoadPicture(D & \"bmp24.bmp\"), D & \"*.out\"", "75", DisplayName = "a pattern: 75")]
    [DataRow("LoadPicture(D & \"bmp24.bmp\"), D & \"folder.bmp\"", "75", DisplayName = "a folder: 75")]
    [DataRow("LoadPicture(D & \"bmp24.bmp\"), D", "76", DisplayName = "a path ending in a separator: 76")]
    public async Task SavePicture_RaisesItsErrors(string arguments, string expected)
        => Printed(new[] { expected }, await RunAsync($"SavePicture {arguments}: Debug.Print Err.Number"));

    [TestMethod]
    public async Task SavePicture_WritesAFileTheProgramHasOpen_AndOverwritesOne()
        => Printed(new[] { "0", "0", "0 len 694 424DB602000000000000360000002800" }, await RunAsync(
            "Dim f As Integer: Set p = LoadPicture(D & \"bmp24.bmp\")",
            "f = FreeFile: Open D & \"saved-open.out\" For Binary As #f: SavePicture p, D & \"saved-open.out\": Debug.Print Err.Number: Close #f",
            "Err.Clear: SavePicture p, D & \"saved-open.out\": Debug.Print Err.Number",
            "Debug.Print Err.Number & \" \" & Head(D & \"saved-open.out\")"));

    // Visual Basic 6.0's LoadPicture is stdole's with its own arguments: Size picks the system's small icon or its large one, or the X by Y of vbLPCustom.
    [TestMethod]
    public async Task VisualBasic6sLoadPicture_IsStdolesWithASize()
        => Printed(["847", "423", "847", "1270", "423", "1 265 529 True", "0 len 694 424DB602000000000000360000002800"], await RunVB6Async(
            "Set p = LoadPicture(D & \"three.ico\"): Debug.Print p.Width",
            "Set p = LoadPicture(D & \"three.ico\", vbLPSmall): Debug.Print p.Width",
            "Set p = LoadPicture(D & \"three.ico\", vbLPLarge): Debug.Print p.Width",
            "Set p = LoadPicture(D & \"three.ico\", vbLPCustom, , 48, 48): Debug.Print p.Width",
            "Set p = LoadPicture(D & \"three.ico\", vbLPSmallShell): Debug.Print p.Width",
            "Set p = VB.Global.LoadPicture(D & \"bmp24.bmp\"): R p",
            "SavePicture p, D & \"saved-vb6.bmp\": Debug.Print Err.Number & \" \" & Head(D & \"saved-vb6.bmp\")"));

    [TestMethod]
    [Ignore("TypeName of a StdPicture is \"Picture\" in MS-VBA; the library states no type name for its objects")]
    public async Task APicturesTypeName_IsPicture()
        => Printed(new[] { "Picture" }, await RunAsync("Set p = LoadPicture(D & \"bmp24.bmp\"): Debug.Print TypeName(p)"));

    [TestMethod]
    [Ignore("SavePicture of a GIF, a JPEG, an icon or a cursor needs the picture decoded: not implemented")]
    [DataRow("pic.gif", "len 1118 424D5E04000000000000360400002800", DisplayName = "a GIF: an 8-bit bitmap")]
    [DataRow("pic.jpg", "len 362 424D6A01000000000000360000002800", DisplayName = "a JPEG: a 24-bit bitmap")]
    [DataRow("three.ico", "len 766 0000010001002020100000000000E802", DisplayName = "an icon: one 32 by 32 image of 16 colours")]
    [DataRow("arrow.cur", "len 766 0000010001002020100000000000E802", DisplayName = "a cursor: an icon")]
    public async Task ASavedPictureOfAnotherFormat_IsWrittenAsMSVBAWritesIt(string file, string expected)
        => Printed(new[] { "0 " + expected }, await RunAsync(
            $"SavePicture LoadPicture(D & \"{file}\"), D & \"saved-{file}\": Debug.Print Err.Number & \" \" & Head(D & \"saved-{file}\")"));
}
