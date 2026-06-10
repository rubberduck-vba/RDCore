using Microsoft.Extensions.Logging;
using RDCore.SDK.Server;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
namespace RDCore.LanguageServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var processTokenSource = new CancellationTokenSource();
        var app = new RDCoreLanguageServerHost(processTokenSource);

        try
        {
            await app.RunAsync(args);
        }
        catch (OperationCanceledException exception)
        {
            app.LogIfEnabled(LogLevel.Debug, exception.Message);
        }
        catch (Exception exception)
        {
            app.LogIfEnabled(LogLevel.Critical, exception.ToString());
            return -1;
        }

        return app.ExitCode;
    }
}