using System.Runtime.InteropServices;
using Message = RDCore.External.Windows.Automation.ComApartment.Native.Message;

namespace RDCore.External.Windows.Forms;

// What hosting an ActiveX control in place takes of OLE and of the desktop: the interfaces of the control and of its site, in the order of their tables, and the
// functions of the windows they live in. Only what the form host calls is declared; the rest of each table is there to keep the later members where they belong.

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Size
{
    public int Width;
    public int Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Point
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct InPlaceFrameInfo
{
    public uint Size;
    public int IsMdiApplication;
    public IntPtr Frame;
    public IntPtr Accelerators;
    public uint AcceleratorCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo
{
    public uint Size;
    public Rect Monitor;
    public Rect WorkArea;
    public uint Flags;
}

[ComImport]
[Guid("00000112-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleObject
{
    [PreserveSig]
    int SetClientSite(IOleClientSite? site);

    [PreserveSig]
    int GetClientSite(out IntPtr site);

    [PreserveSig]
    int SetHostNames([MarshalAs(UnmanagedType.LPWStr)] string application, [MarshalAs(UnmanagedType.LPWStr)] string? document);

    [PreserveSig]
    int Close(uint saveOption);

    [PreserveSig]
    int SetMoniker(uint which, IntPtr moniker);

    [PreserveSig]
    int GetMoniker(uint assign, uint which, out IntPtr moniker);

    [PreserveSig]
    int InitFromData(IntPtr data, int creation, uint reserved);

    [PreserveSig]
    int GetClipboardData(uint reserved, out IntPtr data);

    [PreserveSig]
    int DoVerb(int verb, IntPtr message, IOleClientSite site, int index, IntPtr parent, ref Rect position);

    [PreserveSig]
    int EnumVerbs(out IntPtr verbs);

    [PreserveSig]
    int Update();

    [PreserveSig]
    int IsUpToDate();

    [PreserveSig]
    int GetUserClassID(out Guid classId);

    [PreserveSig]
    int GetUserType(uint form, out IntPtr name);

    [PreserveSig]
    int SetExtent(uint aspect, ref Size size);

    [PreserveSig]
    int GetExtent(uint aspect, out Size size);
}

[ComImport]
[Guid("00000113-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleInPlaceObject
{
    [PreserveSig]
    int GetWindow(out IntPtr window);

    [PreserveSig]
    int ContextSensitiveHelp(int enter);

    [PreserveSig]
    int InPlaceDeactivate();

    [PreserveSig]
    int UIDeactivate();

    [PreserveSig]
    int SetObjectRects(ref Rect position, ref Rect clip);
}

[ComImport]
[Guid("00000118-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleClientSite
{
    [PreserveSig]
    int SaveObject();

    [PreserveSig]
    int GetMoniker(uint assign, uint which, out IntPtr moniker);

    [PreserveSig]
    int GetContainer(out IntPtr container);

    [PreserveSig]
    int ShowObject();

    [PreserveSig]
    int OnShowWindow(int show);

    [PreserveSig]
    int RequestNewObjectLayout();
}

[ComImport]
[Guid("00000119-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleInPlaceSite
{
    [PreserveSig]
    int GetWindow(out IntPtr window);

    [PreserveSig]
    int ContextSensitiveHelp(int enter);

    [PreserveSig]
    int CanInPlaceActivate();

    [PreserveSig]
    int OnInPlaceActivate();

    [PreserveSig]
    int OnUIActivate();

    [PreserveSig]
    int GetWindowContext(out IntPtr frame, out IntPtr document, IntPtr position, IntPtr clip, IntPtr frameInfo);

    [PreserveSig]
    int Scroll(Size extent);

    [PreserveSig]
    int OnUIDeactivate(int undoable);

    [PreserveSig]
    int OnInPlaceDeactivate();

    [PreserveSig]
    int DiscardUndoState();

    [PreserveSig]
    int DeactivateAndUndo();

    [PreserveSig]
    int OnPosRectChange(IntPtr position);
}

[ComImport]
[Guid("00000116-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleInPlaceFrame
{
    [PreserveSig]
    int GetWindow(out IntPtr window);

    [PreserveSig]
    int ContextSensitiveHelp(int enter);

    [PreserveSig]
    int GetBorder(IntPtr rectangle);

    [PreserveSig]
    int RequestBorderSpace(IntPtr widths);

    [PreserveSig]
    int SetBorderSpace(IntPtr widths);

    [PreserveSig]
    int SetActiveObject(IntPtr activeObject, [MarshalAs(UnmanagedType.LPWStr)] string? name);

    [PreserveSig]
    int InsertMenus(IntPtr menu, IntPtr widths);

    [PreserveSig]
    int SetMenu(IntPtr menu, IntPtr shared, IntPtr activeObject);

    [PreserveSig]
    int RemoveMenus(IntPtr menu);

    [PreserveSig]
    int SetStatusText([MarshalAs(UnmanagedType.LPWStr)] string? text);

    [PreserveSig]
    int EnableModeless(int enable);

    [PreserveSig]
    int TranslateAccelerator(IntPtr message, ushort identifier);
}

[ComImport]
[Guid("00000117-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleInPlaceActiveObject
{
    [PreserveSig]
    int GetWindow(out IntPtr window);

    [PreserveSig]
    int ContextSensitiveHelp(int enter);

    [PreserveSig]
    int TranslateAccelerator(ref Message message);

    [PreserveSig]
    int OnFrameWindowActivate(int activate);

    [PreserveSig]
    int OnDocWindowActivate(int activate);
}

[ComImport]
[Guid("0000010A-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistStorage
{
    [PreserveSig]
    int GetClassID(out Guid classId);

    [PreserveSig]
    int IsDirty();

    [PreserveSig]
    int InitNew(IntPtr storage);

    [PreserveSig]
    int Load(IntPtr storage);
}

/// <summary>
/// The ambient properties a control asks its site for (<c>DISPID_AMBIENT_*</c>): those of a container in run mode, not in design mode.
/// </summary>
[ComVisible(true)]
[Guid("6B2B0F55-4C0E-4A9A-9A47-8E3F2C0A1D11")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IAmbientProperties
{
    /// <summary>Whether the container runs its controls (<c>True</c>) or designs them.</summary>
    [DispId(-709)]
    bool UserMode { get; }

    /// <summary>Whether the controls are dead to the user's input.</summary>
    [DispId(-710)]
    bool UIDead { get; }

    /// <summary>Whether the controls show the handles a designer moves them by.</summary>
    [DispId(-711)]
    bool ShowGrabHandles { get; }

    /// <summary>Whether the controls show the hatching of an object that is activated in place.</summary>
    [DispId(-712)]
    bool ShowHatching { get; }

    /// <summary>The locale of the container.</summary>
    [DispId(-705)]
    int LocaleID { get; }
}

internal static class Ole
{
    public const int InPlaceActivate = -5;
    public const int UIActivate = -4;
    public const uint ContentAspect = 1;
    public const uint CloseNoSave = 1;
    public const uint MoveableMemory = 0x0002;
    public const uint ReadShareExclusive = 0x00000010;
    public const uint CreateReadWriteShareExclusive = 0x00001000 | 0x00000002 | 0x00000010;
    public const int NoToolSpace = unchecked((int)0x800401A1);
    public const int NotImplemented = unchecked((int)0x80004001);
    public const int NoInterface = unchecked((int)0x80004002);
    public const int False = 1;

    [DllImport("ole32.dll")]
    public static extern int CreateILockBytesOnHGlobal(IntPtr global, [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease, out IntPtr lockBytes);

    [DllImport("ole32.dll")]
    public static extern int StgOpenStorageOnILockBytes(IntPtr lockBytes, IntPtr priority, uint mode, IntPtr exclude, uint reserved, out IntPtr storage);

    [DllImport("ole32.dll")]
    public static extern int StgCreateDocfileOnILockBytes(IntPtr lockBytes, uint mode, uint reserved, out IntPtr storage);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalUnlock(IntPtr memory);
}

internal static class Desktop
{
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_DPICHANGED = 0x02E0;

    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_CLIPCHILDREN = 0x02000000;
    public const uint WS_EX_DLGMODALFRAME = 0x00000001;
    public const int UseDefault = unchecked((int)0x80000000);

    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    public const uint GA_ROOT = 2;
    public const int EffectiveDpi = 0;
    public const int ColorButtonFace = 15;

    public delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Size;
        public uint Style;
        public WindowProcedure Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(
        uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    // the Unicode one, as the window is: the ANSI one would keep its caption as ANSI, and read a character of it.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowText(IntPtr window, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int length);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AdjustWindowRectExForDpi(ref Rect rectangle, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint extendedStyle, uint dpi);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint dpiX, out uint dpiY);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursor);
}
