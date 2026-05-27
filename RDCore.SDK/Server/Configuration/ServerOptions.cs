using CommandLine;

namespace RDCore.SDK.Server.Configuration;

public enum ServerTransportLayerMode
{
    NamedPipe,
    //StdIO,
    //Sockets,
}

public record class ServerOptions
{
    private const int DefaultConnectTimeoutSeconds = 30;
    private const int DefaultHealthCheckIntervalSeconds = 5;
    private const int DefaultShutdownTimeoutSeconds = 10;
    private const int DefaultMaximumInstances = 16;

    private const ServerTransportLayerMode DefaultTransportLayer = ServerTransportLayerMode.NamedPipe;

    /// <summary>
    /// The maximum number of seconds the server will await a client connection before aborting.
    /// </summary>
    [Option('c', "ConnectTimeoutSeconds", Required = false, Default = DefaultConnectTimeoutSeconds, ResourceType = typeof(ServerOptionsHelpText))]
    public int ConnectTimeoutSeconds { get; init; } = DefaultConnectTimeoutSeconds;

    /// <summary>
    /// Interval (in seconds) between client process health checks.
    /// </summary>
    [Option('h', "HealthCheckIntervalSeconds", Required = false, Default = DefaultHealthCheckIntervalSeconds, ResourceType = typeof(ServerOptionsHelpText))]
    public int HealthCheckIntervalSeconds { get; init; } = DefaultHealthCheckIntervalSeconds;

    /// <summary>
    /// The maximum number of named pipe instances that can run concurrently.
    /// </summary>
    [Option('m', "MaximumInstances", Required = false, Default = DefaultMaximumInstances, ResourceType = typeof(ServerOptionsHelpText))]
    public int MaximumInstances { get; init; } = DefaultMaximumInstances;

    /// <summary>
    /// The name of the <c>named pipe</c> connection, if the <c>TransportLayer</c> is set to <c>ServerTransportLayerMode.NamedPipe</c>.
    /// </summary>
    /// <remarks>
    /// Since <c>ServerTransportLayerMode.NamedPipe</c> is the only supported transport layer mode, this property is <c>required</c>.
    /// </remarks>
    [Option('n', "PipeName", Required = true, ResourceType = typeof(ServerOptionsHelpText))]
    public required string PipeName { get; init; }

    /// <summary>
    /// The port to use for a TCP/socket connection, if the <c>TransportLayer</c> is set to <c>ServerTransportLayerMode.Socket</c>.
    /// </summary>
    /// <remarks>
    /// Since <c>ServerTransportLayerMode.NamedPipe</c> is the only supported transport layer mode, this property is ignored.
    /// </remarks>
    [Option('p', "Port", Required = false, ResourceType = typeof(ServerOptionsHelpText))]
    public int Port { get; init; }

    /// <summary>
    /// The maximum number of seconds the server will wait for an Exit notification after receiving a Shutdown request.
    /// </summary>
    [Option('s', "ShutdownTimeoutSeconds", Required = false, Default = DefaultShutdownTimeoutSeconds, ResourceType = typeof(ServerOptionsHelpText))]
    public int ShutdownTimeoutSeconds { get; init; } = DefaultShutdownTimeoutSeconds;

    /// <summary>
    /// Sets the ServerTransportLayerMode of the client/server application (the same transport layer mode must be supported on both sides of the communication streams).
    /// </summary>
    /// <remarks>
    /// 🧩 Only <c>ServerTransportLayerMode.NamedPipe</c> is supported at the SDK level. 
    /// Additional transport modes may be implemented, but there are important trade-offs to consider.
    /// </remarks>
    [Option('t', "TransportLayer", Required = false, Default = ServerTransportLayerMode.NamedPipe, ResourceType = typeof(ServerOptionsHelpText))]
    public ServerTransportLayerMode TransportLayer { get; init; } = DefaultTransportLayer;

    /// <summary>
    /// Enable verbose output. Can be changed later by sending a SetTrace request from the client process.
    /// </summary>
    [Option('v', "Verbose", Required = false, ResourceType = typeof(ServerOptionsHelpText))]
    public bool Verbose { get; init; } = false;

    /// <summary>
    /// An optional workspace or document URI to launch the server application with.
    /// </summary>
    /// <remarks>
    /// 🧩 This workspace <c>Uri</c> can be exchanged between the client and server during the LSP initialization handshake.
    /// </remarks>
    [Option('w', "WorkspaceUri", Required = false, ResourceType = typeof(ServerOptionsHelpText))]
    public string? WorkspaceUri { get; init; }
}
