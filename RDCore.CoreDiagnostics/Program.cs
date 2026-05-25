using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services.States;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
namespace RDCore.Diagnostics;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = Args.Parse(args);
        var stateProvider = new ServerStateProvider(options);

        try
        {
            await new RDCoreExtensionServerApp(stateProvider).RunAsync();
        }
        catch (OperationCanceledException)
        {
            // suppressed; normal exit
            Console.WriteLine("VIVAT CUCUMIS\n(C) Copyright 2026 9562-7303 Québec inc.");
        }
        catch (Exception exception)
        {
            // unexpected exit.. and we don't have a logger anymore.
            // hopefully a warning was logged somewhere along the way...
            // this is just a last resort to make it known something went wrong.
            Console.WriteLine(exception);
        }

        return stateProvider.State.ExitCode;
    }
}