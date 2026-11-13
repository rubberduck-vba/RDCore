using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RDCore.External.Automation;
using RDCore.External.Hosting;
using RDCore.External.Native;
using RDCore.External.Windows.Automation;
using RDCore.External.Windows.Forms;
using RDCore.External.Windows.Native;
using RDCore.SDK.Server;

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
public sealed class MachineExternalHost(string? logFileName) : RDCorePlatformServerHost<ExternalHostApp>
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        base.ConfigureAdditionalExternalServices(services, configuration);
        services
            .AddSingleton(AutomationService())
            .AddSingleton(new NativeCallService(
                OperatingSystem.IsWindows() ? new WindowsNativePlatform() : UnsupportedNativePlatform.Instance));
    }

    // on Windows, the servers and the form host live on one apartment: a form's controls are objects of the servers, called on the thread its window is pumped on.
    private static ExternalAutomationService AutomationService()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ExternalAutomationService(UnavailableAutomationServer.Instance);
        }

        var apartment = new ComApartment();
        return new ExternalAutomationService(new ComAutomationServer(apartment), new WindowsFormHost(apartment));
    }

    protected override string? LogFileName => logFileName;
}
