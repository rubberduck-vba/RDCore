using Microsoft.Win32;
using RDCore.External.Automation;
using RDCore.External.Forms;
using RDCore.External.Windows.Automation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RDCore.External.Windows.Forms;

/// <summary>
/// The forms of a Windows machine: Microsoft Forms 2.0 forms - the library a VBA UserForm is, which comes with Office - each hosted in a window of its own, on the
/// apartment the automation server's objects live on.
/// </summary>
/// <remarks>
/// <para>
/// A form and its controls are COM objects of that apartment, so the program reaches them as it reaches any server's: <c>Me.Caption</c> is an
/// <see cref="IAutomationServer.Invoke"/> of the form's handle, on the same thread. While a modal form is shown, the apartment is pumped until the form is hidden,
/// and the calls asked of it meanwhile are made: a handler that hides the form, from the program that waits for it.
/// </para>
/// <para>
/// Microsoft Forms is loaded in the process, so its bitness is the process's: this host is 64-bit, and a machine with only the 32-bit Microsoft Forms of a 32-bit
/// Office is told so, rather than told that the library is not there.
/// </para>
/// </remarks>
/// <param name="apartment">The apartment: the automation server's, whose objects the forms' controls are.</param>
[SupportedOSPlatform("windows")]
public sealed class WindowsFormHost(ComApartment apartment) : IFormHost
{
    private const int ClassNotRegistered = unchecked((int)0x80040154);
    private const int InvalidArgument = unchecked((int)0x80070057);

    private readonly Dictionary<object, HostedForm> _forms = new(ReferenceEqualityComparer.Instance);
    private IDisposable? _keys;

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public object Create(FormDefinition form) => apartment.Invoke(() =>
    {
        var created = CreateForm();
        HostedForm hosted;
        try
        {
            hosted = HostedForm.Create(form, created);
        }
        catch (COMException failure)
        {
            _ = Marshal.FinalReleaseComObject(created);
            throw new AutomationException(failure.HResult, string.Format(System.Globalization.CultureInfo.CurrentCulture, ExternalMessages.FormStateUnreadable, form.Name, failure.Message));
        }

        // the keys of every form go to its active object first, which is what moves the focus with Tab.
        _keys ??= apartment.TranslateKeys(HostedForm.TranslateKey);
        _forms[created] = hosted;
        return created;
    })!;

    /// <inheritdoc/>
    public void Show(object form, bool modal) => apartment.Invoke<object?>(() =>
    {
        HostedOf(form).Show(apartment, modal);
        return null;
    });

    /// <inheritdoc/>
    public void Hide(object form) => apartment.Invoke<object?>(() =>
    {
        HostedOf(form).Hide();
        return null;
    });

    /// <inheritdoc/>
    public bool IsShown(object form) => apartment.Invoke(() => HostedOf(form).IsShown);

    /// <inheritdoc/>
    public string GetCaption(object form) => apartment.Invoke(() => HostedOf(form).Caption)!;

    /// <inheritdoc/>
    public void SetCaption(object form, string caption) => apartment.Invoke<object?>(() =>
    {
        HostedOf(form).Caption = caption;
        return null;
    });

    /// <inheritdoc/>
    public void Destroy(object form) => apartment.Invoke<object?>(() =>
    {
        if (_forms.Remove(form, out var hosted))
        {
            hosted.Destroy();
        }

        return null;
    });

    private HostedForm HostedOf(object form)
        => _forms.TryGetValue(form, out var hosted) && !hosted.IsDestroyed ? hosted : throw new AutomationException(InvalidArgument, ExternalMessages.NotAForm);

    // a new form, of the class the designer of a UserForm is: a machine that has only the 32-bit one is told it is the wrong one, not that it has none.
    private static object CreateForm()
    {
        if (!Environment.Is64BitProcess)
        {
            throw new AutomationException(ClassNotRegistered, ExternalMessages.FormsNeed64BitProcess);
        }

        var type = Type.GetTypeFromCLSID(SDK.Workspace.Forms.FormDesigner.MSFormsClassId, throwOnError: false);
        try
        {
            return type is null ? throw new COMException(null, ClassNotRegistered) : Activator.CreateInstance(type)!;
        }
        catch (COMException failure) when (failure.HResult == ClassNotRegistered)
        {
            throw new AutomationException(ClassNotRegistered, IsRegisteredFor32Bit() ? ExternalMessages.FormsOnly32Bit : ExternalMessages.FormsNotInstalled);
        }
    }

    private static bool IsRegisteredFor32Bit()
    {
        using var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32);
        using var server = classes.OpenSubKey($@"CLSID\{SDK.Workspace.Forms.FormDesigner.MSFormsClassId:B}\InprocServer32");
        return server is not null;
    }
}
