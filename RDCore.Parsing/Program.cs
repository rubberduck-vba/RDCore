using Microsoft.Extensions.Logging;
using RDCore.SDK.Server;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
namespace RDCore.Parsing;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var processTokenSource = new CancellationTokenSource();
        using var host = new RDCoreLanguageServerHost(processTokenSource);

        try
        {
            await host.RunAsync(args);
        }
        catch (OperationCanceledException)
        {
            // suppressed; normal exit
            host.LogIfEnabled(LogLevel.Information, "VIVAT CUCUMIS\n(C) Copyright 2026 9562-7303 Québec inc.");
        }
        catch (Exception exception)
        {
            // unexpected exit.. and we don't have a logger anymore.
            // hopefully a warning was logged somewhere along the way...
            // this is just a last resort to make it known something went wrong.
            host.LogIfEnabled(LogLevel.Critical, exception.ToString());
        }

        return host.ExitCode;
    }
}