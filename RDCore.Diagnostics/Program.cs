using RDCore.SDK.Client;
using RDCore.SDK.Server;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]

// advertised so `rdc.exe describe-ext` records it in this extension's manifest; the diagnostics
// extension will contribute a diagnose verb once the extension command path is built out.
[assembly: ProvidesCorePlatformClientCapability<CliCommand>]

namespace RDCore.Diagnostics;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = new CoreDiagnosticsAppHost();        
        return await host.RunAsync(args);
    }
}
