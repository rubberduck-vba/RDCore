using RDCore.Tests.Cli;
using RDCore.Tests.External;
using System.Runtime.Versioning;
using static RDCore.Tests.Runtime.Pictures.TestPictureFiles;
using static RDCore.Tests.Workspace.Forms.FormHostingTests;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A picture of the program's (<c>LoadPicture</c>) given to a form and to its controls, hosted by Microsoft Forms: the control draws it, and gives back the
/// program's own picture, as in Excel (64-bit). Inconclusive on a machine without Microsoft Forms.
/// </summary>
[TestClass]
[TestCategory("MSForms")]
[DoNotParallelize]
[SupportedOSPlatform("windows")]
public sealed class FormPictureTests
{
    private static readonly Dictionary<string, byte[]> Pictures = new()
    {
        ["bmp24.bmp"] = Bitmap(10, 20),
        ["pic.gif"] = Gif(7, 5),
        ["three.ico"] = Icon(1, 16, 32, 48),
        ["arrow.cur"] = Icon(2, 32),
        ["placeable.wmf"] = Metafile(placeable: true),
        ["frame.emf"] = EnhancedMetafile(4964, 2964),
    };

    private static readonly string Folder = Path.Combine(Path.GetTempPath(), $"rdcore-form-pictures-{Guid.NewGuid():N}");

    [ClassInitialize]
    public static void WritePictures(TestContext _)
    {
        Directory.CreateDirectory(Folder);
        foreach (var (name, bytes) in Pictures)
        {
            File.WriteAllBytes(Path.Combine(Folder, name), bytes);
        }
    }

    [ClassCleanup]
    public static void DeletePictures() => Directory.Delete(Folder, recursive: true);

    // Main, then Desc: whether a picture's Handle is set, its Type and its size, or Nothing.
    private static string Program(string[] body) => string.Join("\r\n",
    [
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        $"Private Const D As String = \"{Folder}{Path.DirectorySeparatorChar}\"",
        "Public Sub Main()",
        "    Dim p As StdPicture, p2 As StdPicture, img As MSForms.Image, o As Object",
        "    On Error Resume Next",
        "    Set img = ExampleView.Controls.Add(\"Forms.Image.1\", \"Image1\")",
        .. body.Select(line => "    " + line),
        "    Unload ExampleView",
        "End Sub",
        "Private Function Desc(ByVal o As Object) As String",
        "    On Error Resume Next",
        "    If o Is Nothing Then Desc = \"Nothing\": Exit Function",
        "    Desc = IIf(o.Handle = 0, \"h 0\", \"h set\") & \" type \" & o.Type & \" \" & o.Width & \"x\" & o.Height",
        "End Function",
        "",
    ]);

    private static async Task<string[]> RunAsync(bool external, params string[] body)
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;
        using var host = ExternalHosts.New();
        return await ModuleWorkspace.RunAsync([], Program(body),
            new ModuleWorkspace.WorkspaceLibraries(["MSForms", "stdole"], Shipped, Outside: external ? ExternalHosts.WorldOf(host) : world), [ExampleView()]);
    }

    private static void Printed(string[] expected, string[] printed)
        => Assert.IsTrue(expected.SequenceEqual(printed), $"printed [{string.Join(" | ", printed)}], expected [{string.Join(" | ", expected)}]");

    [TestMethod]
    [DataRow(false, DisplayName = "in the process")]
    [DataRow(true, DisplayName = "through an external host")]
    public async Task APictureGivenToAControl_IsTheProgramsOwnWhenItIsReadBack(bool external)
        => Printed(["Nothing", "0 True True h set type 1 265x529", "True True"], await RunAsync(external,
            "Debug.Print Desc(img.Picture)",
            "Set p = LoadPicture(D & \"bmp24.bmp\")",
            "Set img.Picture = p: Debug.Print Err.Number & \" \" & (img.Picture Is p) & \" \" & (img.Picture.Handle = p.Handle) & \" \" & Desc(img.Picture)",
            "Debug.Print (img.Picture Is img.Picture) & \" \" & (TypeOf img.Picture Is StdPicture)"));

    [TestMethod]
    public async Task OnePicture_IsSharedByTheControlsGivenIt_AndByTheForm()
        => Printed(["0 True True"], await RunAsync(false,
            "Set p = LoadPicture(D & \"bmp24.bmp\"): Set img.Picture = p",
            "Set o = ExampleView.Controls.Add(\"Forms.Label.1\", \"Label1\"): Set o.Picture = p: Set ExampleView.Picture = p",
            "Debug.Print Err.Number & \" \" & (o.Picture Is img.Picture) & \" \" & (ExampleView.Picture Is p)"));

    // MS-VBAL §5.4.3.8 and §5.5.1.2.13 let-coerce an object assigned without Set to a property of a class type through its default member (Handle, a Long),
    // which raises 424; MS-VBA gives the property the object itself (observed in Excel).
    [TestMethod]
    [Ignore("an object let-assigned to a property of a class type is let-coerced through its default member: MS-VBA gives the property the object")]
    public async Task APicture_IsGivenWithoutSet_AsWithIt()
        => Printed(["0 True"], await RunAsync(false,
            "Set p = LoadPicture(D & \"pic.gif\"): img.Picture = p: Debug.Print Err.Number & \" \" & (img.Picture Is p)"));

    [TestMethod]
    [DataRow("Nothing", "Nothing", DisplayName = "Nothing: Nothing")]
    [DataRow("LoadPicture()", "Nothing", DisplayName = "an empty picture: Nothing")]
    [DataRow("New StdPicture", "h 0 type 0 0x0", DisplayName = "a new StdPicture, which holds none: itself")]
    [DataRow("LoadPicture(D & \"three.ico\")", "h set type 3 847x847", DisplayName = "an icon")]
    [DataRow("LoadPicture(D & \"placeable.wmf\")", "h set type 2 2540x1270", DisplayName = "a metafile")]
    [DataRow("LoadPicture(D & \"frame.emf\")", "h set type 4 4964x2964", DisplayName = "an enhanced metafile")]
    public async Task WhatAControlIsGiven_IsWhatItReadsBack(string picture, string expected)
        => Printed(["0 " + expected], await RunAsync(false,
            "Set img.Picture = LoadPicture(D & \"bmp24.bmp\")",
            $"Set img.Picture = {picture}: Debug.Print Err.Number & \" \" & Desc(img.Picture)"));

    [TestMethod]
    public async Task AMouseIcon_IsAnIconOrACursor_AndNoBitmap()
        => Printed(["485 h 0 type 0 0x0", "0 True", "0 True"], await RunAsync(false,
            "Set img.MouseIcon = LoadPicture(D & \"bmp24.bmp\"): Debug.Print Err.Number & \" \" & Desc(img.MouseIcon): Err.Clear",
            "Set p = LoadPicture(D & \"three.ico\"): Set img.MouseIcon = p: Debug.Print Err.Number & \" \" & (img.MouseIcon Is p)",
            "Set p = LoadPicture(D & \"arrow.cur\"): Set ExampleView.MouseIcon = p: Debug.Print Err.Number & \" \" & (ExampleView.MouseIcon Is p)"));

    [TestMethod]
    public async Task AnObjectThatIsNoPicture_IsATypeMismatch_AndLeavesThePicture()
        => Printed(["13 h set type 1 265x529"], await RunAsync(false,
            "Set img.Picture = LoadPicture(D & \"bmp24.bmp\")",
            "Set o = New Collection: Set img.Picture = o: Debug.Print Err.Number & \" \" & Desc(img.Picture)"));

    [TestMethod]
    public async Task APictureAControlHolds_OutlivesTheProgramsVariable_AndIsSavedAsTheProgramsOwn()
        => Printed(["h set type 1 265x529", "0 694", "5"], await RunAsync(false,
            "Set p = LoadPicture(D & \"bmp24.bmp\"): Set img.Picture = p: Set p = Nothing: Debug.Print Desc(img.Picture)",
            "SavePicture img.Picture, D & \"saved.bmp\": Debug.Print Err.Number & \" \" & FileLen(D & \"saved.bmp\")",
            "img.Picture.hPal = 5: Set p = img.Picture: Debug.Print p.hPal"));

    [TestMethod]
    public async Task AControlsPicture_ReadAsAValue_IsItsHandle()
        => Printed(["0 Long True"], await RunAsync(false,
            "Dim v As Variant: Set img.Picture = LoadPicture(D & \"bmp24.bmp\")",
            "v = img.Picture: Debug.Print Err.Number & \" \" & TypeName(v) & \" \" & (v = img.Picture.Handle)"));
}
