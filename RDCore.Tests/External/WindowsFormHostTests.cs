using RDCore.External.Automation;
using RDCore.External.Forms;
using RDCore.External.Windows.Automation;
using RDCore.External.Windows.Forms;
using RDCore.SDK.Workspace.Forms;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace RDCore.Tests.External;

/// <summary>
/// The Windows form host: a VBA UserForm's designer state loaded by Microsoft Forms itself, in a window of the host's own, on the apartment the automation server's
/// objects live on; shown modeless and modal, hidden and destroyed. Inconclusive on a machine without Microsoft Forms, as the Excel tests are without Excel.
/// </summary>
// one at a time: each test has an apartment of its own, and Microsoft Forms, loaded in two apartments at once, fails to load a form now and then
// (E_UNEXPECTED) - which an external host, whose forms all live on its one apartment, never asks of it.
[TestClass]
[TestCategory("External host")]
[TestCategory("MSForms")]
[DoNotParallelize]
public sealed class WindowsFormHostTests
{
    private const uint WM_CLOSE = 0x0010;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private sealed class Hosting : IDisposable
    {
        public ComApartment Apartment { get; } = new();

        public ComAutomationServer Automation { get; }

        public WindowsFormHost Forms { get; }

        public Hosting()
        {
            Automation = new ComAutomationServer(Apartment);
            Forms = new WindowsFormHost(Apartment);
        }

        public object? Get(object target, string member, params object?[] arguments)
            => Automation.Invoke(target, member, AutomationInvocation.Get, arguments, new bool[arguments.Length], CultureInfo.InvariantCulture);

        public void Dispose() => Apartment.Dispose();
    }

    private static Hosting Host()
    {
        if (!OperatingSystem.IsWindows() || Type.GetTypeFromProgID("Forms.Form.1") is null)
        {
            Assert.Inconclusive("Microsoft Forms 2.0 is not installed on this machine.");
        }

        return new Hosting();
    }

    private static byte[] Fixture(string fileName)
    {
        var assembly = typeof(WindowsFormHostTests).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith($".Fixtures.{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // MVVM's ExampleView, as the VBE exported it: 13 controls, captioned "ExampleView" by its designer block - or by a caption of the test's own, which tells its
    // window apart from those of the tests that run alongside it.
    private static FormDefinition ExampleView(string? caption = null)
    {
        var source = Encoding.UTF8.GetString(Fixture("ExampleView.frm"));
        var frx = Fixture("ExampleView.frx");
        return new("ExampleView", FormDesigner.StateOf(source, frx)) { Caption = caption ?? FormDesigner.Read(source, frx)!.Caption };
    }

    private static string Unique() => $"ExampleView {Guid.NewGuid():N}";

    [TestMethod]
    public void AForm_IsLoadedFromItsDesignersState_AndIsAnObjectOfTheAutomationServer()
    {
        using var host = Host();
        var form = host.Forms.Create(ExampleView());

        Assert.AreEqual("ExampleView", host.Forms.GetCaption(form), "the caption is the designer block's");
        var controls = host.Get(form, "Controls")!;
        Assert.AreEqual(13, host.Get(controls, "Count"));
        Assert.AreEqual("AmountBox", host.Get(host.Get(form, "AmountBox")!, "Name"), "a control is a member of its form, by its name");
        Assert.IsFalse(host.Forms.IsShown(form), "a form that is created is not shown");

        host.Forms.Destroy(form);
    }

    [TestMethod]
    public void AFormWithNoDesignerState_IsAnEmptyForm()
    {
        using var host = Host();
        var form = host.Forms.Create(new FormDefinition("Empty", ReadOnlyMemory<byte>.Empty));

        Assert.AreEqual(0, host.Get(host.Get(form, "Controls")!, "Count"));
        host.Forms.Destroy(form);
    }

    [TestMethod]
    public void AModelessForm_IsShownAtOnce_AndHidden()
    {
        using var host = Host();
        var caption = Unique();
        var form = host.Forms.Create(ExampleView(caption));

        host.Forms.Show(form, modal: false);
        Assert.IsTrue(host.Forms.IsShown(form));
        Assert.AreNotEqual(IntPtr.Zero, WindowOf(caption), "the window's caption is the form's");

        host.Forms.SetCaption(form, caption + " again");
        Assert.AreEqual(caption + " again", host.Forms.GetCaption(form));
        Assert.AreNotEqual(IntPtr.Zero, WindowOf(caption + " again"));

        host.Forms.Hide(form);
        Assert.IsFalse(host.Forms.IsShown(form));
        host.Forms.Destroy(form);
    }

    [TestMethod]
    public async Task AModalForm_WaitsUntilItIsHidden()
    {
        using var host = Host();
        var form = host.Forms.Create(ExampleView());

        var showing = Task.Run(() => host.Forms.Show(form, modal: true));
        await WaitUntilAsync(() => host.Forms.IsShown(form));
        await Task.Delay(200);
        Assert.IsFalse(showing.IsCompleted, "a modal Show waits");

        // what is asked of the apartment while the form waits is done: a Hide, as a handler of the form's would ask it.
        host.Forms.Hide(form);
        await showing.WaitAsync(Patience);
        Assert.IsFalse(host.Forms.IsShown(form));
        host.Forms.Destroy(form);
    }

    [TestMethod]
    public async Task AModalForm_WhoseCloseButtonIsClicked_IsHidden_AndStopsWaiting()
    {
        using var host = Host();
        var caption = Unique();
        var form = host.Forms.Create(ExampleView(caption));

        var showing = Task.Run(() => host.Forms.Show(form, modal: true));
        await WaitUntilAsync(() => host.Forms.IsShown(form));

        Assert.IsTrue(PostMessage(WindowOf(caption), WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        await showing.WaitAsync(Patience);
        Assert.IsFalse(host.Forms.IsShown(form));
        host.Forms.Destroy(form);
    }

    [TestMethod]
    public void TheWindow_IsTheFormsSize_AtItsDpi()
    {
        using var host = Host();
        var caption = Unique();
        var form = host.Forms.Create(ExampleView(caption));
        host.Forms.Show(form, modal: false);

        var window = WindowOf(caption);
        var dpi = host.Apartment.Invoke(() => GetDpiForWindow(window));
        _ = GetClientRect(window, out var client);

        // InsideWidth and InsideHeight are the form's client area, in points.
        var width = Convert.ToDouble(host.Get(form, "InsideWidth"), CultureInfo.InvariantCulture);
        var height = Convert.ToDouble(host.Get(form, "InsideHeight"), CultureInfo.InvariantCulture);
        Assert.AreEqual(width * dpi / 72, client.Right - client.Left, 2, $"at {dpi} dpi");
        Assert.AreEqual(height * dpi / 72, client.Bottom - client.Top, 2, $"at {dpi} dpi");
        host.Forms.Destroy(form);
    }

    // the active control is one the program may hold too: the automation server releasing the program's reference to it left the site's cut off, and the next
    // activation of the window raised InvalidComObjectException in the window procedure - which takes the process down (found through the external host).
    [TestMethod]
    public void AControlTheProgramLetsGoOf_IsStillTheFormsActiveOne()
    {
        using var host = Host();
        var caption = Unique();
        var form = host.Forms.Create(ExampleView(caption));
        host.Forms.Show(form, modal: false);

        var control = host.Get(form, "AmountBox")!;
        _ = host.Automation.Invoke(control, "SetFocus", AutomationInvocation.Method, [], [], CultureInfo.InvariantCulture);
        host.Automation.Release(control);

        // the window is activated again, which tells the active control.
        host.Forms.Hide(form);
        host.Forms.Show(form, modal: false);
        Assert.AreEqual("AmountBox", host.Get(host.Get(form, "ActiveControl")!, "Name"));
        host.Forms.Destroy(form);
    }

    [TestMethod]
    public void AFormThatIsDestroyed_IsNoFormOfTheHost()
    {
        using var host = Host();
        var form = host.Forms.Create(ExampleView());
        host.Forms.Show(form, modal: false);

        host.Forms.Destroy(form);
        host.Forms.Destroy(form);

        var failure = Assert.ThrowsExactly<AutomationException>(() => host.Forms.Show(form, modal: false));
        StringAssert.Contains(failure.Message, "not a form");
    }

    [TestMethod]
    public void AFormHost_OnAPlatformWithNone_CreatesNoForm()
    {
        Assert.IsFalse(UnavailableFormHost.Instance.IsAvailable);
        var failure = Assert.ThrowsExactly<AutomationException>(() => UnavailableFormHost.Instance.Create(new FormDefinition("UserForm1", ReadOnlyMemory<byte>.Empty)));
        Assert.AreEqual(unchecked((int)0x80040154), failure.HResult);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - started > Patience)
            {
                Assert.Fail("The form was not shown in time.");
            }

            await Task.Delay(20);
        }
    }

    // the host's window of a form, by its caption: one of the host's form windows, and the only one with that caption.
    private static IntPtr WindowOf(string caption) => FindWindow("RDCore.UserForm", caption);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string title);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
