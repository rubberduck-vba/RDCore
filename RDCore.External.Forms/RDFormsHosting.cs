using RDCore.External.Automation;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;
using System.Globalization;

namespace RDCore.External.Forms.RDForms;

/// <summary>
/// What hosts the forms of a Visual Basic 6.0 project.
/// </summary>
public enum VisualBasicFormsHost
{
    /// <summary>
    /// RDForms, in windows of its own.
    /// </summary>
    RDForms,

    /// <summary>
    /// Nothing: a Visual Basic 6.0 form cannot be shown, as on a machine with no form host.
    /// </summary>
    Off,
}

/// <summary>
/// How the forms of an external host are hosted: the <c>Configuration:Forms</c> section of the settings.
/// </summary>
public sealed class FormsOptions
{
    /// <summary>
    /// What hosts the forms of a Visual Basic 6.0 project (<c>Forms:VB</c>). <see cref="VisualBasicFormsHost.RDForms"/> unless said otherwise.
    /// </summary>
    public VisualBasicFormsHost VB { get; set; } = VisualBasicFormsHost.RDForms;

    /// <summary>
    /// Whether the RDForms windows are headless, shown on no screen (<c>Forms:Headless</c>). <see langword="false"/> unless said otherwise.
    /// </summary>
    public bool Headless { get; set; }
}

/// <summary>
/// The servers and the form host of an external host that hosts RDForms forms beside the servers of its platform.
/// </summary>
public static class RDFormsHosting
{
    /// <summary>
    /// Composes the automation server and the form host of an external host: RDForms objects go to RDForms, and every other object to
    /// <paramref name="platform"/>. A Visual Basic 6.0 form is hosted by RDForms unless <paramref name="options"/> turns it off, and any other form by
    /// <paramref name="microsoftForms"/>.
    /// </summary>
    /// <param name="platform">The platform's own automation server.</param>
    /// <param name="microsoftForms">The form host of VBA UserForms.</param>
    /// <param name="options">How the forms are hosted.</param>
    /// <returns>The automation server and the form host to serve.</returns>
    public static (IAutomationServer Automation, IFormHost Forms) Compose(IAutomationServer platform, IFormHost microsoftForms, FormsOptions options)
    {
        if (options.VB == VisualBasicFormsHost.Off)
        {
            return (platform, new RoutingFormHost(null, microsoftForms));
        }

        var server = new RDFormsAutomationServer(options.Headless ? RDFormsPlatform.Headless : RDFormsPlatform.Desktop);
        return (new RoutingAutomationServer(server, platform), new RoutingFormHost(new RDFormsFormHost(server), microsoftForms));
    }

    // the objects of RDForms to RDForms, by their handles, and everything else to the platform's server: creating an object by its name included.
    private sealed class RoutingAutomationServer(RDFormsAutomationServer forms, IAutomationServer platform) : IAutomationServer
    {
        // RDForms' objects are there to reach on any platform, the controls of its forms included; an object the platform's server cannot create raises 429
        // when it is asked for, as on a machine with no automation servers.
        public bool IsAvailable => true;

        public void Prepare() => platform.Prepare();

        public object CreateObject(string progId) => platform.CreateObject(progId);

        public object GetObject(ExternalObjectRequest request) => platform.GetObject(request);

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
            => ServerOf(target).Invoke(target, member, invocation, arguments, byReference, culture);

        public string? ClassNameOf(object target) => ServerOf(target).ClassNameOf(target);

        public bool? IsInstanceOf(object target, string library, string className) => ServerOf(target).IsInstanceOf(target, library, className);

        public bool MoveNext(object enumerator, out object? current) => ServerOf(enumerator).MoveNext(enumerator, out current);

        public void Reset(object enumerator) => ServerOf(enumerator).Reset(enumerator);

        public void Advise(object source, IAutomationEventSink sink) => ServerOf(source).Advise(source, sink);

        public void Unadvise(object source) => ServerOf(source).Unadvise(source);

        public void Release(object handle) => ServerOf(handle).Release(handle);

        private IAutomationServer ServerOf(object handle) => forms.Owns(handle) ? forms : platform;
    }

    // a Visual Basic 6.0 form to RDForms when it hosts them, and nothing when it does not; any other form to the host of UserForms. A form is called through the
    // host that created it.
    private sealed class RoutingFormHost(RDFormsFormHost? visualBasic, IFormHost microsoftForms) : IFormHost
    {
        public bool IsAvailable => visualBasic is not null || microsoftForms.IsAvailable;

        public object Create(FormDefinition form) => form.Kind == FormDesignerKind.VisualBasic6
            ? ((IFormHost?)visualBasic ?? UnavailableFormHost.Instance).Create(form)
            : microsoftForms.Create(form);

        // an owner from another host is not one of this host's forms.
        public void Show(object form, bool modal, object? owner = null)
        {
            var host = HostOf(form);
            host.Show(form, modal, owner is not null && ReferenceEquals(HostOf(owner), host) ? owner : null);
        }

        public void Hide(object form) => HostOf(form).Hide(form);

        public bool IsShown(object form) => HostOf(form).IsShown(form);

        public string GetCaption(object form) => HostOf(form).GetCaption(form);

        public void SetCaption(object form, string caption) => HostOf(form).SetCaption(form, caption);

        public FormBounds GetBounds(object form) => HostOf(form).GetBounds(form);

        public void SetBounds(object form, FormBounds bounds) => HostOf(form).SetBounds(form, bounds);

        public void Leave(object form) => HostOf(form).Leave(form);

        public void Destroy(object form) => HostOf(form).Destroy(form);

        public void Hold(bool running)
        {
            ((IFormHost?)visualBasic)?.Hold(running);
            microsoftForms.Hold(running);
        }

        public void DoEvents()
        {
            visualBasic?.DoEvents();
            microsoftForms.DoEvents();
        }

        private IFormHost HostOf(object form) => visualBasic is not null && visualBasic.Owns(form) ? visualBasic : microsoftForms;
    }
}
