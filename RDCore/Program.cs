using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
namespace RDCore.LanguageServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var processTokenSource = new CancellationTokenSource();
        var host = new CoreLanguageServerAppHost(processTokenSource);

        try
        {
            await host.RunAsync(args);
        }
        catch (OperationCanceledException exception)
        {
            // normal exit
            Console.WriteLine(exception.Message);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.ToString());
            return -1;
        }

        return host.ExitCode;
    }
}