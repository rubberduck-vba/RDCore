using System.Runtime.CompilerServices;

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
        finally
        {
            host.Dispose();
        }
        // background threads (Serilog.Async, OmniSharp Rx, console logger) can otherwise delay process exit.
        Environment.Exit(code);
        return code;
    }
}