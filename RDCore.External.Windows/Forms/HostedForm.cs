using RDCore.External.Forms;
using RDCore.External.Windows.Automation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RDCore.External.Windows.Forms;

/// <summary>
/// One form, hosted: the Microsoft Forms form, the window it is in, and the site between them. Everything here runs on the apartment.
/// </summary>
/// <remarks>
/// <para>
/// The form is sited before it is loaded, as it asks (<c>OLEMISC_SETCLIENTSITEFIRST</c>): it asks the site whether it runs while it is sited, and loads its
/// controls in run mode. It is then activated in place in the window, which is not shown until the program shows it.
/// </para>
/// <para>
/// The window is what VBA's form host adds to the form: a caption bar with a close button only, a client area the size of the form at the window's DPI - the
/// form's size is in points, and so a form looks the same size on every screen - and where the form starts up. Moved to a screen of another DPI, the window is
/// sized again (<c>WM_DPICHANGED</c>).
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class HostedForm
{
    private const string ClassName = "RDCore.UserForm";
    private const uint Style = Desktop.WS_CAPTION | Desktop.WS_SYSMENU | Desktop.WS_CLIPCHILDREN;
    private const uint ExtendedStyle = Desktop.WS_EX_DLGMODALFRAME;
    private const double HimetricPerInch = 2540;

    // the windows of the apartment's forms, by handle: what the window procedure answers for. A window's messages are dispatched on the thread that made it, so each
    // apartment has its own, and only its own thread reads or writes it.
    [ThreadStatic]
    private static Dictionary<IntPtr, HostedForm>? _windows;
    private static readonly Desktop.WindowProcedure Procedure = WindowProcedure;
    private static readonly Lazy<ushort> Registered = new(Register);

    private static Dictionary<IntPtr, HostedForm> Windows => _windows ??= [];

    // the form that was shown last, and is shown still: the active one, which another's Show deactivates.
    [ThreadStatic]
    private static HostedForm? _active;

    private readonly FormDefinition _definition;
    private readonly FormSite _site;
    private readonly ComApartment _apartment;
    private bool _shownBefore;

    private HostedForm(FormDefinition definition, object form, IntPtr window, ComApartment apartment)
    {
        _definition = definition;
        Form = form;
        Window = window;
        _site = new FormSite(window);
        _apartment = apartment;
    }

    /// <summary>The form: the automation object its own object model is.</summary>
    public object Form { get; }

    /// <summary>The window the form is in.</summary>
    public IntPtr Window { get; }

    /// <summary>Whether the form is destroyed.</summary>
    public bool IsDestroyed { get; private set; }

    /// <summary>Whether the form's window is shown.</summary>
    public bool IsShown => !IsDestroyed && Desktop.IsWindowVisible(Window);

    /// <summary>
    /// The form's caption: its window's, which is what a UserForm's <c>Caption</c> is - the form itself draws no caption of its own, and would draw a frame around its
    /// controls if it were given one.
    /// </summary>
    public string Caption
    {
        get
        {
            var length = Desktop.GetWindowTextLength(Window);
            var text = new System.Text.StringBuilder(length + 1);
            _ = Desktop.GetWindowText(Window, text, text.Capacity);
            return text.ToString();
        }

        set => _ = Desktop.SetWindowText(Window, value);
    }

    /// <summary>
    /// Creates the form, sites it in a window of its own, loads it from the designer's state, and activates it in place; the window is not shown.
    /// </summary>
    /// <param name="definition">The form.</param>
    /// <param name="form">A new Microsoft Forms form, not sited yet.</param>
    /// <param name="apartment">The apartment the form lives on: pumped while it is shown modal, and what raises its window's events to the program.</param>
    /// <exception cref="COMException">The form would not be sited, loaded or activated.</exception>
    public static HostedForm Create(FormDefinition definition, object form, ComApartment apartment)
    {
        _ = Registered.Value;
        var window = Desktop.CreateWindowEx(
            ExtendedStyle, ClassName, definition.Caption ?? definition.Name, Style, Desktop.UseDefault, Desktop.UseDefault, 200, 200, IntPtr.Zero, IntPtr.Zero, Desktop.GetModuleHandle(null), IntPtr.Zero);
        if (window == IntPtr.Zero)
        {
            throw new COMException(null, Marshal.GetHRForLastWin32Error());
        }

        var hosted = new HostedForm(definition, form, window, apartment);
        Windows[window] = hosted;
        try
        {
            hosted.Load();
        }
        catch
        {
            hosted.Destroy();
            throw;
        }

        return hosted;
    }

    private void Load()
    {
        var ole = (IOleObject)Form;
        Check(ole.SetClientSite(_site));
        _ = ole.SetHostNames("RDCore", _definition.Name);

        var persist = (IPersistStorage)Form;
        var storage = StorageOf(_definition.State);
        try
        {
            Check(_definition.State.IsEmpty ? persist.InitNew(storage) : persist.Load(storage));
        }
        finally
        {
            _ = Marshal.Release(storage);
        }

        Size(Desktop.GetDpiForWindow(Window));
        _ = Desktop.GetClientRect(Window, out var client);
        Check(ole.DoVerb(Ole.InPlaceActivate, IntPtr.Zero, _site, 0, Window, ref client));
    }

    /// <summary>
    /// Shows the window - placed where the form starts up, the first time - and activates the form for its user. A modal form is the only one of the apartment's its
    /// user can use, and this returns when it is hidden; a modeless one returns at once.
    /// </summary>
    /// <remarks>
    /// The form that was active is deactivated, and this one activated: its <c>Deactivate</c>, then this one's <c>Activate</c> - on every <c>Show</c>, one after a
    /// <c>Hide</c> included (Excel-measured, <c>oracle\s41</c> V01-V03). A form hidden or unloaded activates none back (V02).
    /// </remarks>
    /// <param name="modal">Whether the form is shown modal.</param>
    public void Show(bool modal)
    {
        if (!_shownBefore)
        {
            Place();
            _shownBefore = true;
        }

        _ = Desktop.ShowWindow(Window, Desktop.SW_SHOW);
        _ = Desktop.SetForegroundWindow(Window);
        _ = Desktop.GetClientRect(Window, out var client);
        _ = ((IOleObject)Form).DoVerb(Ole.UIActivate, IntPtr.Zero, _site, 0, Window, ref client);

        if (_active is { } previous && previous != this && previous.IsShown)
        {
            previous.Raise(FormWindowEvents.Deactivate);
        }

        _active = this;
        Raise(FormWindowEvents.Activate);

        if (!modal)
        {
            return;
        }

        // the other forms wait for this one, as they do in VBA: their user cannot use them until it is hidden.
        var others = Windows.Values.Where(other => other != this && other.IsShown).ToArray();
        foreach (var other in others)
        {
            _ = Desktop.EnableWindow(other.Window, false);
        }

        try
        {
            _apartment.PumpUntil(() => !IsShown);
        }
        finally
        {
            foreach (var other in others.Where(other => !other.IsDestroyed))
            {
                _ = Desktop.EnableWindow(other.Window, true);
            }
        }
    }

    /// <summary>
    /// Hides the window. A form that is not shown is left as it is.
    /// </summary>
    public void Hide()
    {
        if (IsShown)
        {
            _ = Desktop.ShowWindow(Window, Desktop.SW_HIDE);
        }
    }

    /// <summary>
    /// The focus leaves the form for good, as it does when the form is unloaded: the form is deactivated, and the control that had the focus is left - Microsoft Forms
    /// raises its extender's events itself, as it does when the focus moves between its controls.
    /// </summary>
    public void Leave()
    {
        // what VBA's host does: each of the two asks a pending update, the second also exits the control - a cancelled BeforeUpdate is raised twice, as in Excel
        // (oracle\s41 E11); deactivating in place between them would ask it a third time.
        if (!IsDestroyed && Form is IOleInPlaceObject inPlace)
        {
            _ = inPlace.UIDeactivate();
            _ = ((IOleObject)Form).Close(Ole.CloseNoSave);
        }
    }

    /// <summary>
    /// Closes the form and destroys its window. The form object is let go of by whatever holds its handle.
    /// </summary>
    public void Destroy()
    {
        if (IsDestroyed)
        {
            return;
        }

        Hide();
        IsDestroyed = true;
        if (_active == this)
        {
            _active = null;
        }

        var ole = (IOleObject)Form;
        _ = ole.Close(Ole.CloseNoSave);
        _ = ole.SetClientSite(null);
        _ = Windows.Remove(Window);
        _ = Desktop.DestroyWindow(Window);
    }

    /// <summary>
    /// Offers a key to the form's active object, when the key is for the form's window or one of its own.
    /// </summary>
    /// <param name="message">The key.</param>
    /// <returns>Whether the form handled it.</returns>
    public static bool TranslateKey(ComApartment.Native.Message message)
        => Windows.TryGetValue(Desktop.GetAncestor(message.Window, Desktop.GA_ROOT), out var hosted)
           && hosted._site.ActiveObject is { } active
           && active.TranslateAccelerator(ref message) == 0;

    // the client area is the form's size, which is in HIMETRIC, at the window's DPI.
    private void Size(uint dpi)
    {
        _ = ((IOleObject)Form).GetExtent(Ole.ContentAspect, out var extent);
        var client = new Rect { Right = Pixels(extent.Width, dpi), Bottom = Pixels(extent.Height, dpi) };
        _ = Desktop.AdjustWindowRectExForDpi(ref client, Style, false, ExtendedStyle, dpi);
        _ = Desktop.SetWindowPos(Window, IntPtr.Zero, 0, 0, client.Width, client.Height, Desktop.SWP_NOMOVE | Desktop.SWP_NOZORDER | Desktop.SWP_NOACTIVATE);
    }

    private static int Pixels(int himetric, uint dpi) => (int)Math.Round(himetric * dpi / HimetricPerInch);

    // where the form starts up: on the screen it is placed on, at that screen's DPI.
    private void Place()
    {
        if (_definition.StartUpPosition == FormStartUpPosition.WindowsDefault)
        {
            return;
        }

        var monitor = _definition.StartUpPosition switch
        {
            FormStartUpPosition.Manual => Desktop.MonitorFromPoint(PointOf(_definition.Left, _definition.Top, 96), Desktop.MONITOR_DEFAULTTOPRIMARY),
            FormStartUpPosition.CenterScreen => Desktop.MonitorFromPoint(default, Desktop.MONITOR_DEFAULTTOPRIMARY),
            _ => Desktop.MonitorFromWindow(Desktop.GetForegroundWindow(), Desktop.MONITOR_DEFAULTTOPRIMARY),
        };

        var dpi = Desktop.GetDpiForMonitor(monitor, Desktop.EffectiveDpi, out var dpiX, out _) == 0 ? dpiX : Desktop.GetDpiForWindow(Window);
        if (dpi != Desktop.GetDpiForWindow(Window))
        {
            Size(dpi);
        }

        _ = Desktop.GetWindowRect(Window, out var window);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        _ = Desktop.GetMonitorInfo(monitor, ref info);

        // centered on the window that owns it: no window of the program does, so the active window of the desktop is the owner, and the screen when there is none.
        var around = info.WorkArea;
        if (_definition.StartUpPosition == FormStartUpPosition.CenterOwner && Desktop.GetForegroundWindow() is var owner && owner != IntPtr.Zero && owner != Window)
        {
            _ = Desktop.GetWindowRect(owner, out around);
        }

        var manual = PointOf(_definition.Left, _definition.Top, dpi);
        var (x, y) = _definition.StartUpPosition == FormStartUpPosition.Manual
            ? (manual.X, manual.Y)
            :(around.Left + ((around.Width - window.Width) / 2), around.Top + ((around.Height - window.Height) / 2));

        // a form that would start up off its screen starts up inside it, as a dialog does.
        x = Math.Clamp(x, info.WorkArea.Left, Math.Max(info.WorkArea.Left, info.WorkArea.Right - window.Width));
        y = Math.Clamp(y, info.WorkArea.Top, Math.Max(info.WorkArea.Top, info.WorkArea.Bottom - window.Height));
        _ = Desktop.SetWindowPos(Window, IntPtr.Zero, x, y, 0, 0, Desktop.SWP_NOSIZE | Desktop.SWP_NOZORDER | Desktop.SWP_NOACTIVATE);
    }

    // an event of the form's window, to whatever listens to the form's events: the program's side of it.
    private bool Raise(string name) => _apartment.RaiseEvent?.Invoke(Form, name, []) ?? false;

    private static Point PointOf(double left, double top, uint dpi) => new() { X = (int)Math.Round(left * dpi / 72), Y = (int)Math.Round(top * dpi / 72) };

    // the designer's state as a storage the form can load from, or a new one for a form that has none.
    private static IntPtr StorageOf(ReadOnlyMemory<byte> state)
    {
        var memory = Ole.GlobalAlloc(Ole.MoveableMemory, (UIntPtr)Math.Max(1, state.Length));
        if (!state.IsEmpty)
        {
            Marshal.Copy(state.ToArray(), 0, Ole.GlobalLock(memory), state.Length);
            _ = Ole.GlobalUnlock(memory);
        }

        Check(Ole.CreateILockBytesOnHGlobal(memory, true, out var bytes));
        try
        {
            IntPtr storage;
            Check(state.IsEmpty
                ? Ole.StgCreateDocfileOnILockBytes(bytes, Ole.CreateReadWriteShareExclusive, 0, out storage)
                : Ole.StgOpenStorageOnILockBytes(bytes, IntPtr.Zero, Ole.ReadShareExclusive, IntPtr.Zero, 0, out storage));
            return storage;
        }
        finally
        {
            _ = Marshal.Release(bytes);
        }
    }

    private static void Check(int status)
    {
        if (status < 0)
        {
            Marshal.ThrowExceptionForHR(status);
        }
    }

    // the window class of the forms, registered once for the process: a class is the process's, whichever thread registers it.
    private static ushort Register()
    {
        var windowClass = new Desktop.WindowClass
        {
            Size = (uint)Marshal.SizeOf<Desktop.WindowClass>(),
            Procedure = Procedure,
            Instance = Desktop.GetModuleHandle(null),
            Cursor = Desktop.LoadCursor(IntPtr.Zero, 32512),
            Background = Desktop.ColorButtonFace + 1,
            ClassName = ClassName,
        };

        return Desktop.RegisterClassEx(ref windowClass);
    }

    private static IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
        => Windows.TryGetValue(window, out var hosted) && hosted.Handle(message, wParam, lParam) is { } handled
            ? handled
            : Desktop.DefWindowProc(window, message, wParam, lParam);

    // what the window does that the form does not: the close button, the form's activation and focus, its size.
    private IntPtr? Handle(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            // the close button asks the program to close the form: its QueryClose, then the Unload that destroys this window - which ends a modal Show. A form no program
            // listens to is hidden, which ends it too.
            case Desktop.WM_CLOSE:
                if (!Raise(FormWindowEvents.Close))
                {
                    Hide();
                }

                return IntPtr.Zero;

            case Desktop.WM_ACTIVATE:
                _ = _site.ActiveObject?.OnFrameWindowActivate((wParam.ToInt64() & 0xFFFF) != 0 ? 1 : 0);
                return null;

            case Desktop.WM_SETFOCUS:
                if (Form is IOleInPlaceObject inPlace && inPlace.GetWindow(out var own) == 0 && own != IntPtr.Zero)
                {
                    _ = Desktop.SetFocus(own);
                    return IntPtr.Zero;
                }

                return null;

            case Desktop.WM_SIZE:
                if (Form is IOleInPlaceObject resized)
                {
                    _ = Desktop.GetClientRect(Window, out var client);
                    var clip = client;
                    _ = resized.SetObjectRects(ref client, ref clip);
                }

                return null;

            // a screen of another DPI: the window goes where the desktop suggests, and its client area is the form's size again, at the new DPI.
            case Desktop.WM_DPICHANGED:
                var suggested = Marshal.PtrToStructure<Rect>(lParam);
                _ = Desktop.SetWindowPos(Window, IntPtr.Zero, suggested.Left, suggested.Top, suggested.Width, suggested.Height, Desktop.SWP_NOZORDER | Desktop.SWP_NOACTIVATE);
                Size((uint)(wParam.ToInt64() & 0xFFFF));
                return IntPtr.Zero;

            default:
                return null;
        }
    }
}
