using CommandLine;
using Microsoft.Extensions.Logging;

namespace RDCore.SDK.Server.Configuration;

public enum ServerTransportLayerMode
{
    NamedPipe,
    //StdIO,
    //Sockets,
}

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
    [Option('d', "default-location", Group = "workspace", Required = false)]
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
    [Option('m', "mode", Group = "transport", Required = false)]
    public ServerTransportLayerMode Type { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="NamedPipeTransportOptions.PipeName"/> setting.
    /// </summary>
    [Option('n', "name", Group = "pipe", Required = false)]
    public string? PipeName { get; init; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SocketTransportOptions.Port"/> setting.
    /// </summary>
    /// <remarks>
    /// <see cref="TransportOptions"/> does not currently support this setting.
    /// </remarks>
    [Option('p', "port", Group = "socket", Required = false)]
    public string? Port { get; init; }
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
    [Option('w', "workspace")]
    public string? WorkspaceUri { get; init; }
}

/// <summary>
/// Configuration settings, bound from <c>appsettings.json</c> or overridden from command-line arguments.
/// </summary>
public record class SdkAppOptions
{
    /// <summary>
    /// LSP Server options.
    /// </summary>
    /// <remarks>
    /// Most of these settings may be overridden through command-line arguments.
    /// </remarks>
    public SdkServerOptions Server { get; set; } = new();
    /// <summary>
    /// Workspace options.
    /// </summary>
    public SdkWorkspaceOptions Workspace { get; set; } = new();
    /// <summary>
    /// Platform options.
    /// </summary>
    public SdkPlatformOptions Platform { get; set; } = new();
}

/// <summary>
/// Server-level configuration settings, bound from <c>appsettings.json</c> or overridden from command-line arguments.
/// </summary>
public record class SdkServerOptions
{
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
    /// ⚠️ Allows the server platform to load <strong>unsigned plug-ins</strong>.
    /// </summary>
    /// <remarks>
    /// 🧩 This is useful for developing plug-ins with a private fork.<br/>
    /// This setting is <strong>read-only</strong> and must be explicitly overridden via a command-line argument.
    /// </remarks>
    public bool UnsafeDevMode { get; init; }
}

/// <summary>
/// Workspace-level configuration settings, bound from <c>appsettings.json</c> or overridden from command-line arguments.
/// </summary>
public record class SdkWorkspaceOptions
{
    private const string _defaultLocation = "%USERAPPDATA%/RDCore/Workspaces";
    private const string _defaultWorkspaceUri = "/Project1";

    /// <summary>
    /// The default location of RDCore workspaces (source files) on disk.
    /// </summary>
    /// <remarks>
    /// 👉 This location is used as a root when the <c>WorkspaceUri</c> is <strong>relative</strong>.
    /// </remarks>
    public string DefaultLocation { get; set; } = _defaultLocation;
    /// <summary>
    /// The location of the workspace to work with if none is supplied.
    /// </summary>
    /// <remarks>
    /// This setting is for <strong>client applications</strong>. The workspace location a LSP server application should work with should be supplied by a command-line argument <em>or</em> as a parameter in the <c>Initialize</c> request.<br/>
    /// ⚠️ If a workspace <c>Uri</c> is not supplied, a server application should exit with an error code.
    /// </remarks>
    public string WorkspaceUri { get; set; } = _defaultWorkspaceUri;
}

/// <summary>
/// Platform-level configuration settings, bound from <c>appsettings.json</c>.
/// </summary>
public record class SdkPlatformOptions
{
    private const string _defaultBaseUrl = "https://rubberduckvba.ca";
    private const string _defaultApiEndpoint = "/api";
    private const string _defaultServerExecutable = "./RDCore.LanguageServer.exe";
    private const string _defaultPluginsLocation = "./plugins";

    /// <summary>
    /// The base URL for the platform's cloud and online services.
    /// </summary>
    public string BaseWebUrl { get; set; } = _defaultBaseUrl;
    /// <summary>
    /// The URL (relative to the <c>BaseWebUrl</c>) of the backend API.
    /// </summary>
    public string ApiEndpoint { get; set; } = _defaultApiEndpoint;
    /// <summary>
    /// The location of the RDCore LSP language server executable.
    /// </summary>
    /// <remarks>
    /// 👉 This setting is used by <strong>client applications</strong> to locate the RDCore LSP language server executable.<br/>
    /// <strong>DO NOT</strong> modify this setting unless the name of the RDCore.LanguageServer assembly is also changed with it.
    /// </remarks>
    public string ServerExecutable { get; set; } = _defaultServerExecutable;
    /// <summary>
    /// The location RDCore plugins are discovered from. Plugins are identified by a folder containing a RDCore plugin manifest.<br/>
    /// ⚠️ Modifying this setting for a plugin <strong>will</strong> break the RDCore platform.
    /// </summary>
    /// <remarks>
    /// 🧩 If any extensions support plugins, they should be loaded from a subfolder <em>under their own <c>./plugins</c> folder</em>, 
    /// such that the RDCore directory <strong>remains self-contained</strong> with its entire ecosystem.
    /// </remarks>
    public string Plugins { get; set; } = _defaultPluginsLocation;
    /// <summary>
    /// Configures platform-wide transport layer settings.
    /// </summary>
    public TransportOptions Transport { get; set; } = new();
}
/// <summary>
/// Platform-level MSAL (Microsoft Authentication Layer) configuration options.
/// </summary>
/// <remarks>
/// 🧩 Forks that do not wish to leverage the RDCore cloud infrastructure (or implement their own) can do so by overwriting these settings 
/// if authentication with <strong>Microsoft Entra</strong> is a requirement. There is no SDK-level support for other identity providers.
/// </remarks>
public record class MsalOptions
{
    private const string _defaultTenantId = "6709cc6b-06d2-4773-a8d0-3c4aa50d54d8";
    private const string _defaultClientId = "TBD";
    private const string _defaultRedirectUrl = "/signin-rdcore";
    private const string _defaultApiScope = $"api://{_defaultClientId}/api.access";

    /// <summary>
    /// The <strong>Microsoft Entra</strong> tenant ID acting as the <em>authentication authority.</em>
    /// </summary>
    public string TenantId { get; set; } = _defaultTenantId;
    /// <summary>
    /// The <strong>Microsoft Entra</strong> client ID identifying the RDCore cloud application.
    /// </summary>
    public string ClientId { get; set; } = _defaultClientId;
    /// <summary>
    /// The <em>redirect URL</em> used for RDCore authentication, relative to the configured <c>BaseWebUrl</c>
    /// </summary>
    public string RedirectUrl { get; set; } = _defaultRedirectUrl;
    /// <summary>
    /// The <em>authorization scope</em> of the <strong>RDCore</strong> application with the API.
    /// </summary>
    public string ApiScope { get; set; } = _defaultApiScope;
}

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

/// <summary>
/// Configures <em>named pipe</em> transport layer settings.
/// </summary>
/// <remarks>
/// 👉 <strong>Named pipes are inherently local</strong> and the preferred way to establish a communication channel between the platform processes.
/// </remarks>
public record class NamedPipeTransportOptions
{
    private const string _defaultPipeName = "RDCore.SDK.Server.Pipe";
    private const int _defaultMaximumInstances = 1;

    /// <summary>
    /// The base name of the <em>named pipe</em> transport.
    /// </summary>
    /// <remarks>
    /// 🧩 This setting is required in <strong>both <em>client</em> and <em>server</em> applications</strong> and <strong>MUST</strong> be different for each loaded SDK application.
    /// </remarks>
    public string PipeName { get; set; } = $"{_defaultPipeName + Random.Shared.NextInt64()}";
    /// <summary>
    /// The maximum number of pipe instances that can run concurrently.
    /// </summary>
    /// <remarks>
    /// ⚠️ Changing this setting without proper support for it could <strong>corrupt client/server communications</strong>.
    /// Each server instance should append a random suffix to the pipe name to prevent this,
    /// otherwise there needs to be a mechanism (mutex) to prevent multiple instances of the server application from being started.
    /// </remarks>
    //[Option('i', "instances", Group = "pipe", Default = _defaultMaximumInstances)]
    public int MaximumInstances { get; set; } = _defaultMaximumInstances;
}
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
