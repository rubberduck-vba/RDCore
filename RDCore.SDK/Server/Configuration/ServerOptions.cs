using CommandLine;

namespace RDCore.SDK.Server.Configuration;

public record class ServerOptions
{
    private const int DefaultConnectTimeoutSeconds = 30;
    private const int DefaultHealthCheckIntervalSeconds = 5;
    private const int DefaultShutdownTimeoutSeconds = 10;
    private const int DefaultMaximumInstances = 16;

    /// <summary>
    /// Interval (in seconds) between client process health checks.
    /// </summary>
    [Option('i', "healthcheck-interval", Required = false, Default = DefaultHealthCheckIntervalSeconds, HelpText = "Interval (in seconds) between client process health checks.")]
    public int HealthCheckIntervalSeconds { get; init; } = DefaultHealthCheckIntervalSeconds;

    /// <summary>
    /// Enable verbose output. Can be changed later by sending a SetTrace request from the client process.
    /// </summary>
    [Option('v', "verbose", Required = false, HelpText = "Enable verbose output. Can be changed later by sending a SetTrace request from the client process.")]
    public bool Verbose { get; init; } = false;

    /// <summary>
    /// The maximum number of seconds the server will wait for an Exit notification after receiving a Shutdown request.
    /// </summary>
    [Option('t', "shutdown-timeout", Required = false, Default = DefaultShutdownTimeoutSeconds, HelpText = "The maximum number of seconds the server will wait for an Exit notification after receiving a Shutdown request.")]
    public int ShutdownTimeoutSeconds { get; init; } = DefaultShutdownTimeoutSeconds;

    /// <summary>
    /// The maximum number of seconds the server will await a client connection before aborting.
    /// </summary>
    [Option('c', "connect-timeout", Required = false, Default = DefaultConnectTimeoutSeconds, HelpText = "The maximum number of seconds the server will await a client connection before aborting.")]
    public int ConnectTimeoutSeconds { get; init; } = DefaultConnectTimeoutSeconds;

    /// <summary>
    /// The maximum number of named pipe instances that can run concurrently.
    /// </summary>
    [Option('m', "max-instances", Required = false, Default = DefaultMaximumInstances, HelpText = "The maximum number of named pipe instances that can run concurrently.")]
    public int MaximumInstances { get; init; } = DefaultMaximumInstances;
}
