using RDCore.Server;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
namespace RDCore.LanguageServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var app = new RDCoreServerApp();

        try
        {
            await app.RunAsync();
        }
        catch (OperationCanceledException)
        {
            // suppressed; normal exit
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            return -1;
        }

        return app.ServerStateProvider.State.ExitCode; // FIXME that's too deep
    }
}