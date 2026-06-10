using RDCore.SDK.Server;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
namespace RDCore.Diagnostics;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        using var host = new RDCoreLanguageServerHost(cts);
        
        try
        {
            await host.RunAsync(args);
        }
        catch (OperationCanceledException)
        {
            // exception suppressed, normal exit
            Console.WriteLine("VIVAT CUCUMIS\n(C) Copyright 2026 9562-7303 Québec inc.");
        }
        catch (Exception exception)
        {
            // unexpected exit.. and we don't have a logger anymore.
            // hopefully a warning was logged somewhere along the way...
            // this is just a last resort to make it known something went wrong.
            Console.WriteLine(exception);
        }

        return host.ExitCode;
    }
}