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
    public async Task ControlsAddedAtRunTime_AreWhatMSVBAMakesThem(string[] body, string[] printed)
    {
        RequireMicrosoftForms();
        var (world, apartment) = InProcess();
        using var _ = apartment;

        var actual = await RunWithCodeAsync(world, Dynamic, ["On Error Resume Next", .. body]);
        CollectionAssert.AreEqual(printed, actual, string.Join(" | ", actual));
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
