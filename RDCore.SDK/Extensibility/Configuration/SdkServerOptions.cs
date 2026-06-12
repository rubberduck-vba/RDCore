using Microsoft.Extensions.Logging;

namespace RDCore.SDK.Extensibility.Configuration;

/// <summary>
/// Server-level configuration settings, bound from <c>appsettings.json</c> or overridden from command-line arguments.
/// </summary>
public record class SdkServerOptions
{
    /// <summary>
    /// The process ID of the client process that starts a LSP server process.
    /// </summary>
    /// <remarks>
    /// 🧩 For an extension, the <c>ClientProcessID</c> is the process ID of the <em>language server</em> application.
    /// </remarks>
    public int ClientProcessId { get; set; }
    /// <summary>
    /// The minimum Microsoft.Extensions.Logging.LogLevel of a trace message that makes it through to the trace output.
    /// </summary>
    public LogLevel TraceLevel { get; set; }
    /// <summary>
    /// Whether verbose messages are generated or not.<br/>
    /// 🧩 Applications may configure the content of their verbose messages in more details.
    /// </summary>
    /// <remarks>
    /// ⚠️ Verbose messages may contain <strong>relatively sensitive information</strong> such as document locations, symbol names, and execution stack traces.<br/>
    /// ⚠️ Verbose messages are <strong>NEVER</strong> to be transmitted via any kind of telemetry.
    /// </remarks>
    public bool Verbose { get; set; }
    /// <summary>
    /// The <strong>number of seconds</strong> a client will await a server connection before a connection is aborted.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; }
    /// <summary>
    /// The <strong>number of seconds</strong> between health check pings.
    /// </summary>
    /// <remarks>
    /// 👉 Determines how long the server process remains orphaned before initiating a shutdown when the client process that owns it fails to respond, for example if it crashes or is otherwise abruptly terminated.
    /// </remarks>
    public int HealthCheckIntervalSeconds { get; set; }
    /// <summary>
    /// The <strong>number of seconds</strong> the server will wait for an <c>Exit</c> notification from the client after receiving a <c>Shutdown</c> request.
    /// </summary>
    /// <remarks>
    /// 👉 The server process <em>exit code</em> depends on whether the <c>Exit</c> notification was received after processing a <c>Shutdown</c> request.
    /// </remarks>
    public int ShutdownTimeoutSeconds { get; set; }
    /// <summary>
    /// 🧩 Allows the server platform to load <strong>unsigned extensions</strong>, which is useful for developing extensions with a private fork.
    /// </summary>
    /// <remarks>
    /// ⚠️ <strong>This setting has no effect unless explicitly overridden</strong> via a command-line argument.
    /// </remarks>
    public bool UnsafeDevMode { get; set; }
}
