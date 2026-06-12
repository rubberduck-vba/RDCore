namespace RDCore.SDK.Extensibility.Configuration;

/// <summary>
/// Configures <em>socket</em> transport layer options.
/// </summary>
/// <remarks>
/// ⚠️ Sockets are NOT inherently local and therefore <strong>may be blocked by policy and/or firewalls</strong>. They do open up some interesting possibilities though.
/// </remarks>
public record class SocketTransportOptions
{
    private const int _defaultPort = 56789;
    /// <summary>
    /// The port to use for cross-process JSON-RPC LSP communications.
    /// </summary>
    /// <remarks>
    /// 👉 Recommended range would be <c>49152-65535</c> for ephemeral sockets (dev), or <c>1024-49151</c> for registered sockets (prod).
    /// </remarks>
    public int Port { get; set; } = _defaultPort;
}
