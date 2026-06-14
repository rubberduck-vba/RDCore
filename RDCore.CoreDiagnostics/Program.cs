using RDCore.SDK.Server;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
namespace RDCore.Diagnostics;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = new CoreDiagnosticsAppHost();        
        return await host.RunAsync(args);
    }
}
