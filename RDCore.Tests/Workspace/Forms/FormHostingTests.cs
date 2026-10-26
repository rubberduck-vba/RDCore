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
    private static (string Name, string Source, FormDescriptor Designer) ExampleView()
    {
        var frm = Encoding.UTF8.GetString(Fixture("ExampleView.frm"));
        var lines = frm.Split("\r\n");
        var header = string.Join("\r\n", lines.Take(Array.FindLastIndex(lines, line => line.StartsWith("Attribute VB_", StringComparison.Ordinal)) + 1));
        return ("ExampleView", header + "\r\nOption Explicit\r\n", FormDesigner.Read(frm, Fixture("ExampleView.frx"))!);
    }

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
}
