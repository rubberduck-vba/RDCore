using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
namespace RDCore.LanguageServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var host = new CoreLanguageServerHost();
        return await host.RunAsync(args);
    }
}