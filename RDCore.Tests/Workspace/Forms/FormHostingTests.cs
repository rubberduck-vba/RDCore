using RDCore.External;
using RDCore.External.Native;
using RDCore.External.Windows.Automation;
using RDCore.External.Windows.Forms;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.External;
using RDCore.Tests.Runtime.Libraries;
using System.Runtime.InteropServices;
using System.Text;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A program's forms, hosted: the members a form's designer declares reach the form host - <c>Show</c>, <c>Hide</c>, <c>Visible</c>, <c>Caption</c> - or the
/// Microsoft Forms form it creates the first time the program uses the form, whose controls are objects of the automation server like any other. In the process, on
/// one apartment, and through an external host, as <c>rdc</c> runs a program. Inconclusive on a machine without Microsoft Forms, as the Excel tests are without Excel.
/// </summary>
[TestClass]
[TestCategory("MSForms")]
[DoNotParallelize]
public sealed class FormHostingTests
{
    private const uint WM_CLOSE = 0x0010;

    // Microsoft Forms 2.0 as the platform ships it (`rdc export-library` of FM20.DLL): every control a real form has is one of its classes.
    private static DirectoryLibrarySource Shipped => new(new System.IO.Abstractions.FileSystem(), ShippedLibrariesTests.Symbols);

    private static void RequireMicrosoftForms()
    {
        if (!OperatingSystem.IsWindows() || Type.GetTypeFromProgID("Forms.Form.1") is null)
        {
            Assert.Inconclusive("Microsoft Forms 2.0 is not installed on this machine.");
        }
    }

    // the servers and the form host of the test's own, on one apartment, as the external host composes them.
    private static (ExternalWorld World, ComApartment Apartment) InProcess()
    {
        var apartment = new ComApartment();
        return (new ExternalWorld(new ComAutomationServer(apartment), UnavailableNativeLibraryHost.Instance) { Forms = new WindowsFormHost(apartment) }, apartment);
    }

    private static byte[] Fixture(string fileName)
    {
        var assembly = typeof(FormHostingTests).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith($".Fixtures.{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // MVVM's ExampleView - its designer, AmountBox and all - with no code behind it: the header and the attributes of the module, and nothing of MVVM.
    private static (string Name, string Source, FormDescriptor Designer) ExampleView(params string[] code)
    {
        var frm = Encoding.UTF8.GetString(Fixture("ExampleView.frm"));
        var lines = frm.Split("\r\n");
        var header = string.Join("\r\n", lines.Take(Array.FindLastIndex(lines, line => line.StartsWith("Attribute VB_", StringComparison.Ordinal)) + 1));
        return ("ExampleView", header + "\r\nOption Explicit\r\n" + string.Join("\r\n", code) + "\r\n", FormDesigner.Read(frm, Fixture("ExampleView.frx"))!);
    }

    private static Task<string[]> RunWithCodeAsync(ExternalWorld world, string[] code, params string[] body)
        => ModuleWorkspace.RunAsync([], Program(body), new ModuleWorkspace.WorkspaceLibraries(["MSForms"], Shipped, Outside: world), [ExampleView(code)]);

    // the form's code, handling the events of a control and of the form
    private static readonly string[] Handlers =
    [
        "Private Sub AmountBox_Change()", "    Debug.Print \"changed to \"; AmountBox.Text", "End Sub",
        "Private Sub UserForm_Click()", "    Debug.Print \"form clicked\"", "End Sub",
    ];

    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static Task<string[]> RunAsync(ExternalWorld world, params string[] body)
        => ModuleWorkspace.RunAsync([], Program(body), new ModuleWorkspace.WorkspaceLibraries(["MSForms"], Shipped, Outside: world), [ExampleView()]);

    [TestMethod]
    public async Task AFormsCaption_IsItsDesignersUntilTheProgramSetsIt()
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;

        var printed = await RunAsync(world,
            "Debug.Print ExampleView.Caption",
            "ExampleView.Caption = \"Changed\"",
            "Debug.Print ExampleView.Caption",
            "Debug.Print ExampleView.Name");

        CollectionAssert.AreEqual(new[] { "ExampleView", "Changed", "ExampleView" }, printed);
    }

    [TestMethod]
    public async Task AModelessForm_IsShown_AndHidden()
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;

        var printed = await RunAsync(world,
            "Debug.Print ExampleView.Visible",
            "ExampleView.Show vbModeless",
            "Debug.Print ExampleView.Visible",
            "ExampleView.Hide",
            "Debug.Print ExampleView.Visible");

        CollectionAssert.AreEqual(new[] { "False", "True", "False" }, printed);
    }

    [TestMethod]
    public async Task AControl_IsAnObjectOfTheForm_WhoseMembersAreCalled()
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;

        // AmountBox's text is "0" as MVVM's designer saved it.
        var printed = await RunAsync(world,
            "Debug.Print ExampleView.InsideWidth > 0",
            "Debug.Print TypeName(ExampleView.AmountBox), ExampleView.AmountBox.Text",
            "ExampleView.AmountBox.Text = \"42\"",
            "Debug.Print ExampleView.AmountBox.Text");

        CollectionAssert.AreEqual(new[] { "True", "TextBox       0", "42" }, printed);
    }

    [TestMethod]
    public async Task AModalForm_IsWaitedFor_UntilItsCloseButtonIsClicked()
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;
        var caption = $"ExampleView {Guid.NewGuid():N}";

        var running = RunAsync(world,
            $"ExampleView.Caption = \"{caption}\"",
            "ExampleView.Show",
            "Debug.Print \"shown, and closed\"");

        var window = await WindowOfAsync(caption);
        await Task.Delay(200);
        Assert.IsFalse(running.IsCompleted, "Show waits for a modal form, as the designer's ShowModal says it is");

        Assert.IsTrue(PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        CollectionAssert.AreEqual(new[] { "shown, and closed" }, await running.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [TestMethod]
    public async Task AForm_ThroughAnExternalHost_IsHostedThere()
    {
        RequireMicrosoftForms();
        using var host = ExternalHosts.New();

        var printed = await RunAsync(ExternalHosts.WorldOf(host),
            "ExampleView.Caption = \"Remote\"",
            "Debug.Print ExampleView.Caption",
            "ExampleView.Show vbModeless",
            "Debug.Print ExampleView.Visible",
            "ExampleView.AmountBox.Text = \"7\"",
            "Debug.Print ExampleView.AmountBox.Text",
            "ExampleView.Hide",
            "Debug.Print ExampleView.Visible");

        CollectionAssert.AreEqual(new[] { "Remote", "True", "7", "False" }, printed);
    }

    // oracle\s41 L12, L51: an unloaded form's window is gone; the default instance is made again, with its designer's caption, and a form an Unload leaves is one
    // whose designer's members fail, as MS-VBA's do (-2147418105).
    [TestMethod]
    public async Task AnUnloadedForm_IsGoneWithItsWindow()
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;
        var caption = $"ExampleView {Guid.NewGuid():N}";

        var printed = await RunAsync(world,
            $"ExampleView.Caption = \"{caption}\"",
            "ExampleView.Show vbModeless",
            "Unload ExampleView",
            "Debug.Print ExampleView.Caption; ExampleView.Visible",
            "Dim f As ExampleView",
            "Set f = New ExampleView",
            "f.Caption = \"Another\"",
            "Unload f",
            "On Error Resume Next",
            "Debug.Print f.Caption",
            "Debug.Print Err.Number");

        // the statement that failed under On Error Resume Next prints nothing at all (L51).
        CollectionAssert.AreEqual(new[] { "ExampleViewFalse", "-2147418105" }, printed, string.Join(" | ", printed));
        Assert.AreEqual(IntPtr.Zero, FindWindow("RDCore.UserForm", caption), "the unloaded form's window is destroyed");
    }

    // a control raises its event inside the call that made it - Change while its Text is assigned - and the handler the form's code has for it runs then, inside it.
    [TestMethod]
    public async Task AControlsEvent_RunsTheHandlerNamedAfterIt()
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;

        var printed = await RunWithCodeAsync(world, Handlers, "ExampleView.Show vbModeless", "ExampleView.AmountBox.Text = \"42\"", "Debug.Print \"after\"", "Unload ExampleView");

        CollectionAssert.AreEqual(new[] { "changed to 42", "after" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AControlsEvent_ThroughAnExternalHost_RunsTheHandler()
    {
        RequireMicrosoftForms();
        using var host = ExternalHosts.New();

        var printed = await RunWithCodeAsync(ExternalHosts.WorldOf(host), Handlers, "ExampleView.Show vbModeless", "ExampleView.AmountBox.Text = \"7\"", "Debug.Print \"after\"", "Unload ExampleView");

        CollectionAssert.AreEqual(new[] { "changed to 7", "after" }, printed, string.Join(" | ", printed));
    }

    // what an object answers to is asked of it where it lives: in the external host (oracle\s41 M04, M05).
    [TestMethod]
    public async Task AControl_ThroughAnExternalHost_IsTheExtenderItIsWrappedIn()
    {
        RequireMicrosoftForms();
        using var host = ExternalHosts.New();

        var printed = await RunWithCodeAsync(ExternalHosts.WorldOf(host), Handlers, "Debug.Print TypeOf ExampleView.AmountBox Is MSForms.Control",
            "Debug.Print TypeOf ExampleView.AmountBox Is MSForms.ComboBox", "Dim c As MSForms.Control", "Set c = ExampleView.AmountBox", "Debug.Print c.Name", "Unload ExampleView");

        CollectionAssert.AreEqual(new[] { "True", "False", "AmountBox" }, printed, string.Join(" | ", printed));
    }

    // a modal Show waits; a click on the form while it does is the answer to that call, and runs UserForm_Click inside it - which hides the form, which ends the wait.
    [TestMethod]
    [DataRow(false, DisplayName = "in the process")]
    [DataRow(true, DisplayName = "through an external host")]
    public async Task AClickOnAModalForm_RunsItsHandler_WhichCanHideIt(bool external)
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;
        using var host = ExternalHosts.New();
        var caption = $"ExampleView {Guid.NewGuid():N}";
        string[] code = ["Private Sub UserForm_Click()", "    Debug.Print \"form clicked\"", "    Me.Hide", "End Sub"];

        var running = RunWithCodeAsync(external ? ExternalHosts.WorldOf(host) : world, code, $"ExampleView.Caption = \"{caption}\"", "ExampleView.Show", "Debug.Print \"after\"");

        // the form's own window is the only child of the host's: a click on it, away from its controls, is the form's.
        var window = await WindowOfAsync(caption);
        var form = FindWindowEx(window, IntPtr.Zero, null, null);
        Assert.AreNotEqual(IntPtr.Zero, form);
        Assert.IsTrue(GetClientRect(form, out var client));
        var at = (IntPtr)(((client.Bottom - 4) << 16) | ((client.Right - 4) & 0xFFFF));
        Assert.IsTrue(PostMessage(form, 0x0201, (IntPtr)1, at));
        Assert.IsTrue(PostMessage(form, 0x0202, IntPtr.Zero, at));

        CollectionAssert.AreEqual(new[] { "form clicked", "after" }, await running.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    // oracle\s41 V01-V03: Show activates the form; showing another deactivates the first, then activates the other.
    [TestMethod]
    public async Task ShowingAForm_ActivatesIt_AndDeactivatesTheOneBefore()
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;
        string[] code =
        [
            "Private Sub UserForm_Activate()", "    Debug.Print \"Activate \"; Me.Caption", "End Sub",
            "Private Sub UserForm_Deactivate()", "    Debug.Print \"Deactivate \"; Me.Caption", "End Sub",
        ];

        var printed = await RunWithCodeAsync(world, code,
            "ExampleView.Show vbModeless", "Dim other As ExampleView", "Set other = New ExampleView", "other.Caption = \"Other\"", "other.Show vbModeless",
            "Unload other", "Unload ExampleView");

        CollectionAssert.AreEqual(new[] { "Activate ExampleView", "Deactivate ExampleView", "Activate Other" }, printed, string.Join(" | ", printed));
    }

    // the close button asks the form (QueryClose, vbFormControlMenu = 0), which unloads it as Unload does - which ends a modal Show; a QueryClose that cancels keeps it.
    [TestMethod]
    [DataRow(false, DisplayName = "in the process")]
    [DataRow(true, DisplayName = "through an external host")]
    public async Task TheCloseButton_AsksTheForm_ThenUnloadsIt(bool external)
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;
        using var host = ExternalHosts.New();
        var caption = $"ExampleView {Guid.NewGuid():N}";
        string[] code =
        [
            "Private Asked As Long",
            "Private Sub UserForm_QueryClose(Cancel As Integer, CloseMode As Integer)", "    Asked = Asked + 1", "    Debug.Print \"QueryClose\"; CloseMode",
            "    If Asked = 1 Then Cancel = True", "End Sub",
            "Private Sub UserForm_Terminate()", "    Debug.Print \"Terminate\"", "End Sub",
        ];

        var running = RunWithCodeAsync(external ? ExternalHosts.WorldOf(host) : world, code, $"ExampleView.Caption = \"{caption}\"", "ExampleView.Show", "Debug.Print \"after\"");

        var window = await WindowOfAsync(caption);
        Assert.IsTrue(PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        await Task.Delay(500);
        Assert.IsFalse(running.IsCompleted, "the first QueryClose cancels: the form stays, and Show still waits");
        Assert.AreNotEqual(IntPtr.Zero, FindWindow("RDCore.UserForm", caption));

        Assert.IsTrue(PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        CollectionAssert.AreEqual(new[] { "QueryClose 0", "QueryClose 0", "Terminate", "after" }, await running.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.AreEqual(IntPtr.Zero, FindWindow("RDCore.UserForm", caption), "the unloaded form's window is destroyed");
    }

    // a control added at run time, held by a WithEvents variable, and a handler named after it - which MS-VBA does not run (oracle\s41 C02)
    private static readonly string[] Dynamic =
    [
        "Public WithEvents Chk As MSForms.CheckBox",
        "Private Sub Chk_Click()", "    Debug.Print \"ChkClick\"", "End Sub",
        "Private Sub Chk_Change()", "    Debug.Print \"ChkChange\"", "End Sub",
        "Private Sub Dyn1_Click()", "    Debug.Print \"Dyn1Click\"", "End Sub",
        "Public Sub AddCheck()", "    Set Chk = Me.Controls.Add(\"Forms.CheckBox.1\", \"Dyn1\", True)", "End Sub",
    ];

    // oracle\s41 C01-C12: what Controls.Add and Controls.Remove do, as MS-VBA has it. ExampleView has 13 controls of its designer's.
    [TestMethod]
    [DataRow(new[] { "Debug.Print ExampleView.Controls.Count", "ExampleView.AddCheck", "Debug.Print ExampleView.Controls.Count", "Debug.Print TypeName(ExampleView.Chk)", "Debug.Print ExampleView.Chk.Name" },
        new[] { "13", "14", "CheckBox", "Dyn1" }, DisplayName = "C01 Add, and what it returns")]
    [DataRow(new[] { "ExampleView.AddCheck", "ExampleView.Chk.Value = True" }, new[] { "ChkChange", "ChkClick" },
        DisplayName = "C02 its events, to the WithEvents variable's handlers, not to one named after it")]
    [DataRow(new[] { "ExampleView.AddCheck", "Set ExampleView.Chk = Nothing", "ExampleView.Controls(\"Dyn1\").Value = True", "Debug.Print ExampleView.Controls(\"Dyn1\").Value" },
        new[] { "True" }, DisplayName = "C03 nothing to handle them once the variable lets go")]
    [DataRow(new[] { "ExampleView.AddCheck", "Debug.Print TypeName(ExampleView.Controls(\"Dyn1\"))", "Debug.Print ExampleView.Controls(\"Dyn1\") Is ExampleView.Chk" },
        new[] { "CheckBox", "True" }, DisplayName = "C04 the same object, by its name")]
    [DataRow(new[] { "ExampleView.AddCheck", "ExampleView.Controls.Remove \"Dyn1\"", "Debug.Print Err.Number", "Debug.Print ExampleView.Controls.Count",
        "Debug.Print ExampleView.Chk Is Nothing", "Debug.Print ExampleView.Chk.Name", "Debug.Print Err.Number" },
        new[] { "0", "13", "False", "-2147418113" }, DisplayName = "C05 Remove, and the object it leaves")]
    [DataRow(new[] { "ExampleView.Controls.Remove \"AmountBox\"", "Debug.Print Err.Number", "Debug.Print ExampleView.Controls.Count" },
        new[] { "444", "13" }, DisplayName = "C06 a control of the designer's cannot be removed")]
    [DataRow(new[] { "ExampleView.Controls.Remove 0", "Debug.Print Err.Number", "Debug.Print ExampleView.Controls.Count" },
        new[] { "444", "13" }, DisplayName = "C10 by its position either")]
    [DataRow(new[] { "ExampleView.AddCheck", "ExampleView.AddCheck", "Debug.Print Err.Number", "Debug.Print ExampleView.Controls.Count" },
        new[] { "0", "15" }, DisplayName = "C07 a name a control has is no error")]
    [DataRow(new[] { "ExampleView.Controls.Add \"Forms.Nope.1\"", "Debug.Print Err.Number" }, new[] { "-2147221005" }, DisplayName = "C08 a class that is not registered")]
    [DataRow(new[] { "Dim c As Object", "Set c = ExampleView.Controls.Add(\"Forms.CheckBox.1\")", "Debug.Print c.Name", "Set c = ExampleView.Controls.Add(\"Forms.CheckBox.1\")", "Debug.Print c.Name" },
        new[] { "CheckBox1", "CheckBox2" }, DisplayName = "C09 named after its class")]
    [DataRow(new[] { "Dim c As Object", "Set c = ExampleView.Controls.Add(\"Forms.CommandButton.1\", \"Go\")", "Debug.Print TypeName(c)", "Debug.Print ExampleView.Controls(\"Go\").Name",
        "ExampleView.Controls.Remove \"Go\"", "Debug.Print ExampleView.Controls.Count" }, new[] { "CommandButton", "Go", "13" }, DisplayName = "C11 any class of Microsoft Forms")]
    [DataRow(new[] { "ExampleView.AddCheck", "Unload ExampleView", "Debug.Print ExampleView.Controls.Count" }, new[] { "13" }, DisplayName = "C12 gone with the form")]
    [DataRow(new[] { "Debug.Print ExampleView.AmountBox.Left > 0", "ExampleView.AmountBox.Left = 3", "Debug.Print ExampleView.AmountBox.Left", "Debug.Print Err.Number" },
        new[] { "True", "3", "0" }, DisplayName = "the extender's members of a designer's control, which its class does not declare")]
    [DataRow(new[] { "Debug.Print TypeOf ExampleView.AmountBox Is MSForms.Control", "Debug.Print TypeOf ExampleView.AmountBox Is MSForms.TextBox",
        "Debug.Print TypeOf ExampleView.AmountBox Is MSForms.ComboBox", "Debug.Print TypeOf ExampleView.Controls(0) Is MSForms.Control" },
        new[] { "True", "True", "False", "True" }, DisplayName = "M04 M08 a control is the extender it is wrapped in, and its own class")]
    [DataRow(new[] { "Dim c As MSForms.Control", "Set c = ExampleView.AmountBox", "Debug.Print TypeName(c)", "Debug.Print c Is ExampleView.AmountBox", "Debug.Print c.Name",
        "Dim t As MSForms.TextBox", "Set t = c", "Debug.Print TypeName(t)" },
        new[] { "TextBox", "True", "AmountBox", "TextBox" }, DisplayName = "M05 M07 a variable of the extender's class holds the control")]
    [DataRow(new[] { "Dim c As MSForms.Control", "Set c = ExampleView", "Debug.Print Err.Number", "Dim d As Object", "Set d = CreateObject(\"Scripting.Dictionary\")",
        "Debug.Print TypeOf d Is MSForms.Control" },
        new[] { "13", "False" }, DisplayName = "M06 M09 a form, and an object of another library, are none")]
    public async Task ControlsAddedAtRunTime_AreWhatMSVBAMakesThem(string[] body, string[] printed)
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;

        var actual = await RunWithCodeAsync(world, Dynamic, ["On Error Resume Next", .. body]);
        CollectionAssert.AreEqual(printed, actual, string.Join(" | ", actual));
    }

    // the handlers of the extender's events of two controls, AmountBox (the first in the tab order) and ComboBox1, as the Excel oracle's FocusForm has them; and Go,
    // which moves the focus as the program does.
    private static readonly string[] Focus =
    [
        "Public CancelExit As Boolean",
        "Public CancelUpdate As Boolean",
        "Private Sub UserForm_Initialize()", "    Debug.Print \"Initialize\"", "End Sub",
        "Private Sub UserForm_Activate()", "    Debug.Print \"Activate\"", "End Sub",
        "Private Sub UserForm_QueryClose(Cancel As Integer, CloseMode As Integer)", "    Debug.Print \"QueryClose\"; CloseMode", "End Sub",
        "Private Sub UserForm_Terminate()", "    Debug.Print \"Terminate\"", "End Sub",
        "Private Sub AmountBox_Enter()", "    Debug.Print \"AmountEnter\"", "End Sub",
        "Private Sub AmountBox_Exit(ByVal Cancel As MSForms.ReturnBoolean)", "    Debug.Print \"AmountExit\"", "    If CancelExit Then Cancel = True", "End Sub",
        "Private Sub AmountBox_BeforeUpdate(ByVal Cancel As MSForms.ReturnBoolean)", "    Debug.Print \"AmountBeforeUpdate\"", "    If CancelUpdate Then Cancel = True", "End Sub",
        "Private Sub AmountBox_AfterUpdate()", "    Debug.Print \"AmountAfterUpdate\"", "End Sub",
        "Private Sub AmountBox_Change()", "    Debug.Print \"AmountChange\"", "End Sub",
        "Private Sub ComboBox1_Enter()", "    Debug.Print \"ComboEnter\"", "End Sub",
        "Private Sub ComboBox1_Exit(ByVal Cancel As MSForms.ReturnBoolean)", "    Debug.Print \"ComboExit\"", "End Sub",
        "Public Sub Go(ByVal name As String)", "    Me.Controls(name).SetFocus", "End Sub",
    ];

    // oracle\s41 E01-E17: the extender's events as the focus moves, in the order Excel raises them - by Microsoft Forms itself, and by the host when the form is unloaded.
    [TestMethod]
    [DataRow(new[] { "ExampleView.Show vbModeless", "Debug.Print ExampleView.ActiveControl.Name", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "AmountBox", "QueryClose 1", "AmountExit", "Terminate" },
        DisplayName = "E01 Show enters the first control before Activate, Unload exits it after QueryClose")]
    [DataRow(new[] { "ExampleView.Show vbModeless", "ExampleView.Go \"AmountBox\"", "ExampleView.Go \"ComboBox1\"", "ExampleView.Go \"AmountBox\"", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "AmountExit", "ComboEnter", "ComboExit", "AmountEnter", "QueryClose 1", "AmountExit", "Terminate" },
        DisplayName = "E02 a move exits one control and enters the next")]
    [DataRow(new[] { "ExampleView.Show vbModeless", "ExampleView.Go \"AmountBox\"", "ExampleView.AmountBox.Text = \"5\"", "ExampleView.Go \"ComboBox1\"", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "AmountChange", "AmountBeforeUpdate", "AmountAfterUpdate", "AmountExit", "ComboEnter", "QueryClose 1",
            "ComboExit", "Terminate" },
        DisplayName = "E03 a changed control is updated before it is exited")]
    [DataRow(new[] { "ExampleView.CancelExit = True", "ExampleView.Show vbModeless", "ExampleView.Go \"ComboBox1\"", "Debug.Print ExampleView.ActiveControl.Name",
        "ExampleView.CancelExit = False", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "AmountExit", "AmountBox", "QueryClose 1", "AmountExit", "Terminate" },
        DisplayName = "E04 Exit cancelled keeps the focus")]
    [DataRow(new[] { "ExampleView.CancelUpdate = True", "ExampleView.Show vbModeless", "ExampleView.AmountBox.Text = \"5\"", "ExampleView.Go \"ComboBox1\"",
        "Debug.Print ExampleView.ActiveControl.Name", "Debug.Print ExampleView.AmountBox.Text", "ExampleView.CancelUpdate = False", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "AmountChange", "AmountBeforeUpdate", "AmountBox", "5", "QueryClose 1", "AmountBeforeUpdate", "AmountAfterUpdate",
            "AmountExit", "Terminate" },
        DisplayName = "E05 BeforeUpdate cancelled keeps the focus, and the update pending")]
    [DataRow(new[] { "ExampleView.Show vbModeless", "ExampleView.Hide", "ExampleView.Show vbModeless", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "Activate", "QueryClose 1", "AmountExit", "Terminate" },
        DisplayName = "E07 Hide and Show again enter and exit nothing")]
    [DataRow(new[] { "Load ExampleView", "ExampleView.Go \"AmountBox\"", "ExampleView.Go \"ComboBox1\"", "Debug.Print Err.Number", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "AmountExit", "ComboEnter", "0", "QueryClose 1", "ComboExit", "Terminate" },
        DisplayName = "E08 a form that is not shown")]
    [DataRow(new[] { "ExampleView.CancelExit = True", "ExampleView.Show vbModeless", "Unload ExampleView", "Debug.Print Err.Number" },
        new[] { "Initialize", "AmountEnter", "Activate", "QueryClose 1", "AmountExit", "Terminate", "0" },
        DisplayName = "E10 Exit cancelled at Unload changes nothing")]
    [DataRow(new[] { "ExampleView.CancelUpdate = True", "ExampleView.Show vbModeless", "ExampleView.AmountBox.Text = \"5\"", "Unload ExampleView", "Debug.Print Err.Number" },
        new[] { "Initialize", "AmountEnter", "Activate", "AmountChange", "QueryClose 1", "AmountBeforeUpdate", "AmountBeforeUpdate", "AmountExit", "Terminate", "0" },
        DisplayName = "E11 BeforeUpdate cancelled at Unload is asked once more, then not made")]
    [DataRow(new[] { "ExampleView.Show vbModeless", "ExampleView.Hide", "ExampleView.Go \"ComboBox1\"", "Debug.Print ExampleView.ActiveControl.Name", "Debug.Print \"shown\"",
        "ExampleView.Show vbModeless", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "AmountExit", "ComboEnter", "ComboBox1", "shown", "Activate", "QueryClose 1", "ComboExit", "Terminate" },
        DisplayName = "E13 the focus moves on a hidden form too")]
    [DataRow(new[] { "ExampleView.Show vbModeless", "ExampleView.Go \"ComboBox1\"", "ExampleView.AmountBox.Text = \"7\"", "DoEvents", "ExampleView.Go \"AmountBox\"",
        "ExampleView.Go \"ComboBox1\"", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "AmountExit", "ComboEnter", "AmountChange", "ComboExit", "AmountEnter", "AmountExit", "ComboEnter", "QueryClose 1",
            "ComboExit", "Terminate" },
        DisplayName = "E14 a change while the focus is elsewhere, pumped at once, is no update")]
    [DataRow(new[] { "Load ExampleView", "ExampleView.AmountBox.Text = \"1\"", "DoEvents", "Debug.Print \"pumped\"", "ExampleView.Show vbModeless", "Unload ExampleView" },
        new[] { "Initialize", "AmountChange", "pumped", "AmountEnter", "Activate", "QueryClose 1", "AmountExit", "Terminate" },
        DisplayName = "E15 nothing is entered before Show, and a change pumped before it is no update")]
    [DataRow(new[] { "ExampleView.CancelExit = True", "ExampleView.Show vbModeless", "ExampleView.Controls(\"ComboBox1\").SetFocus", "Debug.Print Err.Number",
        "Debug.Print Err.Description", "Err.Clear", "Debug.Print ExampleView.ActiveControl.Name", "ExampleView.CancelExit = False", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "AmountExit", "-2147467259", "Unspecified error.", "AmountBox", "QueryClose 1", "AmountExit", "Terminate" },
        DisplayName = "E16 the SetFocus whose move Exit cancels fails")]
    [DataRow(new[] { "ExampleView.CancelUpdate = True", "ExampleView.Show vbModeless", "ExampleView.AmountBox.Text = \"5\"", "ExampleView.Controls(\"ComboBox1\").SetFocus",
        "Debug.Print Err.Number", "Debug.Print Err.Description", "Err.Clear", "ExampleView.CancelUpdate = False", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "AmountChange", "AmountBeforeUpdate", "-2147467259", "Unspecified error.", "QueryClose 1", "AmountBeforeUpdate",
            "AmountAfterUpdate", "AmountExit", "Terminate" },
        DisplayName = "E17 the SetFocus whose move BeforeUpdate cancels fails")]
    public async Task TheExtendersEvents_FollowTheFocus_AsMSVBARaisesThem(string[] body, string[] printed)
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;

        var actual = await RunWithCodeAsync(world, Focus, ["On Error Resume Next", .. body]);
        CollectionAssert.AreEqual(printed, actual, string.Join(" | ", actual));
    }

    // oracle\s41 E09, E12: the same changes as E14, E15, with nothing pumped between the program's statements - which MS-VBA does not do until DoEvents, a modal Show
    // or the program's end, and Microsoft Forms then counts the change as the focused control's.
    [TestMethod]
    [Ignore("The apartment dispatches the forms' messages between every call the program makes; MS-VBA dispatches none while a program runs, so a change Microsoft Forms posts is the next focused control's update there (E09, E12, against E14, E15). Pumping only when the program waits is a design ruling to ask for.")]
    [DataRow(new[] { "ExampleView.Show vbModeless", "ExampleView.Go \"ComboBox1\"", "ExampleView.AmountBox.Text = \"7\"", "ExampleView.Go \"AmountBox\"", "ExampleView.Go \"ComboBox1\"",
        "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "AmountExit", "ComboEnter", "AmountChange", "ComboExit", "AmountEnter", "AmountBeforeUpdate",
            "AmountAfterUpdate", "AmountExit", "ComboEnter", "QueryClose 1", "ComboExit", "Terminate" },
        DisplayName = "E09 a change while the focus is elsewhere is updated when the control is next exited")]
    [DataRow(new[] { "Load ExampleView", "Debug.Print \"afterLoad\"", "ExampleView.AmountBox.Text = \"1\"", "Debug.Print \"afterText\"", "ExampleView.Show vbModeless",
        "Debug.Print \"afterShow\"", "Unload ExampleView" },
        new[] { "Initialize", "afterLoad", "AmountChange", "afterText", "AmountEnter", "AmountBeforeUpdate", "AmountAfterUpdate", "Activate", "afterShow", "QueryClose 1",
            "AmountExit", "Terminate" },
        DisplayName = "E12 a change before Show, not pumped, is updated when Show enters the control")]
    public async Task TheExtendersEvents_OfAChangeNothingPumped_AreMSVBAs(string[] body, string[] printed)
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;

        var actual = await RunWithCodeAsync(world, Focus, ["On Error Resume Next", .. body]);
        CollectionAssert.AreEqual(printed, actual, string.Join(" | ", actual));
    }

    // oracle\s41 E01: the Cancel argument is an object of Microsoft Forms' that says it is an IReturnBoolean, and has no class to say otherwise.
    [TestMethod]
    [Ignore("TypeName of a server's object names the class the runtime binds it as (the parameter's ReturnBoolean), or the class whose default interface the object answers to; MS-VBA names the interface an object without class information says it is (IReturnBoolean). Follow-up.")]
    public async Task TheCancelArgument_IsNamedAsTheObjectSaysItIs()
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;
        string[] code = ["Private Sub AmountBox_Exit(ByVal Cancel As MSForms.ReturnBoolean)", "    Debug.Print TypeName(Cancel)", "End Sub"];

        CollectionAssert.AreEqual(new[] { "IReturnBoolean" }, await RunWithCodeAsync(world, code, "ExampleView.Show vbModeless", "Unload ExampleView"));
    }

    // the Cancel argument is Microsoft Forms' object, in the external host, which the handler's assignment reaches across the process; the SetFocus whose move is
    // cancelled fails, as Excel's does (oracle\s41 E16, E17).
    [TestMethod]
    public async Task TheExtendersEvents_ThroughAnExternalHost_AreCancelled()
    {
        RequireMicrosoftForms();
        using var host = ExternalHosts.New();

        var actual = await RunWithCodeAsync(ExternalHosts.WorldOf(host), Focus, "On Error Resume Next",
            "ExampleView.CancelExit = True", "ExampleView.Show vbModeless", "ExampleView.Go \"ComboBox1\"", "Debug.Print Err.Number", "Err.Clear",
            "Debug.Print ExampleView.ActiveControl.Name", "ExampleView.CancelExit = False",
            "ExampleView.CancelUpdate = True", "ExampleView.AmountBox.Text = \"5\"", "ExampleView.Go \"ComboBox1\"", "Debug.Print Err.Number", "Err.Clear",
            "Debug.Print ExampleView.ActiveControl.Name", "ExampleView.CancelUpdate = False",
            "ExampleView.Go \"ComboBox1\"", "Debug.Print Err.Number", "Unload ExampleView");

        CollectionAssert.AreEqual(new[]
        {
            "Initialize", "AmountEnter", "Activate", "AmountExit", "-2147467259", "AmountBox", "AmountChange", "AmountBeforeUpdate", "-2147467259", "AmountBox",
            "AmountBeforeUpdate", "AmountAfterUpdate", "AmountExit", "ComboEnter", "0", "QueryClose 1", "ComboExit", "Terminate",
        }, actual, string.Join(" | ", actual));
    }

    // a sink a program connects to a control itself, as MVVM's ControlEventsPunk does: shlwapi's ConnectToConnectionPoint, IDispatch, and members by the dispatch
    // identifiers of VBA's extender (and Change's, 2). Kind 0 connects IDispatch's point, 1 Microsoft Forms' ControlEvents'.
    private static readonly (string Name, string Source) Punk = ("Punk", ModuleWorkspace.ClassModule("Punk",
        "Option Explicit",
        "Private Type GUID", "    Data1 As Long", "    Data2 As Integer", "    Data3 As Integer", "    Data4(0 To 7) As Byte", "End Type",
        "Private Declare PtrSafe Function ConnectToConnectionPoint Lib \"shlwapi\" Alias \"#168\" (ByVal Punk As stdole.IUnknown, ByRef riidEvent As GUID, ByVal fConnect As Long, ByVal PunkTarget As stdole.IUnknown, ByRef pdwCookie As Long, Optional ByVal ppcpOut As LongPtr) As Long",
        "Public Tag As String",
        "Public CancelExit As Boolean",
        "Public CancelUpdate As Boolean",
        "Private Target As Object",
        "Private Cookie As Long",
        "Private Iid As GUID",
        "Public Function Connect(ByVal t As Object, ByVal kind As Long) As Long",
        "    Set Target = t",
        "    If kind = 0 Then",
        "        Iid.Data1 = &H20400: Iid.Data4(0) = &HC0: Iid.Data4(7) = &H46",
        "    Else",
        "        Iid.Data1 = &H9A4BBF53: Iid.Data2 = &H4E46: Iid.Data3 = &H101B: Iid.Data4(0) = &H8B: Iid.Data4(1) = &HBD: Iid.Data4(3) = &HAA: Iid.Data4(5) = &H3E: Iid.Data4(6) = &H3B: Iid.Data4(7) = &H29",
        "    End If",
        "    Connect = ConnectToConnectionPoint(Me, Iid, 1, t, Cookie, 0)",
        "End Function",
        "Public Function Disconnect() As Long", "    Disconnect = ConnectToConnectionPoint(Me, Iid, 0, Target, Cookie, 0)", "End Function",
        "Public Property Get CookieValue() As Long", "    CookieValue = Cookie", "End Property",
        "Public Sub OnAfterUpdate()", "Attribute OnAfterUpdate.VB_UserMemId = -2147384832", "    Debug.Print Tag & \"AfterUpdate\"", "End Sub",
        "Public Sub OnBeforeUpdate(ByVal Cancel As MSForms.IReturnBoolean)", "Attribute OnBeforeUpdate.VB_UserMemId = -2147384831", "    Debug.Print Tag & \"BeforeUpdate\"",
        "    If CancelUpdate Then Cancel.Value = True", "End Sub",
        "Public Sub OnEnter()", "Attribute OnEnter.VB_UserMemId = -2147384830", "    Debug.Print Tag & \"Enter\"", "End Sub",
        "Public Sub OnExit(ByVal Cancel As MSForms.IReturnBoolean)", "Attribute OnExit.VB_UserMemId = -2147384829", "    Debug.Print Tag & \"Exit\"",
        "    If CancelExit Then Cancel.Value = True", "End Sub",
        "Public Sub OnEnter82()", "Attribute OnEnter82.VB_UserMemId = -2147384318", "    Debug.Print Tag & \"Enter82\"", "End Sub",
        "Public Sub OnChange()", "Attribute OnChange.VB_UserMemId = 2", "    Debug.Print Tag & \"Change\"", "End Sub"));

    // oracle\s41 P01-P06, Q01-Q04: what a connected sink is called for, and when, and what ConnectToConnectionPoint returns.
    [TestMethod]
    [DataRow(new[] { "Dim p As New Punk", "p.Tag = \"P:\"", "Debug.Print p.Connect(ExampleView.AmountBox, 0)", "Debug.Print p.CookieValue <> 0", "ExampleView.Show vbModeless",
        "ExampleView.Go \"ComboBox1\"", "ExampleView.Go \"AmountBox\"", "ExampleView.AmountBox.Text = \"5\"", "ExampleView.Go \"ComboBox1\"", "Debug.Print p.Disconnect",
        "ExampleView.Go \"AmountBox\"", "Unload ExampleView" },
        new[] { "Initialize", "0", "True", "AmountEnter", "P:Enter", "Activate", "AmountExit", "P:Exit", "ComboEnter", "ComboExit", "AmountEnter", "P:Enter", "AmountChange",
            "P:Change", "AmountBeforeUpdate", "P:BeforeUpdate", "AmountAfterUpdate", "P:AfterUpdate", "AmountExit", "P:Exit", "ComboEnter", "0", "ComboExit", "AmountEnter",
            "QueryClose 1", "AmountExit", "Terminate" },
        DisplayName = "P01 the extender's events and the control's own, after the form's handler, until it is disconnected")]
    [DataRow(new[] { "Dim p As New Punk", "p.Tag = \"P:\"", "Debug.Print p.Connect(ExampleView.AmountBox, 1)", "Debug.Print p.CookieValue", "ExampleView.Show vbModeless",
        "ExampleView.Go \"ComboBox1\"", "Unload ExampleView" },
        new[] { "Initialize", "-2147467262", "0", "AmountEnter", "Activate", "AmountExit", "ComboEnter", "QueryClose 1", "ComboExit", "Terminate" },
        DisplayName = "P02 Q02 an interface other than IDispatch is E_NOINTERFACE")]
    [DataRow(new[] { "Dim p As New Punk", "p.Tag = \"P:\"", "Debug.Print p.Connect(ExampleView.Controls(\"AmountBox\"), 0)", "p.CancelExit = True", "ExampleView.Show vbModeless",
        "ExampleView.Go \"ComboBox1\"", "Debug.Print Err.Number", "Err.Clear", "Debug.Print ExampleView.ActiveControl.Name", "p.CancelExit = False", "Unload ExampleView" },
        new[] { "Initialize", "0", "AmountEnter", "P:Enter", "Activate", "AmountExit", "P:Exit", "-2147467259", "AmountBox", "QueryClose 1", "AmountExit", "P:Exit", "Terminate" },
        DisplayName = "P03 the sink cancels Exit")]
    [DataRow(new[] { "Dim p As New Punk", "p.Tag = \"P:\"", "Debug.Print p.Connect(ExampleView.AmountBox, 0)", "p.CancelUpdate = True", "ExampleView.Show vbModeless",
        "ExampleView.AmountBox.Text = \"5\"", "ExampleView.Go \"ComboBox1\"", "Debug.Print Err.Number", "Err.Clear", "p.CancelUpdate = False", "Unload ExampleView" },
        new[] { "Initialize", "0", "AmountEnter", "P:Enter", "Activate", "AmountChange", "P:Change", "AmountBeforeUpdate", "P:BeforeUpdate", "-2147467259", "QueryClose 1",
            "AmountBeforeUpdate", "P:BeforeUpdate", "AmountAfterUpdate", "P:AfterUpdate", "AmountExit", "P:Exit", "Terminate" },
        DisplayName = "P04 the sink cancels BeforeUpdate")]
    [DataRow(new[] { "Dim p As New Punk", "Dim q As New Punk", "p.Tag = \"P:\"", "q.Tag = \"Q:\"", "Debug.Print p.Connect(ExampleView.AmountBox, 0)",
        "Debug.Print q.Connect(ExampleView.AmountBox, 0)", "Debug.Print p.CookieValue <> q.CookieValue", "ExampleView.Show vbModeless", "ExampleView.Go \"ComboBox1\"",
        "Unload ExampleView" },
        new[] { "Initialize", "0", "0", "True", "AmountEnter", "P:Enter", "Q:Enter", "Activate", "AmountExit", "P:Exit", "Q:Exit", "ComboEnter", "QueryClose 1", "ComboExit",
            "Terminate" },
        DisplayName = "P05 Q04 two sinks, in the order they were connected")]
    [DataRow(new[] { "Dim p As New Punk", "p.Tag = \"P:\"", "ExampleView.Show vbModeless", "Debug.Print p.Connect(ExampleView.AmountBox, 0)", "ExampleView.Go \"ComboBox1\"",
        "Set p = Nothing", "ExampleView.Go \"AmountBox\"", "Unload ExampleView" },
        new[] { "Initialize", "AmountEnter", "Activate", "0", "AmountExit", "P:Exit", "ComboEnter", "ComboExit", "AmountEnter", "P:Enter", "QueryClose 1", "AmountExit", "P:Exit",
            "Terminate" },
        DisplayName = "P06 the connection holds the sink the program let go of")]
    [DataRow(new[] { "Dim p As New Punk", "Dim c As Collection", "Set c = New Collection", "Debug.Print p.Connect(c, 0)", "Debug.Print Err.Number", "Debug.Print p.CookieValue" },
        new[] { "-2147467262", "0", "0" },
        DisplayName = "Q03 an object that has no connection points is E_NOINTERFACE")]
    [DataRow(new[] { "Dim p As New Punk", "Debug.Print p.Connect(ExampleView.AmountBox, 0)", "Debug.Print p.Disconnect", "Debug.Print p.Disconnect", "Unload ExampleView" },
        new[] { "Initialize", "0", "0", "0", "QueryClose 1", "Terminate" },
        DisplayName = "Q01 disconnecting, twice")]
    public async Task ASinkConnectedToAControl_IsCalledAsMSVBACallsIt(string[] body, string[] printed)
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;

        var actual = await ModuleWorkspace.RunAsync(
            [Punk], Program(["On Error Resume Next", .. body]), new ModuleWorkspace.WorkspaceLibraries(["MSForms", "stdole"], Shipped, Outside: world), [ExampleView(Focus)]);
        CollectionAssert.AreEqual(printed, actual, string.Join(" | ", actual));
    }

    // the dispatch identifier an event is raised with crosses from the external host with it, and the sink's Cancel is Microsoft Forms' object there.
    [TestMethod]
    public async Task ASinkConnectedToAControl_ThroughAnExternalHost_IsCalledByTheIdentifierTheEventIsRaisedWith()
    {
        RequireMicrosoftForms();
        using var host = ExternalHosts.New();

        var actual = await ModuleWorkspace.RunAsync(
            [Punk],
            Program("On Error Resume Next", "Dim p As New Punk", "p.Tag = \"P:\"", "Debug.Print p.Connect(ExampleView.AmountBox, 0)", "p.CancelExit = True",
                "ExampleView.Show vbModeless", "ExampleView.AmountBox.Text = \"5\"", "ExampleView.Go \"ComboBox1\"", "Debug.Print Err.Number", "Err.Clear", "p.CancelExit = False",
                "Debug.Print p.Disconnect", "Unload ExampleView"),
            new ModuleWorkspace.WorkspaceLibraries(["MSForms", "stdole"], Shipped, Outside: ExternalHosts.WorldOf(host)), [ExampleView(Focus)]);

        CollectionAssert.AreEqual(new[]
        {
            "Initialize", "0", "AmountEnter", "P:Enter", "Activate", "AmountChange", "P:Change", "AmountBeforeUpdate", "P:BeforeUpdate", "AmountAfterUpdate", "P:AfterUpdate",
            "AmountExit", "P:Exit", "-2147467259", "0", "QueryClose 1", "AmountExit", "Terminate",
        }, actual, string.Join(" | ", actual));
    }

    [TestMethod]
    public async Task AMemberOfTheFormObject_ThatIsNotHostedYet_SaysSo()
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;

        var printed = await RunAsync(world, "On Error Resume Next", "ExampleView.Move 10", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "438" }, printed);
    }

    private static async Task<IntPtr> WindowOfAsync(string caption)
    {
        var started = DateTime.UtcNow;
        while (FindWindow("RDCore.UserForm", caption) is var window && window == IntPtr.Zero)
        {
            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(20))
            {
                Assert.Fail("The form was not shown in time.");
            }

            await Task.Delay(50);
        }

        return FindWindow("RDCore.UserForm", caption);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string title);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rect rectangle);
}
