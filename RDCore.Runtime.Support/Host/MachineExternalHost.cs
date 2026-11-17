using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RDCore.External.Automation;
using RDCore.External.Forms;
using RDCore.External.Forms.RDForms;
using RDCore.External.Hosting;
using RDCore.External.Native;
using RDCore.External.Windows.Automation;
using RDCore.External.Windows.Forms;
using RDCore.External.Windows.Native;
using RDCore.SDK.Server;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Support.Host;

/// <summary>
/// The external host of a program: the process that makes the program's calls to the outside world of the machine (its automation servers, its forms and
/// its native libraries), started by the process that runs the program, from its own executable, with <c>RDCORE_MODE=external</c>.
/// </summary>
/// <remarks>
/// ⚖️ GPLv3. <c>rdc</c> runs as one for the environment host; a built program runs as its own (<see cref="Build.BuiltProgram"/>), since its folder holds no
/// <c>rdc</c>.
/// </remarks>
/// <param name="logFileName">The file it logs to, in the platform's logs folder; none when null.</param>
/// <param name="hostsRDForms">
/// Whether it hosts Visual Basic 6.0 forms in RDForms. A built program with no such form is built without RDForms, and its host loads nothing of it.
/// </param>
public sealed class MachineExternalHost(string? logFileName, bool hostsRDForms = true) : RDCorePlatformServerHost<ExternalHostApp>
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        base.ConfigureAdditionalExternalServices(services, configuration);
        services
            .AddSingleton(AutomationService(configuration, hostsRDForms))
            .AddSingleton(new NativeCallService(
                OperatingSystem.IsWindows() ? new WindowsNativePlatform() : UnsupportedNativePlatform.Instance));
    }

    // on Windows, the servers and the UserForms' host live on one apartment: a UserForm's controls are objects of the servers, called on the thread its window is
    // pumped on. The RDForms forms are beside them, on a UI thread of their own, and their objects need no apartment.
    private static ExternalAutomationService AutomationService(IConfiguration configuration, bool rdForms)
    {
        if (!OperatingSystem.IsWindows())
        {
            var (anywhere, forms) = Compose(UnavailableAutomationServer.Instance, UnavailableFormHost.Instance, configuration, rdForms);
            return new ExternalAutomationService(anywhere, forms);
        }

        var apartment = new ComApartment();
        var (automation, host) = Compose(new ComAutomationServer(apartment), new WindowsFormHost(apartment), configuration, rdForms);
        return new ExternalAutomationService(automation, host);
    }

    private static (IAutomationServer Automation, IFormHost Forms) Compose(IAutomationServer platform, IFormHost microsoftForms, IConfiguration configuration,
        bool rdForms)
        => rdForms ? ComposeRDForms(platform, microsoftForms, configuration) : (platform, microsoftForms);

    // the only method that names a type of RDForms' host: compiled only when it is called, so that a host whose folder holds no RDForms never loads it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IAutomationServer Automation, IFormHost Forms) ComposeRDForms(IAutomationServer platform, IFormHost microsoftForms, IConfiguration configuration)
        => RDFormsHosting.Compose(platform, microsoftForms, configuration.GetSection("Configuration:Forms").Get<FormsOptions>() ?? new FormsOptions());

    protected override string? LogFileName => logFileName;
}
