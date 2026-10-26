using System.Runtime.CompilerServices;
using RDCore.SDK.Server;
using RDCore.SDK.Client;

[assembly: InternalsVisibleTo("RDCore.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

// platform capabilities the language server provides to a connected client:
[assembly: ProvidesCorePlatformClientCapability<SessionStatus>]
[assembly: ProvidesCorePlatformClientCapability<SessionExecute>]
[assembly: ProvidesCorePlatformClientCapability<SessionDiscard>]
[assembly: ProvidesCorePlatformClientCapability<ProgramDebugging>]
[assembly: ProvidesCorePlatformClientCapability<SessionAnalyze>]
[assembly: ProvidesCorePlatformClientCapability<SessionCompletion>]
[assembly: ProvidesCorePlatformClientCapability<SessionSignatureHelp>]
[assembly: ProvidesCorePlatformClientCapability<SessionMemoryAccess>]
// it relays the dialogs of the program that runs in the environment host to a client that answers them:
[assembly: ProvidesCorePlatformClientCapability<SessionInteraction>]
namespace RDCore.LanguageServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // the same platform, spoken to by a debugger client over standard input and output.
        if (RDCore.LanguageServer.Debugging.DebugAdapterEntry.IsRequested(args))
        {
            var adapterCode = await RDCore.LanguageServer.Debugging.DebugAdapterEntry.RunAsync(args);
            ProcessWatchdog.Arm(adapterCode);
            return adapterCode;
        }

        var host = new CoreLanguageServerHost();
        int code;
        try
        {
            code = await host.RunAsync(args);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.ToString());
            code = -1;
        }

        // the shutdown sequence is bounded and returns promptly; this only guards against a wedged
        // background thread (Serilog.Async, OmniSharp Rx) keeping the process alive past a clean exit.
        ProcessWatchdog.Arm(code);

        try
        {
            host.Dispose();
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.ToString());
        }

        return code;
    }
}
