using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// The RDCore <strong>debug adapter</strong> application host: the platform, on a protocol a debugger client speaks.
/// </summary>
/// <remarks>
/// 👉 <strong>Standard output is the protocol.</strong> Nothing but the adapter writes to it, and so every other writer to the console is sent to standard error (see
/// <see cref="DebugAdapterEntry"/>), and the processes this one starts are given no console (<see cref="SdkServerOptions.CaptureChildOutput"/>).
/// </remarks>
/// <param name="transport">What the client is talked to over.</param>
internal sealed class DebugAdapterHost(DebugAdapterTransport transport) : AppHost<DebugAdapterApp>
{
    protected override void Configure(IConfigurationBuilder configuration, IServiceCollection services, string[] args)
    {
        var parsed = Parser.Default.ParseArguments<SdkAppCommandLineArgs>(args).Value
            ?? throw new ArgumentException($"Could not parse command-line arguments: {string.Join(" ", args)}");

        // a debug adapter debugs a workspace, and cannot start without one.
        var workspace = parsed.WorkspaceUri ?? throw new ArgumentNullException(nameof(SdkAppCommandLineArgs.WorkspaceUri));

        // the components of the platform are told the workspace as a URI, which writes the drive of a path in lower case; and every symbol is addressed by the root it
        // was defined under. The root is said once, the way they will all say it.
        parsed = parsed with { WorkspaceUri = ((DocumentUri)workspace).GetFileSystemPath() };

        configuration.AddInMemoryCollection(parsed.ToConfigurationOverrides());
        configuration.AddInMemoryCollection([new("Configuration:Server:CaptureChildOutput", bool.TrueString)]);
    }

    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
        => services
            .AddPlatformCoordination(Info.Version ?? new Version(0, 0, 0))
            .AddSingleton(transport)
            .AddSingleton<IDebugWorkspace, PlatformDebugWorkspace>()
            .AddSingleton<ProgramDebugAdapter>();

    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.AddFile(
            Path.Combine(PlatformEnvironment.Default.LogsDirectory, "RDCore.DebugAdapter.log"),
            ResolveTraceLevel(configuration));

        base.ConfigureExternalLogging(services, builder, configuration);
    }
}

/// <summary>
/// Starts the language server's executable as a debug adapter: <c>RDCore.LanguageServer --dap --workspace &lt;path&gt;</c>.
/// </summary>
internal static class DebugAdapterEntry
{
    /// <summary>The switch that selects the debug adapter.</summary>
    public const string Switch = "--dap";

    /// <summary>
    /// Whether the command line asks for the debug adapter.
    /// </summary>
    /// <param name="args">The command-line arguments as received.</param>
    public static bool IsRequested(string[] args) => args.Contains(Switch, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs the debug adapter over the standard input and output of the process.
    /// </summary>
    /// <param name="args">The command-line arguments as received.</param>
    /// <returns>The exit code of the process.</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        // the streams are taken before anything else can write to the console: from here on the console is the error stream, and the output stream is the adapter's.
        var transport = new DebugAdapterTransport(Console.OpenStandardInput(), Console.OpenStandardOutput());
        Console.SetOut(Console.Error);

        var rest = args.Where(arg => !string.Equals(arg, Switch, StringComparison.OrdinalIgnoreCase)).ToArray();
        using var host = new DebugAdapterHost(transport);
        return await host.RunAsync(rest);
    }
}
