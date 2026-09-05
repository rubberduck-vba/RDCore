using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
namespace RDCore.LanguageServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            using var host = new CoreLanguageServerHost();
            return await host.RunAsync(args);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.ToString());
            Console.ReadLine();
            return -1;
        }
    }
}