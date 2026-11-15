using Avalonia.Threading;
using RDCore.External.Automation;
using RDCore.Forms.Avalonia.Controls;
using RDCore.Forms.Avalonia.Rendering;
using RDCore.Forms.Menus;
using RDCore.Forms.Model.VB;
using RDCore.Forms.Model;
using Model = RDCore.Forms.Model;

namespace RDCore.External.Forms.RDForms;

// the handle of one of a form's components, the form itself included: the object the automation server answers for, one to a component, so that a program that
// asks for a control twice gets the same object.
internal sealed class RDFormsObject(RDFormsForm form, ComponentInstance component)
{
    public RDFormsForm Form { get; } = form;

    public ComponentInstance Component { get; } = component;

    public bool IsForm => ReferenceEquals(Component, Form.Model.Root);

    public override string ToString() => Component.ToString();
}

// a form hosted in an RDForms window: its model, its window, the handles of its components, and where their events go. Made and used on the UI thread only.
internal sealed class RDFormsForm : IComponentEventSink
{
    private readonly Dictionary<ComponentInstance, RDFormsObject> _handles = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ComponentInstance, IAutomationEventSink> _sinks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FormMenuArray, RDFormsMenuArray> _menuArrays = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FormMenuArray, IAutomationEventSink> _arraySinks = new(ReferenceEqualityComparer.Instance);
    private DispatcherFrame? _modal;

    public RDFormsForm(Model.FormDefinition model, CodeUnit unit)
    {
        Model = model;
        Unit = unit;
        Window = new VBFormWindow(model) { UnloadRequestHandler = AskToUnload };
        VBControl.SetEventSink(Window, this);
        Handle = HandleOf(model.Root);
        Controls = new RDFormsControls(this);
    }

    public Model.FormDefinition Model { get; }

    // the unit the form's program reads and writes its components' positions and sizes in; the model holds them in pixels.
    public CodeUnit Unit { get; }

    public VBFormWindow Window { get; }

    public RDFormsObject Handle { get; }

    // the form's Controls collection.
    public RDFormsControls Controls { get; }

    // the program's pictures that its components' picture properties hold.
    public RDFormsPictures Pictures { get; } = new();

    public bool IsDestroyed { get; private set; }

    public RDFormsObject HandleOf(ComponentInstance component)
    {
        if (!_handles.TryGetValue(component, out var handle))
        {
            handle = new RDFormsObject(this, component);
            _handles[component] = handle;
        }

        return handle;
    }

    // the handle of one of the form's menu arrays, where it has one of that name.
    public RDFormsMenuArray? MenuArrayOf(string name)
    {
        if (FormMenu.Of(Model.Root).ArrayOf(name) is not { } array)
        {
            return null;
        }

        if (!_menuArrays.TryGetValue(array, out var handle))
        {
            handle = new RDFormsMenuArray(this, array);
            _menuArrays[array] = handle;
        }

        return handle;
    }

    public void Advise(ComponentInstance source, IAutomationEventSink sink) => _sinks[source] = sink;

    public void Unadvise(ComponentInstance source) => _sinks.Remove(source);

    // where the events of a menu array's elements go, those a program loads included: each with its element's index first.
    public void Advise(FormMenuArray source, IAutomationEventSink sink) => _arraySinks[source] = sink;

    public void Unadvise(FormMenuArray source) => _arraySinks.Remove(source);

    // shown with the owner form's window as its owner when it has one: only a window not shown yet takes an owner, and only one that is another form's window, shown
    // (Avalonia's Show refuses a closed owner and the window itself). A form owned is placed over its owner, and closed with it (UnloadMode vbFormOwner).
    public void Show(RDFormsForm? owner)
    {
        if (owner is { IsDestroyed: false } && !ReferenceEquals(owner, this) && owner.Window.IsVisible && !Window.IsVisible)
        {
            Window.Show(owner.Window);
        }
        else
        {
            Window.Show();
        }
    }

    // shown modal: the other windows of the form host wait, and so does the call, on a frame of the UI thread's own that hiding the form ends.
    public void ShowModal(IEnumerable<RDFormsForm> others, RDFormsForm? owner)
    {
        var waiting = others.Where(other => other.Window.IsEnabled).ToList();
        foreach (var other in waiting)
        {
            other.Window.IsEnabled = false;
        }

        try
        {
            // the window raises its first Resize inside Show, and a handler of it can hide the form before the frame is there to end.
            var frame = new DispatcherFrame();
            _modal = frame;
            Show(owner);
            if (frame.Continue && Window.IsVisible)
            {
                Dispatcher.UIThread.PushFrame(frame);
            }
        }
        finally
        {
            _modal = null;
            foreach (var other in waiting)
            {
                other.Window.IsEnabled = true;
            }
        }
    }

    public void Hide()
    {
        Window.Hide();
        EndModal();
    }

    public void Destroy()
    {
        if (IsDestroyed)
        {
            return;
        }

        IsDestroyed = true;
        EndModal();
        _sinks.Clear();
        Pictures.ReleaseAll();
        VBControl.SetEventSink(Window, null);

        // closed from code, the window raises nothing: the program has unloaded the form already.
        Window.Close();
    }

    private void EndModal()
    {
        if (_modal is { } frame)
        {
            frame.Continue = false;
        }
    }

    // an event of one of the form's components, raised on the UI thread: the sink of the component hears it, and what its handlers left in the arguments is what the
    // control reads back.
    public void Raise(ComponentInstance source, EventDefinition definition, FormValue[] arguments)
    {
        // an element of a menu array raises its events to the array's sink, with its index before the event's own arguments, as VB6 calls the array's handler.
        var first = 0;
        object? element = null;
        if (!_sinks.TryGetValue(source, out var sink))
        {
            if (source is not { Index: { } index } || source.Class != VBClasses.Menu || FormMenuItem.Of(source).Array is not { } array
                || !_arraySinks.TryGetValue(array, out sink))
            {
                return;
            }

            first = 1;
            element = (short)index;
        }

        object?[] raised = [.. first == 1 ? [element] : Array.Empty<object?>(), .. arguments.Select(argument => RDFormsValues.ToNeutral(argument, HandleOf))];
        sink.OnEvent(new AutomationEvent(definition.Name, raised, RDFormsUI.IsInCall, _ => RDFormsUI.Serve()));
        for (var index = 0; index < arguments.Length; index++)
        {
            arguments[index] = RDFormsValues.FromNeutral(raised[first + index], arguments[index]);
        }
    }

    // the window was asked to close, by its user, the desktop or its owner (the UnloadMode says which): closing a form is the program's to decide and to do, as Unload
    // does, so the window stays open and raises nothing, and the program is asked. A form no program listens to has nothing to ask, and closes.
    private void AskToUnload(int mode)
    {
        if (IsDestroyed)
        {
            return;
        }

        if (!_sinks.TryGetValue(Model.Root, out var sink))
        {
            Window.Close();
            return;
        }

        sink.OnEvent(new AutomationEvent(FormWindowEvents.Close, [(short)mode], RDFormsUI.IsInCall, _ => RDFormsUI.Serve()));
    }
}
