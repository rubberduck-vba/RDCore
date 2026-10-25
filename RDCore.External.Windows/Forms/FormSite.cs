using System.Runtime.InteropServices;

namespace RDCore.External.Windows.Forms;

/// <summary>
/// The one OLE site a form is hosted in: its client site, its in-place site and its in-place frame, all of them the window the form is in; and the ambient
/// properties of a container that runs its controls.
/// </summary>
/// <remarks>
/// <para>
/// A Microsoft Forms form is itself the container of its controls, which are windowless, so it is the only control the host sites. The form tells its frame which of
/// its controls is the active one each time the focus moves, and that is the object a key is offered to before the window is.
/// </para>
/// <para>
/// The site is public only so that the runtime answers the form's <c>IDispatch</c> for its ambient properties: it does not for a class that is not, and a form that
/// cannot ask whether it is run loads in design mode, and is not activated. Its OLE interfaces are the host's own.
/// </para>
/// </remarks>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
[ComDefaultInterface(typeof(IAmbientProperties))]
public sealed class FormSite : IOleClientSite, IOleInPlaceSite, IOleInPlaceFrame, IAmbientProperties
{
    private readonly IntPtr _window;

    internal FormSite(IntPtr window) => _window = window;

    // the object that is active in the form now, which the keys go to first; null when none is.
    internal IOleInPlaceActiveObject? ActiveObject { get; private set; }

    // the form asks while it is sited, and loads its controls in run mode.
    /// <inheritdoc/>
    public bool UserMode => true;

    /// <inheritdoc/>
    public bool UIDead => false;

    /// <inheritdoc/>
    public bool ShowGrabHandles => false;

    /// <inheritdoc/>
    public bool ShowHatching => false;

    // the language of the desktop's own user interface: the language the controls speak, as an Office application's are in its own. The invariant culture is no
    // language at all, and Microsoft Forms finds no resources for it.
    /// <inheritdoc/>
    public int LocaleID => System.Globalization.CultureInfo.InstalledUICulture is { LCID: not 127 } installed ? installed.LCID : 0x0409;

    int IOleClientSite.SaveObject() => 0;

    int IOleClientSite.GetMoniker(uint assign, uint which, out IntPtr moniker)
    {
        moniker = IntPtr.Zero;
        return Ole.NotImplemented;
    }

    int IOleClientSite.GetContainer(out IntPtr container)
    {
        container = IntPtr.Zero;
        return Ole.NoInterface;
    }

    int IOleClientSite.ShowObject() => 0;

    int IOleClientSite.OnShowWindow(int show) => 0;

    int IOleClientSite.RequestNewObjectLayout() => Ole.NotImplemented;

    int IOleInPlaceSite.GetWindow(out IntPtr window)
    {
        window = _window;
        return 0;
    }

    int IOleInPlaceFrame.GetWindow(out IntPtr window)
    {
        window = _window;
        return 0;
    }

    int IOleInPlaceSite.ContextSensitiveHelp(int enter) => 0;

    int IOleInPlaceFrame.ContextSensitiveHelp(int enter) => 0;

    int IOleInPlaceSite.CanInPlaceActivate() => 0;

    int IOleInPlaceSite.OnInPlaceActivate() => 0;

    int IOleInPlaceSite.OnUIActivate() => 0;

    // the form is the whole of the window's client area, and the window is its own frame: there is no document window between them, and no accelerator table.
    int IOleInPlaceSite.GetWindowContext(out IntPtr frame, out IntPtr document, IntPtr position, IntPtr clip, IntPtr frameInfo)
    {
        frame = Marshal.GetComInterfaceForObject(this, typeof(IOleInPlaceFrame));
        document = IntPtr.Zero;
        _ = Desktop.GetClientRect(_window, out var client);
        Marshal.StructureToPtr(client, position, false);
        Marshal.StructureToPtr(client, clip, false);

        var info = Marshal.PtrToStructure<InPlaceFrameInfo>(frameInfo);
        info.IsMdiApplication = 0;
        info.Frame = _window;
        info.Accelerators = IntPtr.Zero;
        info.AcceleratorCount = 0;
        Marshal.StructureToPtr(info, frameInfo, false);
        return 0;
    }

    int IOleInPlaceSite.Scroll(Size extent) => 0;

    int IOleInPlaceSite.OnUIDeactivate(int undoable) => 0;

    int IOleInPlaceSite.OnInPlaceDeactivate()
    {
        if (ActiveObject is { } previous)
        {
            _ = Marshal.ReleaseComObject(previous);
        }

        ActiveObject = null;
        return 0;
    }

    int IOleInPlaceSite.DiscardUndoState() => 0;

    int IOleInPlaceSite.DeactivateAndUndo() => 0;

    int IOleInPlaceSite.OnPosRectChange(IntPtr position) => 0;

    // a frame with no toolbars has no room to give one.
    int IOleInPlaceFrame.GetBorder(IntPtr rectangle) => Ole.NoToolSpace;

    int IOleInPlaceFrame.RequestBorderSpace(IntPtr widths) => Ole.NoToolSpace;

    int IOleInPlaceFrame.SetBorderSpace(IntPtr widths) => 0;

    // the active object is a control, which the program may hold too: the site's reference to it is a wrapper of its own, which the automation server releasing the
    // program's cannot cut off from under it.
    int IOleInPlaceFrame.SetActiveObject(IntPtr activeObject, string? name)
    {
        if (ActiveObject is { } previous)
        {
            _ = Marshal.ReleaseComObject(previous);
        }

        ActiveObject = activeObject == IntPtr.Zero ? null : (IOleInPlaceActiveObject)Marshal.GetUniqueObjectForIUnknown(activeObject);
        return 0;
    }

    int IOleInPlaceFrame.InsertMenus(IntPtr menu, IntPtr widths) => 0;

    int IOleInPlaceFrame.SetMenu(IntPtr menu, IntPtr shared, IntPtr activeObject) => 0;

    int IOleInPlaceFrame.RemoveMenus(IntPtr menu) => 0;

    int IOleInPlaceFrame.SetStatusText(string? text) => 0;

    int IOleInPlaceFrame.EnableModeless(int enable) => 0;

    int IOleInPlaceFrame.TranslateAccelerator(IntPtr message, ushort identifier) => Ole.False;
}
