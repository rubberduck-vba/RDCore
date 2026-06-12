namespace RDCore.SDK.Extensibility.Configuration;

/// <summary>
/// Transport-level configuration. RDCore uses <see cref="ServerTransportLayerMode.NamedPipe"/> by default.
/// </summary>
public record class TransportOptions
{
    private const ServerTransportLayerMode _defaultTranportType = ServerTransportLayerMode.NamedPipe;
    /// <summary>
    /// The type of transport layer (platform-wide).
    /// </summary>
    /// <remarks>
    /// 👉 Only <see cref="ServerTransportLayerMode.NamedPipe"/> is supported for now, but <c>StdIO</c> and <c>Sockets</c> could technically be made to work as well.
    /// </remarks>
    public ServerTransportLayerMode Type { get; set; } = _defaultTranportType;
    /// <summary>
    /// Configuration settings for <em>named pipe</em>-enabled transport.
    /// </summary>
    public NamedPipeTransportOptions PipeConfig { get; set; } = new();
    /// <summary>
    /// Configration settings for <em>socket</em>-enabled transport.
    /// </summary>
    public SocketTransportOptions SocketConfig { get; set; } = new();
}
