using CommandLine;
using Microsoft.Extensions.Logging;

namespace RDCore.SDK.Extensibility.Configuration;

/// <summary>
/// Configuration settings overridden from command-line arguments.
/// </summary>
/// <remarks>
/// Regroups all command-line arguments in one place, <em>sorted by alias / short name</em>.
/// </remarks>
public record class SdkAppCommandLineArgs
{
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkServerOptions.ConnectTimeoutSeconds"/> setting.
    /// </summary>
    [Option('c', "connect-timeout")]
    public int? ConnectTimeoutSeconds { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkWorkspaceOptions.DefaultLocation"/> setting.
    /// </summary>
    [Option('d', "default-location")]
    public string? DefaultLocation { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the application startup sequence and outputs command-line arguments documentation instead.
    /// </summary>
    [Option('h', "help")]
    public bool? ShowHelp { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkServerOptions.HealthCheckIntervalSeconds"/> setting.
    /// </summary>
    [Option('k', "healthcheck-timeout")]
    public int? HealthCheckIntervalSeconds { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="TransportOptions.HealthCheckIntervalSeconds"/> setting.
    /// </summary>
    [Option('m', "mode")]
    public ServerTransportLayerMode Type { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="NamedPipeTransportOptions.PipeName"/> setting.
    /// </summary>
    [Option('n', "name")]
    public string? PipeName { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that provides the <see cref="SdkServerOptions.ClientProcessId"/> owner process ID to a server app.
    /// </summary>
    /// <remarks>
    /// 👉 This argument is required for starting a server app.
    /// </remarks>
    [Option('p', "client-id")]
    public int? ClientProcessId { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkServerOptions.ShutdownTimeoutSeconds"/> setting.
    /// </summary>
    [Option('s', "shutdown-timeout")]
    public int? ShutdownTimeoutSeconds { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkServerOptions.TraceLevel"/> setting.
    /// </summary>
    [Option('t', "trace")]
    public LogLevel? TraceLevel { get; init; }
    /// <summary>
    /// A <em>command-line switch</em> that overrides the <see cref="SdkServerOptions.TraceLevel"/> setting.
    /// </summary>
    /// <remarks>
    /// 👉 This switch has <strong>no alias</strong> and does not appear in <em>help documentation</em>.
    /// </remarks>
    [Option("unsafe-dev-mode", Hidden = true)]
    public bool? UnsafeDevMode { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkServerOptions.Verbose"/> setting.
    /// </summary>
    [Option('v', "verbose")]
    public bool? Verbose { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkWorkspaceOptions.WorkspaceUri"/> setting.
    /// </summary>
    /// <remarks>
    /// 👉 This argument is required for starting a server app.
    /// </remarks>
    [Option('w', "workspace")]
    public string? WorkspaceUri { get; init; }
}
