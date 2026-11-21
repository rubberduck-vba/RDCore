using Avalonia.Threading;
using RDCore.External.Automation;
using RDCore.Forms.Avalonia.Rendering;
using RDCore.Forms.Menus;
using RDCore.Forms.Model;
using RDCore.Forms.Porting;
using System.Globalization;

namespace RDCore.External.Forms.RDForms;

/// <summary>
/// The automation server of the RDForms forms a <see cref="RDFormsFormHost"/> hosts: a form and each of its controls are objects of it. An object's properties are
/// those of its RDForms component, and a form's controls are reached by their names.
/// </summary>
/// <remarks>
/// It creates nothing by name: a form is created by its form host, and its controls with it. Every call on its objects is made on the RDForms UI thread, inline
/// when it comes from that thread already, as the calls an event's handlers make are.
/// </remarks>
public sealed class RDFormsAutomationServer : IAutomationServer
{
    private const int ClassNotRegistered = unchecked((int)0x80040154);
    private const int UnknownName = unchecked((int)0x80020006);
    private const int BadParameterCount = unchecked((int)0x8002000E);
    private const int NotImplemented = unchecked((int)0x80004001);
    private const int VisualBasicError = unchecked((int)0x800A0000);
    private const string VisualBasicLibrary = "VB";
    private const string RDFormsLibrary = "RDForms";
    private const string PopupMenuMember = "PopupMenu";
    private const string ControlsMember = "Controls";
    private const string NameMember = "Name";
    private const string IndexMember = "Index";
    private const string ContainerMember = "Container";
    private const string FontProperty = "Font";
    private const string StdFontClass = "stdole.StdFont";

    // a control's font is a StdFont, and of the interfaces it implements, as TypeOf reads them in VB6.
    private static readonly string[] FontClasses = ["StdFont", "Font", "IFontDisp", "IFont"];

    // the forms are Visual Basic 6.0 forms, whose code knows a component by its class of the VB library (VB.TextBox): the class each RDForms class
    // (RDForms.TextBox) ports from, by the RDForms class's name.
    private static readonly Dictionary<string, TypePort> VisualBasicClasses = FormsPortMap.VB6.Types
        .Where(type => type.Kind == TypePortKind.Class)
        .GroupBy(type => type.RDFormsName, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    private static bool? IsFontClass(string library, string className)
        => string.Equals(library, "stdole", StringComparison.OrdinalIgnoreCase)
            ? FontClasses.Contains(className, StringComparer.OrdinalIgnoreCase)
            : null;

    private const string DefaultMemberName = "_Default";
    private const string DefaultMemberDispatchName = "[DISPID=0]";

    /// <summary>
    /// Creates the server of the forms of a process, whose windows are on <paramref name="platform"/>.
    /// </summary>
    /// <param name="platform">
    /// Where the windows are. The process's first form starts the RDForms UI thread on it, once per process: a server of the same process that asks for the other
    /// platform cannot create a form.
    /// </param>
    public RDFormsAutomationServer(RDFormsPlatform platform)
    {
        Platform = platform;
    }

    /// <summary>
    /// Where the windows of the forms are.
    /// </summary>
    public RDFormsPlatform Platform { get; }

    /// <summary>
    /// Whether an object is one of this server's: a form, one of its controls, or a font one of them hands out.
    /// </summary>
    /// <param name="handle">The handle of the object.</param>
    public bool Owns(object handle) => handle is RDFormsObject or RDFormsControls or RDFormsControlsEnumerator or RDFormsArray or RDFormsFont;

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public object CreateObject(string progId) => throw new AutomationException(ClassNotRegistered, $"{progId}: RDForms creates no object by name.");

    /// <inheritdoc/>
    public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
    {
        if (target is RDFormsControls collection)
        {
            return RDFormsUI.Run(Program, () =>
            {
                Alive(collection.Form.Handle);
                return RDFormsControlsMembers.Invoke(collection, member, invocation, arguments, culture);
            });
        }

        if (target is RDFormsArray array)
        {
            return RDFormsUI.Run(Program, () =>
            {
                Alive(array.Form.Handle);
                return RDFormsArrayMembers.Invoke(array, member, invocation, arguments, culture);
            });
        }

        // a font is read after its control's form is gone, as VB6 reads it.
        if (target is RDFormsFont font)
        {
            return RDFormsUI.Run(Program, () => RDFormsFontMembers.Invoke(font, member, invocation, arguments, culture));
        }

        var handle = HandleOf(target);
        return RDFormsUI.Run(Program, () =>
        {
            Alive(handle);
            var component = handle.Component;

            // FontBold and its kin are members of the control's Font.
            if (component.Class.Property(member) is null && RDFormsFontMembers.TryShorthand(member, out var fontMember)
                && component.Class.Property(FontProperty) is { } fontProperty && RDFormsFonts.IsFont(fontProperty))
            {
                return RDFormsFontMembers.Invoke(handle.Form.Fonts.FontOf(component, fontProperty), fontMember, invocation, arguments, culture);
            }

            // a control's default member (_Default early bound, DISPID 0 late bound) is the member its class names for it, reached as if the program named it.
            if (IsDefaultMember(member) && component.Class.DefaultMember is { } named)
            {
                member = named;
            }

            // a control assigned a value through its form (Form1.Text1 = "abc") is assigned it through its own default member: the form's control is a property
            // that returns the control, and an object assigned a value gives it to its default member.
            if (invocation is AutomationInvocation.Let && arguments.Length == 1 && component.Class.Property(member) is null && Control(handle, member) is { } assigned)
            {
                return Invoke(assigned, DefaultMemberName, invocation, arguments, byReference, culture);
            }

            // an RDForms form's program renames a control it added, as MSForms lets it; a control of the form's file keeps its name (382).
            if (invocation is AutomationInvocation.Let && arguments.Length == 1 && !handle.Form.IsVisualBasic && component != handle.Form.Model.Root
                && component.Class.Property(member) is null && string.Equals(member, NameMember, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    handle.Form.Model.RenameControl(component, (string)RDFormsValues.FromNeutral(arguments[0], typeof(string), culture)!);
                    return null;
                }
                catch (FormErrorException exception)
                {
                    throw FormFailure(exception.Error);
                }
            }

            // a property the form's window answers, whose value its control holds, or which takes arguments: a ListBox's ListIndex and List(Index). It comes
            // before the model's property of the same name (a ListBox's List), which is the designer's.
            if (component.Class.Method(member) is { IsProperty: true } answered)
            {
                return invocation is AutomationInvocation.Let or AutomationInvocation.Set
                    ? AssignThroughWindow(handle, answered, arguments, culture)
                    : Call(handle, answered, arguments, culture);
            }

            if (RDFormsGraphics.TryInvoke(component, member, invocation, arguments, culture, out var drawn))
            {
                return drawn;
            }

            if (component.Class.Property(member) is { } property)
            {
                // the class says which of its properties only a form's file and a designer set (382 on an assignment), and which a program cannot reach at
                // all (393 on a read): the Timer's Left and Top are its class's, not the property's. A VB6 form keeps VB6's rule; an RDForms form's program
                // also sets what either library lets a program set (a TextBox's MultiLine, as MSForms does).
                var readOnly = handle.Form.IsVisualBasic
                    ? component.Class.IsReadOnlyAtRunTimeOnAVisualBasic6Form(property)
                    : component.Class.IsReadOnlyAtRunTime(property);
                if (invocation is AutomationInvocation.Let or AutomationInvocation.Set && readOnly)
                {
                    throw FormFailure(FormError.SetNotSupportedAtRuntime);
                }

                if (invocation is not (AutomationInvocation.Let or AutomationInvocation.Set) && component.Class.IsNotAvailableAtRunTime(property))
                {
                    throw FormFailure(FormError.GetNotSupportedAtRuntime);
                }

                return invocation switch
                {
                    AutomationInvocation.Let or AutomationInvocation.Set => Assign(handle.Form, component, property, arguments, culture),
                    _ when arguments.Length == 0 => Read(handle.Form, component, property),
                    _ => throw new AutomationException(BadParameterCount, $"{handle}.{member} takes no argument."),
                };
            }

            if (invocation is AutomationInvocation.Get or AutomationInvocation.Method && component.Class.Method(member) is { } method)
            {
                return Call(handle, method, arguments, culture);
            }

            // a control's name is not a property of its class: the file names the control, and Controls.Add names the one it adds.
            if (invocation is AutomationInvocation.Get or AutomationInvocation.Method && arguments.Length == 0
                && string.Equals(member, NameMember, StringComparison.OrdinalIgnoreCase))
            {
                return component.Name;
            }

            // an element's index in its array is not a property of its class either: the file gives it, and Load the one it loads. A control of no array has
            // none (343).
            if (invocation is AutomationInvocation.Get or AutomationInvocation.Method && arguments.Length == 0
                && string.Equals(member, IndexMember, StringComparison.OrdinalIgnoreCase) && component.Class.Property(member) is null)
            {
                return component.Index is { } index ? (short)index : throw FormFailure(FormError.ObjectNotAnArray);
            }

            // a control's container is not a property of its class either: the form, a Frame or a PictureBox, read as its object and placed with Set.
            // A Timer, a menu and a form have none (438), and an assignment without Set is 438 too.
            if (component.Class.HasContainer && component.Class.Property(member) is null
                && string.Equals(member, ContainerMember, StringComparison.OrdinalIgnoreCase))
            {
                if (invocation is AutomationInvocation.Get or AutomationInvocation.Method && arguments.Length == 0)
                {
                    return handle.Form.HandleOf(component.Container!);
                }

                if (invocation is AutomationInvocation.Set && arguments.Length == 1)
                {
                    PlaceIn(handle, arguments[0]);
                    return null;
                }
            }

            // the form's collection of its controls, which a control of its file cannot be named for; given arguments, its default member: Controls(0) is
            // Controls.Item(0).
            if (invocation is AutomationInvocation.Get or AutomationInvocation.Method && handle.IsForm
                && string.Equals(member, ControlsMember, StringComparison.OrdinalIgnoreCase))
            {
                return arguments.Length == 0 ? handle.Form.Controls : RDFormsControlsMembers.Invoke(handle.Form.Controls, "Item", invocation, arguments, culture);
            }

            if (invocation is AutomationInvocation.Get or AutomationInvocation.Method && arguments.Length == 0
                && Control(handle, member) is { } control)
            {
                return control;
            }

            // an array, given arguments, is its default member, as a late-bound call reaches an element (f.mnuRecent(2), f.picCell(0)) in one call.
            if (invocation is AutomationInvocation.Get or AutomationInvocation.Method && handle.IsForm && handle.Form.ArrayOf(member) is { } indexed)
            {
                return RDFormsArrayMembers.Invoke(indexed, DefaultMemberDispatchName, invocation, arguments, culture);
            }

            if (invocation is AutomationInvocation.Get or AutomationInvocation.Method && handle.IsForm
                && string.Equals(member, PopupMenuMember, StringComparison.OrdinalIgnoreCase))
            {
                PopupMenu(handle, arguments, culture);
                return null;
            }

            throw new AutomationException(UnknownName, $"{handle} has no member named {member}.");
        });
    }

    // a method runs on the form's window, which checks the arguments as VB6 does and raises what the method raises. An argument left out is none, and one for a
    // typed parameter is let-coerced to its type first, as a typed parameter's argument is before the call. A property the window answers is read the same way,
    // and returns its value.
    private static object? Call(RDFormsObject handle, MethodDefinition method, object?[] arguments, CultureInfo culture)
    {
        var given = ArgumentsOf(method, arguments, arguments.Length, culture);
        try
        {
            var result = handle.Form.Window.Invoke(handle.Component, method, given, handle.Form.Unit);
            return RDFormsValues.ToNeutral(result, handle.Form.HandleOf);
        }
        catch (FormErrorException exception)
        {
            throw FormFailure(exception.Error);
        }
    }

    // Set Control.Container: the form places the control and its window moves it. Nothing, an object other than one of the same form's, and what the form
    // refuses are 425.
    private static void PlaceIn(RDFormsObject handle, object? value)
    {
        if (value is not RDFormsObject container || !ReferenceEquals(container.Form, handle.Form))
        {
            throw FormFailure(FormError.InvalidObjectUse);
        }

        Alive(container);
        try
        {
            handle.Form.Model.SetContainer(handle.Component, container.Component);
        }
        catch (FormErrorException exception)
        {
            throw FormFailure(exception.Error);
        }
    }

    // a property the form's window answers, assigned: its arguments come first, and the value last, let-coerced to the property's type (List(0) = 5 assigns "5").
    private static object? AssignThroughWindow(RDFormsObject handle, MethodDefinition property, object?[] arguments, CultureInfo culture)
    {
        if (arguments.Length == 0)
        {
            throw new AutomationException(BadParameterCount, $"{handle}.{property.Name} is assigned no value.");
        }

        var given = ArgumentsOf(property, arguments, arguments.Length - 1, culture);
        var value = RDFormsValues.ArgumentOf(arguments[^1], property.Value, culture);
        try
        {
            handle.Form.Window.Assign(handle.Component, property, given, value);
            return null;
        }
        catch (FormErrorException exception)
        {
            throw FormFailure(exception.Error);
        }
    }

    private static FormValue?[] ArgumentsOf(MethodDefinition method, object?[] arguments, int count, CultureInfo culture)
    {
        var given = new FormValue?[count];
        for (var index = 0; index < count; index++)
        {
            given[index] = arguments[index] is System.Reflection.Missing
                ? null
                : RDFormsValues.ArgumentOf(arguments[index], index < method.Parameters.Count ? method.Parameters[index] : null, culture);
        }

        return given;
    }

    // VB's PopupMenu Menu, [Flags], [X], [Y], [BoldCommand]: the form's window pops up the menu's items, and the call waits, as a modal Show does, until the menu
    // closes, after the Click of an item chosen from it. X and Y are in the form's scale, and either one left out is the pointer's. Of Flags, 4 centers the menu
    // on the point and 8 puts its right edge there; the mouse button flag (2) is accepted and changes nothing. A form that is not shown has no menu host, and pops up nothing.
    private static void PopupMenu(RDFormsObject handle, object?[] arguments, CultureInfo culture)
    {
        if (arguments.Length is 0 or > 5)
        {
            throw new AutomationException(BadParameterCount, $"{handle}.{PopupMenuMember} takes a menu, and up to four arguments after it.");
        }

        var menu = MenuOf(handle, arguments[0]) ?? throw NotAMenu(handle);
        var flags = Given(arguments, 1) is { } flag ? (int)RDFormsValues.FromNeutral(flag, typeof(int), culture)! : 0;
        double? x = Given(arguments, 2) is { } left ? RDFormsValues.SingleOf(left, culture) : null;
        double? y = Given(arguments, 3) is { } top ? RDFormsValues.SingleOf(top, culture) : null;
        var bold = Given(arguments, 4) is { } boldCommand ? MenuOf(handle, boldCommand) ?? throw NotAMenu(handle) : null;
        var alignment = (flags & 8) != 0 ? PopupMenuAlignment.Right : (flags & 4) != 0 ? PopupMenuAlignment.Center : PopupMenuAlignment.Left;

        if (MenuHosts.Of(handle.Form.Model.Root) is not { } host)
        {
            return;
        }

        var closed = host.PopupMenu(menu, x, y, alignment, bold);
        if (!closed.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = closed.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false, DispatcherPriority.Send), TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }

        // what the menu raised as it closed fails the call.
#pragma warning disable VSTHRD002
        closed.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    // an argument given: none past the last one, and none for one left out.
    private static object? Given(object?[] arguments, int index)
        => index < arguments.Length && arguments[index] is not System.Reflection.Missing ? arguments[index] : null;

    // the item a menu of the form's own is; null for any other value.
    private static FormMenuItem? MenuOf(RDFormsObject form, object? value)
        => value is RDFormsObject { IsForm: false } menu && ReferenceEquals(menu.Form, form.Form) && menu.Component.Class == ComponentClasses.Menu
            ? FormMenuItem.Of(menu.Component)
            : null;

    private static AutomationException NotAMenu(RDFormsObject handle)
        => new(VisualBasicError | 5, $"{handle}.{PopupMenuMember}: the argument is not a menu of the form.");

    // a control of the form, by its name: a control that is an element of an array is reached through the array, which its name alone names.
    private static object? Control(RDFormsObject handle, string name)
        => !handle.IsForm ? null
            : handle.Form.Model.Find(name) is { } control && !ReferenceEquals(control, handle.Component) ? handle.Form.HandleOf(control)
            : handle.Form.ArrayOf(name);

    // the model holds a component's position and size in pixels: the program reads and writes them in its form's unit.
    private static object? Read(RDFormsForm form, ComponentInstance component, PropertyDefinition property)
    {
        if (RDFormsFonts.IsFont(property))
        {
            return form.Fonts.FontOf(component, property);
        }

        var stored = component.GetBoxed(property);
        if (RDFormsPictures.IsPicture(property) && form.Pictures.PictureOf(component, property, stored as byte[]) is { } picture)
        {
            return picture;
        }

        // a picture property that holds none is Nothing, which a late-bound read (a Label's Picture, RDForms' own) takes as Nothing too.
        // CA1416: a wrapper of no object asks nothing of COM, whatever the platform.
#pragma warning disable CA1416
        if (RDFormsPictures.IsPicture(property) && stored is null)
        {
            return new System.Runtime.InteropServices.DispatchWrapper(null);
        }
#pragma warning restore CA1416

        if (CodeUnits.IsExtent(property))
        {
            stored = CodeUnits.ToCode(component, property, (double)stored!, form.Unit);
        }

        return RDFormsValues.TryToNeutral(stored, out var value)
            ? value
            : throw new AutomationException(NotImplemented, $"{component}.{property.Name}: a value of type {property.ValueType.Name} cannot be given to a program.");
    }

    private static object? Assign(RDFormsForm form, ComponentInstance component, PropertyDefinition property, object?[] arguments, CultureInfo culture)
    {
        if (arguments.Length != 1)
        {
            throw new AutomationException(BadParameterCount, $"{component}.{property.Name} takes no argument.");
        }

        if (RDFormsFonts.IsFont(property))
        {
            AssignFont(form, component, property, arguments[0], culture);
            return null;
        }

        var value = RDFormsPictures.IsPicture(property)
            ? form.Pictures.Assign(component, property, arguments[0])
            : RDFormsValues.FromNeutral(arguments[0], property.ValueType, culture);
        if (CodeUnits.IsExtent(property))
        {
            value = CodeUnits.FromCode(component, property, (double)value!, form.Unit);
        }

        if (!component.TrySet(property, value))
        {
            throw new AutomationException(RDFormsValues.InvalidPropertyValue, "Invalid property value");
        }

        return null;
    }

    // Set of a font shares it: the control draws it, and a change through any reference is the control's (measured in VB6). Nothing is 380, and the font stays. A
    // String or a number, which a Let of a font gives as its default member, sets the font's name.
    private static void AssignFont(RDFormsForm form, ComponentInstance component, PropertyDefinition property, object? value, CultureInfo culture)
    {
        switch (value)
        {
            case RDFormsFont font:
                form.Fonts.Share(component, property, font);
                return;

            // CA1416: a wrapper of no object asks nothing of COM, whatever the platform.
#pragma warning disable CA1416
            case null or System.Runtime.InteropServices.DispatchWrapper { WrappedObject: null }:
#pragma warning restore CA1416
                throw FormFailure(FormError.InvalidPropertyValue);

            case string or bool or byte or short or int or long or float or double or decimal or DateTime:
                _ = RDFormsFontMembers.Invoke(form.Fonts.FontOf(component, property), NameMember, AutomationInvocation.Let, [value], culture);
                return;

            default:
                throw new AutomationException(NotImplemented, $"{component}.{property.Name}: only a font a form's control holds can be given to it.");
        }
    }

    /// <inheritdoc/>
    public string? ClassNameOf(object target)
        // the Controls collection and a control array are not of a library's class: a late-bound Object.
        => target is RDFormsFont ? StdFontClass
            : target is RDFormsControls or RDFormsArray ? null
            : HandleOf(target) is { Form.IsVisualBasic: true } handle && VisualBasicClasses.TryGetValue(handle.Component.Class.TypeName, out var ported)
                ? $"{ported.LegacyLibrary}.{ported.LegacyName}"
                : HandleOf(target).Component.Class.TypeName;

    /// <inheritdoc/>
    public bool IsFormHostObject(object target) => Owns(target);

    /// <inheritdoc/>
    // a VB6 form's components are of VB's classes, and an RDForms form's of RDForms'.
    public bool? IsInstanceOf(object target, string library, string className)
    {
        if (target is RDFormsFont)
        {
            return IsFontClass(library, className);
        }

        if (target is RDFormsControls or RDFormsArray)
        {
            return null;
        }

        var handle = HandleOf(target);
        var typeName = handle.Component.Class.TypeName;
        if (handle.Form.IsVisualBasic)
        {
            return string.Equals(library, VisualBasicLibrary, StringComparison.OrdinalIgnoreCase)
                ? VisualBasicClasses.TryGetValue(typeName, out var ported) && string.Equals(ported.LegacyName, className, StringComparison.OrdinalIgnoreCase)
                : null;
        }

        return string.Equals(library, RDFormsLibrary, StringComparison.OrdinalIgnoreCase)
            ? string.Equals(typeName, $"{RDFormsLibrary}.{className}", StringComparison.OrdinalIgnoreCase)
            : null;
    }

    /// <inheritdoc/>
    public bool MoveNext(object enumerator, out object? current)
        => enumerator is RDFormsControlsEnumerator controls
            ? controls.MoveNext(out current)
            : throw new AutomationException(NotImplemented, "Only a form's Controls collection has an enumerator.");

    /// <inheritdoc/>
    public void Reset(object enumerator)
    {
        if (enumerator is not RDFormsControlsEnumerator controls)
        {
            throw new AutomationException(NotImplemented, "Only a form's Controls collection has an enumerator.");
        }

        controls.Reset();
    }

    /// <inheritdoc/>
    public void Advise(object source, IAutomationEventSink sink)
    {
        // an array raises the events of its elements, each with the element's index first.
        if (source is RDFormsArray array)
        {
            RDFormsUI.Run(Program, () => array.Form.Advise(array, sink));
            return;
        }

        var handle = source is RDFormsControls ? throw new AutomationException(NotImplemented, "A form's Controls collection raises no events.") : HandleOf(source);
        RDFormsUI.Run(Program, () => handle.Form.Advise(handle.Component, sink));
    }

    /// <inheritdoc/>
    public void Unadvise(object source)
    {
        if (source is RDFormsArray array)
        {
            RDFormsUI.Run(Program, () => array.Form.Unadvise(array));
            return;
        }

        if (source is RDFormsControls)
        {
            return;
        }

        var handle = HandleOf(source);
        RDFormsUI.Run(Program, () => handle.Form.Unadvise(handle.Component));
    }

    /// <inheritdoc/>
    public void Release(object handle)
    {
        // a form is destroyed when it is let go of, as a UserForm of the platform's own server is; a control is the form's, and goes with it.
        if (handle is RDFormsObject { IsForm: true } form)
        {
            Destroy(form.Form);
        }
    }

    // the forms that are not destroyed, which a modal one keeps from being used. Touched on the UI thread only.
    internal List<RDFormsForm> Forms { get; } = [];

    // the program the forms are of, as RDForms sees it: its windows share one active form, which another program's window taking the activation leaves alone,
    // and the calls the UI thread makes for it are the ones its forms' events answer (RDFormsUI.IsInCallOf).
    internal RDFormsProgram Program { get; } = new();

    internal void Destroy(RDFormsForm form) => RDFormsUI.Run(Program, () =>
    {
        form.Destroy();
        _ = Forms.Remove(form);
    });

    internal static RDFormsObject HandleOf(object target)
        => target as RDFormsObject ?? throw new AutomationException(unchecked((int)0x80020005), "The object is not one of RDForms.");

    internal static void Alive(RDFormsObject handle)
    {
        if (handle.Form.IsDestroyed)
        {
            throw new AutomationException(unchecked((int)0x80010108), $"{handle}: its form was destroyed.");
        }

        // a control Controls.Remove took out of the form, or one a control it took out held: any use of it is 735, whatever still holds its object.
        if (!handle.IsForm && !IsPlaced(handle))
        {
            throw FormFailure(FormError.ControlRemoved);
        }
    }

    // what a form raises, as the program's Err has it.
    private static AutomationException FormFailure(FormError error) => new(VisualBasicError | error.Number, error.Description);

    private static bool IsDefaultMember(string member)
        => string.Equals(member, DefaultMemberName, StringComparison.OrdinalIgnoreCase) || string.Equals(member, DefaultMemberDispatchName, StringComparison.Ordinal);

    // whether a control is in its form: its containers lead to the form's root.
    internal static bool IsPlaced(RDFormsObject handle)
    {
        var component = handle.Component;
        while (component.Container is { } container)
        {
            component = container;
        }

        return ReferenceEquals(component, handle.Form.Model.Root);
    }
}
