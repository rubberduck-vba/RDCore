using System.Runtime.InteropServices;
using System.Text;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.External;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A form's <c>Left</c>, <c>Top</c>, <c>Width</c> and <c>Height</c>, which are its window's, against its <c>InsideWidth</c> and <c>InsideHeight</c>, as Excel
/// (64-bit) has them. A size or position rounded to a pixel is Excel's at 96 DPI only, and so are these rows: at another DPI they are inconclusive.
/// </summary>
/// <remarks>
/// SizeForm is MVVM's ExampleView's designer state (InsideWidth 328.2, InsideHeight 235.8) under a designer block of the rows' own: ClientLeft 45 and ClientTop
/// 375 twips, and the ClientWidth, ClientHeight and StartUpPosition each row says. Each form shown is shown modeless and unloaded.
/// </remarks>
[TestClass]
[TestCategory("MSForms")]
[DoNotParallelize]
// Windows only past RequireMicrosoftForms(), which is inconclusive on any other platform.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class FormSizeTests
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private static void RequireMeasuredDpi()
    {
        FormHostingTests.RequireMicrosoftForms();
        if (GetDpiForSystem() != 96)
        {
            Assert.Inconclusive("The sizes Excel was measured for are at 96 DPI.");
        }
    }

    private static (string Name, string Source, FormDescriptor Designer) SizeForm(int startUpPosition = 1, int clientWidth = 4000, int clientHeight = 3000)
    {
        var source = string.Join("\r\n",
            "VERSION 5.00",
            "Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} SizeForm ",
            "   Caption         =   \"SizeForm\"",
            $"   ClientHeight    =   {clientHeight}",
            "   ClientLeft      =   45",
            "   ClientTop       =   375",
            $"   ClientWidth     =   {clientWidth}",
            "   OleObjectBlob   =   \"SizeForm.frx\":0000",
            $"   StartUpPosition =   {startUpPosition}",
            "End",
            "Attribute VB_Name = \"SizeForm\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False",
            "Option Explicit",
            string.Empty);
        return ("SizeForm", source, FormDesigner.Read(source, FormHostingTests.Fixture("ExampleView.frx"))!);
    }

    // what the program prints of each value: a number with Str (no locale), as the measuring probe did.
    private static string Program(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nPrivate Sub P(ByVal x As Variant)\r\n"
           + "    If VarType(x) <> vbString And IsNumeric(x) Then Debug.Print Trim$(Str$(x)) Else Debug.Print CStr(x)\r\nEnd Sub\r\n"
           + $"Public Sub Main()\r\n    On Error Resume Next\r\n    {string.Join("\r\n    ", body)}\r\n    Unload SizeForm\r\nEnd Sub\r\n";

    private static async Task<string[]> RunAsync((string Name, string Source, FormDescriptor Designer) form, params string[] body)
    {
        var (world, apartment) = FormHostingTests.InProcess();
        using var _ = apartment;
        return await ModuleWorkspace.RunAsync([], Program(body), new ModuleWorkspace.WorkspaceLibraries(["MSForms"], FormHostingTests.Shipped, Outside: world), [form]);
    }

    [TestMethod]
    public async Task LeftTopWidthAndHeight_AreSingles()
    {
        FormHostingTests.RequireMicrosoftForms();

        var printed = await RunAsync(SizeForm(),
            "P TypeName(SizeForm.Left)", "P TypeName(SizeForm.Top)", "P TypeName(SizeForm.Width)", "P TypeName(SizeForm.Height)");

        CollectionAssert.AreEqual(new[] { "Single", "Single", "Single", "Single" }, printed, string.Join(" | ", printed));
    }

    // the window is the designer block's client area and the frame (12 points wide, 29.25 high), whatever size the designer's state gives the form, which is
    // InsideWidth and InsideHeight; the same once shown. Until it is shown, it is where ClientLeft and ClientTop put its client area.
    [TestMethod]
    public async Task TheWindow_IsTheDesignerBlocksClientArea_AndTheFormItsOwnSize()
    {
        RequireMeasuredDpi();

        var printed = await RunAsync(SizeForm(),
            "P SizeForm.Width", "P SizeForm.Height", "P SizeForm.InsideWidth", "P SizeForm.InsideHeight", "P SizeForm.Left", "P SizeForm.Top",
            "SizeForm.Show vbModeless", "DoEvents",
            "P SizeForm.Width", "P SizeForm.Height", "P SizeForm.InsideWidth", "P SizeForm.InsideHeight");

        CollectionAssert.AreEqual(
            new[] { "212.25", "179.25", "328.2", "235.8", "-3.75", "-4.5", "212.25", "179.25", "328.2", "235.8" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AnotherClientArea_SizesTheWindow_AndNotTheForm()
    {
        RequireMeasuredDpi();

        var printed = await RunAsync(SizeForm(clientWidth: 6000, clientHeight: 2000),
            "P SizeForm.InsideWidth", "P SizeForm.InsideHeight", "P SizeForm.Width", "P SizeForm.Height");

        CollectionAssert.AreEqual(new[] { "328.2", "235.8", "312", "129" }, printed, string.Join(" | ", printed));
    }

    // a size the program writes reads back as written; the form is sized to the client area the window has then, rounded to a pixel.
    [TestMethod]
    [DataRow(false, DisplayName = "not shown")]
    [DataRow(true, DisplayName = "shown")]
    public async Task AWrittenSize_ReadsBackAsWritten_AndSizesTheForm(bool shown)
    {
        RequireMeasuredDpi();
        string[] show = shown ? ["SizeForm.Show vbModeless", "DoEvents"] : [];

        var printed = await RunAsync(SizeForm(), [.. show,
            "SizeForm.Width = 300", "P SizeForm.Width", "P SizeForm.InsideWidth",
            "SizeForm.Height = SizeForm.Height + 10", "P SizeForm.Height", "P SizeForm.InsideHeight", "P Err.Number"]);

        CollectionAssert.AreEqual(new[] { "300", "288", "189.25", "159.75", "0" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task ASizeTheDesktopDoesNotAllow_ReadsBackAsTheWindows()
    {
        RequireMeasuredDpi();

        var printed = await RunAsync(SizeForm(),
            "SizeForm.Width = 100.3", "P SizeForm.Width", "SizeForm.Width = 100.6", "P SizeForm.Width",
            "SizeForm.Show vbModeless", "DoEvents", "P SizeForm.Width",
            "SizeForm.Height = 0", "P Err.Number", "P SizeForm.Height", "P SizeForm.InsideHeight");

        CollectionAssert.AreEqual(new[] { "102", "102", "102", "0", "29.25", "0" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task ANegativeWidth_IsIgnored()
    {
        RequireMeasuredDpi();

        var printed = await RunAsync(SizeForm(), "SizeForm.Width = -10", "P Err.Number", "P SizeForm.Width");

        CollectionAssert.AreEqual(new[] { "0", "212.25" }, printed, string.Join(" | ", printed));
    }

    // a written size is let-coerced to a Single, so a String that is not a number is a type mismatch. 1E+30 is a Single, and an overflow because no window's
    // pixels reach it. Neither changes the size.
    [TestMethod]
    public async Task AWrittenSize_IsLetCoercedToASingle()
    {
        RequireMeasuredDpi();

        var printed = await RunAsync(SizeForm(),
            "SizeForm.Width = \"abc\"", "P Err.Number", "Err.Clear",
            "SizeForm.Width = 1E+30", "P Err.Number", "Err.Clear", "P SizeForm.Width",
            "SizeForm.Width = \"250\"", "P Err.Number", "P SizeForm.Width");

        CollectionAssert.AreEqual(new[] { "13", "6", "212.25", "0", "250" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AWrittenPosition_ReadsBackAsWritten()
    {
        RequireMeasuredDpi();

        var printed = await RunAsync(SizeForm(),
            "SizeForm.Left = 100", "SizeForm.Top = 50", "P SizeForm.Left", "P SizeForm.Top",
            "SizeForm.Show vbModeless", "DoEvents",
            "SizeForm.Left = 100", "SizeForm.Top = 50", "P SizeForm.Left", "P SizeForm.Top");

        CollectionAssert.AreEqual(new[] { "100", "50", "100", "50" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow(0, DisplayName = "Manual")]
    [DataRow(3, DisplayName = "Windows Default")]
    public async Task AFormThatIsNotCentered_StartsUpWhereItIs(int startUpPosition)
    {
        RequireMeasuredDpi();

        var printed = await RunAsync(SizeForm(startUpPosition),
            "P SizeForm.Left", "P SizeForm.Top", "SizeForm.Show vbModeless", "DoEvents", "P SizeForm.Left", "P SizeForm.Top");

        var at = startUpPosition == 0 ? new[] { "-3.75", "-4.5" } : ["0", "0"];
        CollectionAssert.AreEqual(new[] { at[0], at[1], at[0], at[1] }, printed, string.Join(" | ", printed));
    }

    // centered on the screen, whichever screen it is: the window's pixels, halved, from the screen's.
    [TestMethod]
    public async Task ACenterScreenForm_StartsUpCenteredOnTheScreen()
    {
        RequireMeasuredDpi();

        var printed = await RunAsync(SizeForm(2),
            "P SizeForm.Left", "P SizeForm.Top", "SizeForm.Show vbModeless", "DoEvents", "P SizeForm.Left", "P SizeForm.Top");

        // 212.25 x 179.25 points is 283 x 239 pixels at 96 DPI.
        var left = ((GetSystemMetrics(0) - 283) / 2) * 0.75;
        var top = ((GetSystemMetrics(1) - 239) / 2) * 0.75;
        CollectionAssert.AreEqual(
            new[] { "-3.75", "-4.5", left.ToString(System.Globalization.CultureInfo.InvariantCulture), top.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            printed, string.Join(" | ", printed));
    }
}
