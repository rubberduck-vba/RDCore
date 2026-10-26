namespace RDCore.External.Forms;

/// <summary>
/// Where a form starts up when it is shown the first time: the form's <c>StartUpPosition</c>, as a VBA UserForm's designer has it.
/// </summary>
public enum FormStartUpPosition
{
    /// <summary>Where the form's own <c>Left</c> and <c>Top</c> say (<c>0 - Manual</c>).</summary>
    Manual = 0,

    /// <summary>Centered on the window that owns it, which is the active window of the desktop when no window of the program owns it (<c>1 - CenterOwner</c>).</summary>
    CenterOwner = 1,

    /// <summary>Centered on the screen it is shown on (<c>2 - CenterScreen</c>).</summary>
    CenterScreen = 2,

    /// <summary>Where the desktop puts a new window (<c>3 - Windows Default</c>).</summary>
    WindowsDefault = 3,
}

/// <summary>
/// A form to host: its name, the designer's state of it, and how its window starts up.
/// </summary>
/// <param name="Name">The name of the form, as its module is named.</param>
/// <param name="State">
/// The designer's state of the form: for a VBA UserForm, the compound file its <c>.frx</c> keeps (<c>FormDesigner.StateOf</c>), which Microsoft Forms reads
/// itself. Empty for a form that has none, which starts out empty.
/// </param>
public sealed record class FormDefinition(string Name, ReadOnlyMemory<byte> State)
{
    /// <summary>
    /// The form's caption, as its designer block says (<c>Caption = "..."</c>): the VBE keeps it in the module's text, not in the designer's state, and applies it to
    /// the form it loads. <see langword="null"/> leaves the form's own.
    /// </summary>
    public string? Caption { get; init; }

    /// <summary>Where the form starts up. <see cref="FormStartUpPosition.CenterOwner"/>, as a new UserForm's designer has it, unless said otherwise.</summary>
    public FormStartUpPosition StartUpPosition { get; init; } = FormStartUpPosition.CenterOwner;

    /// <summary>Where a <see cref="FormStartUpPosition.Manual"/> form's window is, from the left of the desktop, in points.</summary>
    public double Left { get; init; }

    /// <summary>Where a <see cref="FormStartUpPosition.Manual"/> form's window is, from the top of the desktop, in points.</summary>
    public double Top { get; init; }

    /// <summary>
    /// Whether <c>Show</c> without an argument shows the form modal: its designer's <c>ShowModal</c>, which is <c>True</c> unless the designer block says otherwise.
    /// </summary>
    public bool ShowModal { get; init; } = true;

    /// <summary>
    /// The definition of the form a form module's designer declares: its state, and what its designer block says of its window.
    /// </summary>
    /// <param name="name">The name of the form's module.</param>
    /// <param name="designer">What the designer declares.</param>
    public static FormDefinition Of(string name, SDK.Workspace.Forms.FormDescriptor designer) => new(name, designer.State)
    {
        Caption = designer.Caption,
        StartUpPosition = designer.StartUpPosition is { } position && Enum.IsDefined((FormStartUpPosition)position) ? (FormStartUpPosition)position : FormStartUpPosition.CenterOwner,
        Left = PointsOf(designer.Properties.GetValueOrDefault("Left")),
        Top = PointsOf(designer.Properties.GetValueOrDefault("Top")),
        ShowModal = designer.ShowModal ?? true,
    };

    private static double PointsOf(string? value)
        => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var points) ? points : 0;
}

/// <summary>
/// Hosts the forms of a program: each in a window of its own, with the controls its designer gave it, shown and hidden as the program says.
/// </summary>
/// <remarks>
/// <para>
/// A form is an object of the automation server it is hosted by, and its handle is one like any other: what its own object model has - a UserForm's
/// <c>Caption</c>, its <c>Controls</c> - is called through <see cref="Automation.IAutomationServer.Invoke"/>, on the same thread, and its controls are objects of
/// that server too. What the host adds is the window, which is what VBA's form host adds to Microsoft Forms: the caption bar and its close button, where the form starts
/// up, and whether showing it waits.
/// </para>
/// <para>
/// ⚠️ An implementation is called from whichever thread the pipeline runs on and is responsible for running the form on the thread it needs, the one its automation
/// server's objects live on.
/// </para>
/// </remarks>
public interface IFormHost
{
    /// <summary>
    /// Whether this machine can host forms at all.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Creates a form from its definition, in a window that is not shown yet.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <returns>The handle of the form: the automation object its own object model is.</returns>
    /// <exception cref="Automation.AutomationException">
    /// Forms cannot be hosted here - the machine has no Microsoft Forms, or only one this process cannot load - or the designer's state cannot be read.
    /// </exception>
    object Create(FormDefinition form);

    /// <summary>
    /// Shows a form. A modal form is the only one its program's user can use until it is hidden, and showing it waits until it is; a modeless one is shown, and
    /// showing it returns at once.
    /// </summary>
    /// <remarks>
    /// While a modal form waits, what is asked of its thread is done: a handler of one of its events that hides it, from the program that waits.
    /// </remarks>
    /// <param name="form">The handle of the form.</param>
    /// <param name="modal">Whether it is shown modal.</param>
    /// <exception cref="Automation.AutomationException">The handle is not a form of this host, or the form was destroyed.</exception>
    void Show(object form, bool modal);

    /// <summary>
    /// Hides a form, which ends the wait of the <see cref="Show"/> that showed it modal. A form that is not shown is left as it is.
    /// </summary>
    /// <param name="form">The handle of the form.</param>
    /// <exception cref="Automation.AutomationException">The handle is not a form of this host, or the form was destroyed.</exception>
    void Hide(object form);

    /// <summary>
    /// Whether a form is shown.
    /// </summary>
    /// <param name="form">The handle of the form.</param>
    /// <exception cref="Automation.AutomationException">The handle is not a form of this host, or the form was destroyed.</exception>
    bool IsShown(object form);

    /// <summary>
    /// Gets a form's caption: its window's, which is what a UserForm's <c>Caption</c> is.
    /// </summary>
    /// <param name="form">The handle of the form.</param>
    /// <exception cref="Automation.AutomationException">The handle is not a form of this host, or the form was destroyed.</exception>
    string GetCaption(object form);

    /// <summary>
    /// Sets a form's caption.
    /// </summary>
    /// <param name="form">The handle of the form.</param>
    /// <param name="caption">The caption.</param>
    /// <exception cref="Automation.AutomationException">The handle is not a form of this host, or the form was destroyed.</exception>
    void SetCaption(object form, string caption);

    /// <summary>
    /// The focus leaves a form for good, as it does when the form is unloaded: the form is deactivated and closed, and the control that has the focus is left - the
    /// events of the extender MS-VBA wraps it in, its pending update (<c>BeforeUpdate</c>, asked once more when it is cancelled, then <c>AfterUpdate</c> unless it
    /// is cancelled again) and its <c>Exit</c>, whose cancel changes nothing (Excel-measured, <c>oracle\s41</c> E01, E05, E10, E11). The form's window stays until
    /// it is destroyed.
    /// </summary>
    /// <param name="form">The handle of the form.</param>
    /// <exception cref="Automation.AutomationException">The handle is not a form of this host, or the form was destroyed.</exception>
    void Leave(object form);

    /// <summary>
    /// Destroys a form and its window, and lets go of what it holds. A form that is shown is hidden first; one that was destroyed already is left as it is.
    /// </summary>
    /// <param name="form">The handle of the form.</param>
    void Destroy(object form);
}

/// <summary>
/// The events a form host raises for the window a form is in, on the form's handle, to whatever listens to the form's events: the program's side of the form, which
/// runs its handlers.
/// </summary>
/// <remarks>
/// <c>Activate</c> and <c>Deactivate</c> are the VBA form object's events, raised by name. The close button is not an event of the form: it asks the program to close
/// the form (<see cref="Close"/>), which is the program's to decide - its <c>QueryClose</c> can cancel it - and to do, as <c>Unload</c> does.
/// </remarks>
public static class FormWindowEvents
{
    /// <summary>The form's window is shown, and is the active form: the form object's <c>Activate</c>.</summary>
    public const string Activate = "Activate";

    /// <summary>Another form is shown, and is the active one: the form object's <c>Deactivate</c>.</summary>
    public const string Deactivate = "Deactivate";

    /// <summary>
    /// The user closed the form's window - its close button, Alt+F4: the program asks the form (<c>QueryClose</c>, with <c>vbFormControlMenu</c>) and unloads it
    /// unless it cancels. Not an event any handler is named after.
    /// </summary>
    public const string Close = "[Close]";
}

/// <summary>
/// The form host of a machine that has none: no form can be created.
/// </summary>
public sealed class UnavailableFormHost : IFormHost
{
    private const int ClassNotRegistered = unchecked((int)0x80040154);

    private UnavailableFormHost()
    {
    }

    /// <summary>The one instance.</summary>
    public static UnavailableFormHost Instance { get; } = new();

    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public object Create(FormDefinition form) => throw new Automation.AutomationException(ClassNotRegistered, ExternalMessages.FormsUnavailable);

    /// <inheritdoc/>
    public void Show(object form, bool modal) => throw new Automation.AutomationException(ClassNotRegistered, ExternalMessages.FormsUnavailable);

    /// <inheritdoc/>
    public void Hide(object form) => throw new Automation.AutomationException(ClassNotRegistered, ExternalMessages.FormsUnavailable);

    /// <inheritdoc/>
    public bool IsShown(object form) => false;

    /// <inheritdoc/>
    public string GetCaption(object form) => throw new Automation.AutomationException(ClassNotRegistered, ExternalMessages.FormsUnavailable);

    /// <inheritdoc/>
    public void SetCaption(object form, string caption) => throw new Automation.AutomationException(ClassNotRegistered, ExternalMessages.FormsUnavailable);

    /// <inheritdoc/>
    public void Leave(object form)
    {
    }

    /// <inheritdoc/>
    public void Destroy(object form)
    {
    }
}
