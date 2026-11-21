using RDCore.External.Automation;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using System.Globalization;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// The fonts of a Visual Basic 6.0 form's controls, hosted by RDForms, headless: a control's <c>Font</c> is a <c>StdFont</c>, the same object on every read,
/// which a change through any reference changes, and which a <c>Set</c> to another control's <c>Font</c> shares; <c>FontBold</c> and its kin are members of it.
/// </summary>
// the active form is the UI thread's, which every RDForms window shares: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicFormFontTests
{
    // Form7, with Label1, a Label in the form's font, and Text1, a TextBox whose file gives it Arial 12, bold.
    private const string Form7 = "VERSION 5.00\r\n"
        + "Begin VB.Form Form7 \r\n"
        + "   Caption         =   \"Form7\"\r\n"
        + "   ClientHeight    =   3195\r\n"
        + "   ClientWidth     =   4680\r\n"
        + "   Begin VB.TextBox Text1 \r\n"
        + "      BeginProperty Font \r\n"
        + "         Name            =   \"Arial\"\r\n"
        + "         Size            =   12\r\n"
        + "         Charset         =   0\r\n"
        + "         Weight          =   700\r\n"
        + "         Underline       =   0   'False\r\n"
        + "         Italic          =   0   'False\r\n"
        + "         Strikethrough   =   0   'False\r\n"
        + "      EndProperty\r\n"
        + "      Height          =   375\r\n"
        + "      Left            =   120\r\n"
        + "      TabIndex        =   0\r\n"
        + "      Text            =   \"Text1\"\r\n"
        + "      Top             =   120\r\n"
        + "      Width           =   1500\r\n"
        + "   End\r\n"
        + "   Begin VB.Label Label1 \r\n"
        + "      Caption         =   \"Label1\"\r\n"
        + "      Height          =   375\r\n"
        + "      Left            =   120\r\n"
        + "      TabIndex        =   1\r\n"
        + "      Top             =   720\r\n"
        + "      Width           =   1500\r\n"
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
        "Public Sub Main()",
        "    Dim f As Object, g As Object",
        "    Form7.Show",
        "    DoEvents",
        .. body.Select(line => "    " + line),
        "    Unload Form7",
        "End Sub",
        string.Empty,
    ]);

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static async Task AssertPrintsAsync(string[] expected, params string[] body)
    {
        var printed = await ModuleWorkspace.RunAsync([], Program(body), Libraries, [("Form7", Form7, FormDesigner.Read(Form7)!)], SupportedLanguages.VB6);
        printed = [.. printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal) && line != "Paint")];

        CollectionAssert.AreEqual(expected, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public Task AControlsFont_IsWhatItsFileGivesIt_OrTheFormsFont()
        => AssertPrintsAsync(["Arial 12 True 700", "MS Sans Serif 8.25 False 400"],
            "Debug.Print Form7.Text1.Font.Name & \" \" & Form7.Text1.Font.Size & \" \" & Form7.Text1.Font.Bold & \" \" & Form7.Text1.Font.Weight",
            "Debug.Print Form7.Label1.Font & \" \" & Form7.Label1.Font.Size & \" \" & Form7.Label1.Font.Bold & \" \" & Form7.Label1.Font.Weight");

    [TestMethod]
    public Task AControlsFont_IsTheSameObjectOnEveryRead_AndAChangeThroughItIsTheControls()
        => AssertPrintsAsync(["True", "True True", "True True"],
            "Debug.Print Form7.Label1.Font Is Form7.Label1.Font",
            "Set f = Form7.Label1.Font",
            "f.Bold = True",
            "Debug.Print Form7.Label1.Font.Bold & \" \" & Form7.Label1.FontBold",
            "Form7.Label1.FontStrikethru = True",
            "Debug.Print f.Strikethrough & \" \" & Form7.Label1.Font.Strikethrough");

    [TestMethod]
    [DataRow("FontBold", "True", "Bold", DisplayName = "FontBold")]
    [DataRow("FontItalic", "True", "Italic", DisplayName = "FontItalic")]
    [DataRow("FontUnderline", "True", "Underline", DisplayName = "FontUnderline")]
    [DataRow("FontStrikethru", "True", "Strikethrough", DisplayName = "FontStrikethru")]
    [DataRow("FontName", "\"Arial\"", "Name", DisplayName = "FontName")]
    public Task VB6sFontProperties_AreMembersOfTheControlsFont(string property, string value, string member)
        => AssertPrintsAsync([$"{value.Trim('"')} {value.Trim('"')}"],
            $"Form7.Label1.{property} = {value}",
            $"Debug.Print Form7.Label1.Font.{member} & \" \" & Form7.Label1.{property}");

    [TestMethod]
    public Task ASize_IsTheOneTheFontIsDrawnAt()
        => AssertPrintsAsync(["14.25 14.25"],
            "Form7.Label1.FontName = \"Arial\"",
            "Form7.Label1.FontSize = 14",
            "Debug.Print Form7.Label1.FontSize & \" \" & Form7.Label1.Font.Size");

    [TestMethod]
    public Task SetOfAnotherControlsFont_SharesIt()
        => AssertPrintsAsync(["True Arial", "True", "True"],
            "Set Form7.Label1.Font = Form7.Text1.Font",
            "Debug.Print (Form7.Label1.Font Is Form7.Text1.Font) & \" \" & Form7.Label1.FontName",
            "Form7.Text1.Font.Italic = True",
            "Debug.Print Form7.Label1.FontItalic",
            "Form7.Label1.FontUnderline = True",
            "Debug.Print Form7.Text1.Font.Underline");

    [TestMethod]
    public Task SetOfANewStdFont_GivesTheControlItsValues()
        => AssertPrintsAsync(["Arial 12 True True", "MS Sans Serif 8.25 False"],
            "Set f = New StdFont: f.Name = \"Arial\": f.Size = 12: f.Bold = True: f.Italic = True",
            "Set Form7.Label1.Font = f",
            "Debug.Print Form7.Label1.FontName & \" \" & Form7.Label1.FontSize & \" \" & Form7.Label1.FontBold & \" \" & Form7.Label1.Font.Italic",
            "Set Form7.Text1.Font = New StdFont",
            "Debug.Print Form7.Text1.FontName & \" \" & Form7.Text1.FontSize & \" \" & Form7.Text1.FontBold");

    [TestMethod]
    public Task SetOfANewStdFont_SharesIt()
        => AssertPrintsAsync(["True", "True", "True"],
            "Set f = New StdFont",
            "Set Form7.Label1.Font = f",
            "Debug.Print Form7.Label1.Font Is f",
            "f.Bold = True",
            "Debug.Print Form7.Label1.FontBold",
            "Form7.Label1.FontItalic = True",
            "Debug.Print f.Italic");

    [TestMethod]
    public Task ANewStdFontSetToTwoControls_IsSharedByBoth()
        => AssertPrintsAsync(["True True True"],
            "Set f = New StdFont",
            "Set Form7.Label1.Font = f: Set Form7.Text1.Font = f",
            "Form7.Label1.FontUnderline = True",
            "Debug.Print (Form7.Label1.Font Is Form7.Text1.Font) & \" \" & Form7.Text1.FontUnderline & \" \" & f.Underline");

    [TestMethod]
    public Task AControlGivenAnotherFont_NoLongerSharesTheProgramsFont()
        => AssertPrintsAsync(["False True False"],
            "Set f = New StdFont",
            "Set Form7.Label1.Font = f",
            "Set Form7.Label1.Font = Form7.Text1.Font",
            "f.Italic = True",
            "Debug.Print Form7.Label1.FontItalic & \" \" & Form7.Label1.FontBold & \" \" & (Form7.Label1.Font Is f)");

    // Courier 14, its size not read, snaps to Arial's 14.25, as a font no control holds does.
    [TestMethod]
    [Ignore("not implemented: telling the forms' server what a shared font holds reads its size, which a later name then snaps from")]
    public Task ASharedFontsSize_SnapsToALaterName_AsTheProgramLastSetOrReadIt()
        => AssertPrintsAsync(["14.25"],
            "Set f = New StdFont",
            "Set Form7.Label1.Font = f",
            "f.Name = \"Courier\": f.Size = 14: f.Name = \"Arial\"",
            "Debug.Print f.Size");

    [TestMethod]
    public Task WhatASharedFontRefuses_ThroughTheControl_RaisesWhatVB6Raises_AndChangesNothing()
        => AssertPrintsAsync(["6 8.25"],
            "Set f = New StdFont",
            "Set Form7.Label1.Font = f",
            "On Error Resume Next",
            "Form7.Label1.FontSize = 24576",
            "Debug.Print Err.Number & \" \" & f.Size");

    // the platform's server of an RDForms world whose machine has none: it is told what the forms' server is told of the session's objects.
    private sealed class ToldPlatform : IAutomationServer
    {
        public List<SessionFont> Told { get; } = [];

        public bool IsAvailable => false;

        public object CreateObject(string progId) => UnavailableAutomationServer.Instance.CreateObject(progId);

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
            => UnavailableAutomationServer.Instance.Invoke(target, member, invocation, arguments, byReference, culture);

        public string? ClassNameOf(object target) => null;

        public bool MoveNext(object enumerator, out object? current) => UnavailableAutomationServer.Instance.MoveNext(enumerator, out current);

        public void Reset(object enumerator) => UnavailableAutomationServer.Instance.Reset(enumerator);

        public void Advise(object source, IAutomationEventSink sink)
        {
        }

        public void Unadvise(object source)
        {
        }

        public void Release(object handle)
        {
        }

        public void SessionObjectChanged(SessionObjectReference changed) => Told.Add(changed.Font!);
    }

    [TestMethod]
    public async Task AChangeOfASharedStdFont_IsToldToTheFormsServers_WithItsValues_WhileAControlHoldsIt()
    {
        var platform = new ToldPlatform();
        var (automation, forms) = RDCore.External.Forms.RDForms.RDFormsHosting.Compose(platform, RDCore.External.Forms.UnavailableFormHost.Instance,
            new RDCore.External.Forms.RDForms.FormsOptions { VB = RDCore.External.Forms.RDForms.VisualBasicFormsHost.RDForms, Headless = true });
        var libraries = Libraries with
        {
            Outside = new RDCore.External.ExternalWorld(automation, RDCore.External.Native.UnavailableNativeLibraryHost.Instance) { Forms = forms },
        };

        _ = await ModuleWorkspace.RunAsync([], Program([
            "Set f = New StdFont",
            "f.Underline = True",
            "Set Form7.Label1.Font = f",
            "f.Bold = True",
            "Form7.Label1.FontItalic = True",
            "Set Form7.Label1.Font = Form7.Text1.Font",
            "f.Strikethrough = True",
        ]), libraries, [("Form7", Form7, FormDesigner.Read(Form7)!)], SupportedLanguages.VB6);

        CollectionAssert.AreEqual(new[] { "700 False True False", "700 True True False" },
            platform.Told.Select(font => $"{font.Weight} {font.Italic} {font.Underline} {font.Strikethrough}").ToArray());
    }

    [TestMethod]
    public Task AFont_IsStillReadAfterItsFormIsUnloaded()
        => AssertPrintsAsync(["True"], "Set f = Form7.Label1.Font", "f.Bold = True", "Unload Form7", "Debug.Print f.Bold");

    [TestMethod]
    [DataRow("Set Form7.Label1.Font = Nothing", "380", DisplayName = "Nothing")]
    [DataRow("Form7.Label1.Font.Size = 0", "380", DisplayName = "a size of 0")]
    [DataRow("Form7.Label1.FontSize = 24576", "6", DisplayName = "a size too large")]
    [DataRow("Form7.Label1.Font.Weight = 1001", "380", DisplayName = "a weight past 1000")]
    [DataRow("Form7.Label1.Font.Charset = 256", "380", DisplayName = "a character set past 255")]
    [DataRow("Form7.Label1.FontName = String(32, \"a\")", "380", DisplayName = "a name of 32 characters")]
    public Task WhatAFontRefuses_RaisesWhatVB6Raises_AndChangesNothing(string statement, string number)
        => AssertPrintsAsync([number, "MS Sans Serif 8.25 400 0"], "On Error Resume Next", statement, "Debug.Print Err.Number",
            "Set f = Form7.Label1.Font",
            "Debug.Print f.Name & \" \" & f.Size & \" \" & f.Weight & \" \" & f.Charset");

    [TestMethod]
    public Task TypeName_OfAControlsFont_IsFont()
        => AssertPrintsAsync(["Font"], "Debug.Print TypeName(Form7.Label1.Font)");
}
