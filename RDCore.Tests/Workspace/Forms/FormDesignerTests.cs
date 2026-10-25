using System.Buffers.Binary;
using System.Text;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.Tests.Workspace.Forms;

/// <remarks>
/// The <c>Example*</c> fixtures are forms of rubberduck-vba/MVVM (MIT), exported by the VBE. <c>AllControls.storage</c> is
/// a form's storage written by Microsoft Forms 2.0 itself (<c>IPersistStorage.Save</c>), with every control class Microsoft
/// Forms caches, a <c>Frame</c> in a <c>Frame</c>, and a <c>MultiPage</c> with a control on each page.
/// </remarks>
[TestClass]
public sealed class FormDesignerTests
{
    private static byte[] Fixture(string fileName)
    {
        var assembly = typeof(FormDesignerTests).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith($".Fixtures.{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string Text(string fileName) => Encoding.UTF8.GetString(Fixture(fileName));

    // the VBE's blob header: "LB", version 8, the length of the compound file that follows, then four more integers
    private static byte[] AsFrx(byte[] compoundFile)
    {
        var frx = new byte[24 + compoundFile.Length];
        frx[0] = (byte)'L';
        frx[1] = (byte)'B';
        frx[2] = 8;
        BinaryPrimitives.WriteInt32LittleEndian(frx.AsSpan(4), compoundFile.Length);
        compoundFile.CopyTo(frx, 24);
        return frx;
    }

    private const string AllControlsModule =
        "VERSION 5.00\r\n" +
        "Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} AllControls \r\n" +
        "   Caption         =   \"Every \"\"control\"\" it's got\"   'a quote, and an apostrophe in a string\r\n" +
        "   OleObjectBlob   =   \"AllControls.frx\":0000\r\n" +
        "   ShowModal       =   0   'False\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"AllControls\"\r\n";

    // the shape of a VB6 form: ActiveX controls listed first, nested blocks, grouped properties, a control array
    private const string VisualBasic6Module =
        "VERSION 5.00\r\n" +
        "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   3195\r\n" +
        "   StartUpPosition =   3  'Windows Default\r\n" +
        "   Begin VB.Frame Frame1 \r\n" +
        "      Caption         =   \"Frame1\"\r\n" +
        "      TabIndex        =   2\r\n" +
        "      Begin VB.CommandButton Command1 \r\n" +
        "         Caption         =   \"Command1\"\r\n" +
        "         Index           =   1\r\n" +
        "         TabIndex        =   4\r\n" +
        "      End\r\n" +
        "      Begin VB.CommandButton Command1 \r\n" +
        "         Caption         =   \"Command1\"\r\n" +
        "         Index           =   0\r\n" +
        "         TabIndex        =   3\r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "   Begin MSComctlLib.ListView ListView1 \r\n" +
        "      TabIndex        =   1\r\n" +
        "      BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851} \r\n" +
        "         Name            =   \"Tahoma\"\r\n" +
        "         TabIndex        =   99\r\n" +
        "      EndProperty\r\n" +
        "   End\r\n" +
        "   Begin VB.Label Label1 \r\n" +
        "      Caption         =   \"Label1\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private static string Tree(IEnumerable<FormControlDescriptor> controls, int depth = 0)
        => string.Concat(controls.Select(control =>
            $"{new string(' ', depth * 2)}{control.Name}{(control.Index is { } index ? $"({index})" : "")} : {control.Class} [{control.TabIndex}]\n{Tree(control.Children, depth + 1)}"));

    [TestMethod]
    public void Read_UserForm_ReadsItsControlsFromTheFrx()
    {
        var form = FormDesigner.Read(Text("ExampleView.frm"), Fixture("ExampleView.frx"))!;

        Assert.AreEqual(
            "AmountBox : MSForms.TextBox [0]\n" +
            "CancelButton : MSForms.CommandButton [1]\n" +
            "OkButton : MSForms.CommandButton [2]\n" +
            "BrowseButton : MSForms.CommandButton [3]\n" +
            "ValidationMessage1 : MSForms.Label [4]\n" +
            "OptionsFrame : MSForms.Frame [5]\n" +
            "  OptionButton1 : MSForms.OptionButton [0]\n" +
            "  OptionButton2 : MSForms.OptionButton [1]\n" +
            "InstructionsLabel : MSForms.Label [6]\n" +
            "TextBox1 : MSForms.TextBox [7]\n" +
            "ValidationMessage2 : MSForms.Label [8]\n" +
            "ValidationMessage3 : MSForms.Label [9]\n" +
            "ComboBox1 : MSForms.ComboBox [10]\n",
            Tree(form.Controls));
        Assert.AreEqual(13, form.AllControls().Count());
    }

    [TestMethod]
    public void Read_UserForm_ReadsItsOwnDesignerProperties()
    {
        var form = FormDesigner.Read(Text("ExampleView.frm"), Fixture("ExampleView.frx"))!;

        Assert.AreEqual("ExampleView", form.Name);
        Assert.AreEqual(FormDesignerKind.MSForms, form.Kind);
        Assert.AreEqual("MSForms.UserForm", form.Class.ToString());
        Assert.AreEqual(FormDesigner.MSFormsClassId, form.Class.ClassId);
        Assert.AreEqual("ExampleView", form.Caption);
        Assert.AreEqual(1, form.StartUpPosition);
        Assert.IsNull(form.ShowModal, "ShowModal is written only when it is not its default");
        Assert.AreEqual("612", form.Properties["clientheight"], "property names compare case-insensitively");
    }

    [TestMethod]
    public void Read_UserFormWithoutControls_HasNone()
    {
        var form = FormDesigner.Read(Text("ExampleDynamicView.frm"), Fixture("ExampleDynamicView.frx"))!;

        Assert.AreEqual("ExampleDynamicView", form.Name);
        Assert.AreEqual(0, form.Controls.Count);
    }

    [TestMethod]
    public void Read_StorageWrittenByMSForms_ReadsEveryCachedClassAndNesting()
    {
        var form = FormDesigner.Read(AllControlsModule, AsFrx(Fixture("AllControls.storage")))!;

        // a MultiPage's own tab strip is not a control; its pages are, with their own controls
        Assert.AreEqual(
            "Caption1 : MSForms.Label [1]\n" +
            "Entry : MSForms.TextBox [2]\n" +
            "Choices : MSForms.ComboBox [3]\n" +
            "Items : MSForms.ListBox [4]\n" +
            "Toggle1 : MSForms.CheckBox [5]\n" +
            "Option1 : MSForms.OptionButton [6]\n" +
            "Switch : MSForms.ToggleButton [7]\n" +
            "Go : MSForms.CommandButton [0]\n" +
            "Tabs : MSForms.TabStrip [8]\n" +
            "Scroller : MSForms.ScrollBar [9]\n" +
            "Spinner : MSForms.SpinButton [10]\n" +
            "Picture1 : MSForms.Image [13]\n" +
            "Outer : MSForms.Frame [11]\n" +
            "  OuterEntry : MSForms.TextBox [0]\n" +
            "  Inner : MSForms.Frame [1]\n" +
            "    InnerCheck : MSForms.CheckBox [0]\n" +
            "Pages : MSForms.MultiPage [12]\n" +
            "  Page1 : MSForms.Page [1]\n" +
            "    FirstPageEntry : MSForms.TextBox [0]\n" +
            "  Page2 : MSForms.Page [2]\n" +
            "    SecondPageGo : MSForms.CommandButton [0]\n",
            Tree(form.Controls));
    }

    [TestMethod]
    public void Read_DesignerProperty_UnquotesStringsAndDropsComments()
    {
        var form = FormDesigner.Read(AllControlsModule, AsFrx(Fixture("AllControls.storage")))!;

        Assert.AreEqual("Every \"control\" it's got", form.Caption);
        Assert.AreEqual(false, form.ShowModal);
        Assert.AreEqual("\"AllControls.frx\":0000", form.Properties["OleObjectBlob"]);
    }

    [TestMethod]
    public void CompanionOf_UserForm_IsItsOleObjectBlob()
    {
        Assert.AreEqual(new FormDesignerReference("ExampleView.frx", 0), FormDesigner.CompanionOf(Text("ExampleView.frm")));
        Assert.AreEqual(
            new FormDesignerReference("Other.frx", 0x1A2B),
            FormDesigner.CompanionOf(AllControlsModule.Replace("\"AllControls.frx\":0000", "\"Other.frx\":1A2B")));
    }

    [TestMethod]
    public void CompanionOf_VisualBasic6Form_IsNone()
        => Assert.IsNull(FormDesigner.CompanionOf(VisualBasic6Module));

    [TestMethod]
    public void Read_VisualBasic6Form_ReadsItsControlsFromItsText()
    {
        var form = FormDesigner.Read(VisualBasic6Module)!;

        Assert.AreEqual("Form1", form.Name);
        Assert.AreEqual(FormDesignerKind.VisualBasic6, form.Kind);
        Assert.AreEqual("VB.Form", form.Class.ToString());
        Assert.AreEqual(3, form.StartUpPosition);
        Assert.AreEqual(
            "Frame1 : VB.Frame [2]\n" +
            "  Command1(1) : VB.CommandButton [4]\n" +
            "  Command1(0) : VB.CommandButton [3]\n" +
            "ListView1 : MSComctlLib.ListView [1]\n" +
            "Label1 : VB.Label []\n",
            Tree(form.Controls));
    }

    [TestMethod]
    public void Read_OtherDesigner_IsNamedByItsClassId()
    {
        var form = FormDesigner.Read("VERSION 5.00\r\nBegin {78E93846-85FD-11D0-8487-00A0C90DC8A9} DataReport1 \r\nEnd\r\n")!;

        Assert.AreEqual(FormDesignerKind.Other, form.Kind);
        Assert.AreEqual(new Guid("78E93846-85FD-11D0-8487-00A0C90DC8A9"), form.Class.ClassId);
        Assert.AreEqual("{78E93846-85FD-11D0-8487-00A0C90DC8A9}", form.Class.ToString());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\n")]
    [DataRow("VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Class1\"\r\n")]
    public void Read_NotAFormModule_IsNull(string? source)
    {
        Assert.IsNull(FormDesigner.Read(source));
        Assert.IsNull(FormDesigner.CompanionOf(source));
    }

    [TestMethod]
    public void Read_FormModuleWithLineFeedEndings_ReadsTheSame()
    {
        // a checkout on Linux normalizes the VBE's CRLF to LF
        var crlf = FormDesigner.Read(VisualBasic6Module)!;
        var lf = FormDesigner.Read(VisualBasic6Module.Replace("\r\n", "\n"))!;

        Assert.AreEqual(Tree(crlf.Controls), Tree(lf.Controls));
        Assert.AreEqual(crlf.Caption, lf.Caption);
    }

    private static FormDesignerFormatProblem ProblemOf(Action read)
        => Assert.ThrowsExactly<FormDesignerFormatException>(read).Problem;

    [TestMethod]
    public void Read_UnterminatedBlock_Throws()
        => Assert.AreEqual(
            FormDesignerFormatProblem.UnterminatedBlock,
            ProblemOf(() => FormDesigner.Read("VERSION 5.00\r\nBegin VB.Form Form1\r\n   Begin VB.Label Label1\r\n   End\r\n")));

    [TestMethod]
    public void Read_UserFormWithoutItsFrx_Throws()
        => Assert.AreEqual(FormDesignerFormatProblem.MissingCompanion, ProblemOf(() => FormDesigner.Read(Text("ExampleView.frm"))));

    [TestMethod]
    public void Read_BlobOffsetPastTheFrx_Throws()
        => Assert.AreEqual(
            FormDesignerFormatProblem.BlobOutOfRange,
            ProblemOf(() => FormDesigner.Read(Text("ExampleView.frm").Replace(".frx\":0000", ".frx\":FFFF"), Fixture("ExampleView.frx"))));

    [TestMethod]
    public void Read_BlobLengthPastTheFrx_Throws()
    {
        var frx = Fixture("ExampleView.frx");
        BinaryPrimitives.WriteInt32LittleEndian(frx.AsSpan(4), frx.Length);

        Assert.AreEqual(FormDesignerFormatProblem.BlobOutOfRange, ProblemOf(() => FormDesigner.Read(Text("ExampleView.frm"), frx)));
    }

    [TestMethod]
    public void Read_BlobThatIsNotACompoundFile_Throws()
        => Assert.AreEqual(
            FormDesignerFormatProblem.NotACompoundFile,
            ProblemOf(() => FormDesigner.Read(AllControlsModule, AsFrx(new byte[1024]))));

    [TestMethod]
    public void Read_CompoundFileWithACycleInItsChains_Throws()
    {
        // point the directory's first sector's FAT entry back at itself: a chain that never ends
        var storage = Fixture("AllControls.storage");
        var sectorSize = 1 << BinaryPrimitives.ReadUInt16LittleEndian(storage.AsSpan(30));
        var directorySector = BinaryPrimitives.ReadInt32LittleEndian(storage.AsSpan(48));
        var firstFatSector = BinaryPrimitives.ReadInt32LittleEndian(storage.AsSpan(76));
        BinaryPrimitives.WriteInt32LittleEndian(storage.AsSpan((firstFatSector + 1) * sectorSize + directorySector * 4), directorySector);

        Assert.AreEqual(FormDesignerFormatProblem.CorruptCompoundFile, ProblemOf(() => FormDesigner.Read(AllControlsModule, AsFrx(storage))));
    }

    private static byte[] FormStreamOf(byte[] storage)
    {
        var file = CompoundFile.Open(storage);
        return file.Read(file.Find(file.Root, "f")!);
    }

    [TestMethod]
    public void FormStream_Truncated_Throws()
    {
        var stream = FormStreamOf(Fixture("AllControls.storage"));

        Assert.AreEqual(FormDesignerFormatProblem.Truncated, ProblemOf(() => MSFormsFormStream.Read(stream.AsSpan(0, stream.Length / 2))));
    }

    [TestMethod]
    public void FormStream_UnknownVersion_Throws()
    {
        var stream = FormStreamOf(Fixture("AllControls.storage"));
        stream[1] = 5;

        Assert.AreEqual(FormDesignerFormatProblem.UnsupportedVersion, ProblemOf(() => MSFormsFormStream.Read(stream)));
    }

    [TestMethod]
    public void Exception_Message_IsLocalized()
    {
        var exception = new FormDesignerFormatException(FormDesignerFormatProblem.MissingFormStream);

        Assert.IsFalse(string.IsNullOrWhiteSpace(exception.Message));
        Assert.AreEqual(FormDesignerFormatProblem.MissingFormStream, exception.Problem);
    }
}
