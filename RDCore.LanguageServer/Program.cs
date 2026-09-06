using System.Runtime.CompilerServices;
using RDCore.SDK.Server;

[assembly: InternalsVisibleTo("RDCore.Tests")]
namespace RDCore.LanguageServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
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
        try { host.Dispose(); } catch (Exception exception) { Console.WriteLine(exception); }
        return code;
    }
}
