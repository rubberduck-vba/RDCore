using CommandLine;
using Microsoft.Extensions.Logging;
using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Extensibility;

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
    public int? ConnectTimeoutSeconds { get; set; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkWorkspaceOptions.DefaultLocation"/> setting.
    /// </summary>
    [Option('d', "default-workspace")]
    public string? DefaultLocation { get; set; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the application startup sequence and outputs command-line arguments documentation instead.
    /// </summary>
    [Option('h', "help")]
    public bool? Help { get; set; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkServerOptions.HealthCheckIntervalSeconds"/> setting.
    /// </summary>
    [Option('k', "healthcheck-interval")]
    public int? HealthCheckIntervalSeconds { get; set; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="TransportOptions.Type"/> setting.
    /// </summary>
    [Option('m', "transport-mode")]
    public ServerTransportLayerMode? Type { get; set; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="NamedPipeTransportOptions.PipeName"/> setting.
    /// </summary>
    [Option('n', "pipe-name")]
    public string? PipeName { get; set; }
    /// <summary>
    /// A <em>command-line argument</em> that provides the <see cref="SdkServerOptions.ClientProcessId"/> owner process ID to a server app.
    /// </summary>
    /// <remarks>
    /// 👉 This argument is required for starting a server app.
    /// </remarks>
    [Option('p', "client-process-id")]
    public int? ClientProcessId { get; set; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkServerOptions.ShutdownTimeoutSeconds"/> setting.
    /// </summary>
    [Option('s', "shutdown-timeout")]
    public int? ShutdownTimeoutSeconds { get; set; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkServerOptions.TraceLevel"/> setting.
    /// </summary>
    [Option('t', "trace-level")]
    public LogLevel? TraceLevel { get; set; }
    /// <summary>
    /// A <em>command-line switch</em> that overrides the <see cref="SdkServerOptions.TraceLevel"/> setting.
    /// </summary>
    /// <remarks>
    /// 👉 This switch has <strong>no alias</strong> and does not appear in <em>help documentation</em>.
    /// </remarks>
    [Option("unsafe-dev-mode", Hidden = true)]
    public bool? UnsafeDevMode { get; set; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkServerOptions.Verbose"/> setting.
    /// </summary>
    [Option('v', "verbose")]
    public bool? Verbose { get; set; }
    /// <summary>
    /// A <em>command-line argument</em> that overrides the <see cref="SdkWorkspaceOptions.WorkspaceUri"/> setting.
    /// </summary>
    /// <remarks>
    /// 👉 This argument is required for starting a language server app.
    /// </remarks>
    [Option('w', "workspace")]
    public string? WorkspaceUri { get; set; }

    /// <summary>
    /// Repeatable <em>command-line argument</em> defining or overriding a project-level precompiler
    /// constant, in <c>NAME=VALUE</c> form (e.g. <c>--define RDDEBUG=1</c>). Overrides the
    /// <c>.rdproj</c> and the built-in host constants.
    /// </summary>
    [Option('D', "define")]
    public IEnumerable<string>? Define { get; set; }

    /// <summary>
    /// The <c>--define</c> arguments parsed into a <c>NAME → VALUE</c> map (case-insensitive; the last
    /// value wins). Malformed entries are ignored.
    /// </summary>
    public IReadOnlyDictionary<string, string> PrecompilerConstantOverrides()
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Define ?? [])
        {
            var separator = entry.IndexOf('=');
            if (separator > 0)
            {
                overrides[entry[..separator].Trim()] = entry[(separator + 1)..].Trim();
            }
        }
        return overrides;
    }

    /// <summary>
    /// Projects the supplied arguments onto <c>IConfiguration</c> keys.
    /// </summary>
    /// <remarks>
    /// Options that were not supplied are omitted, so <c>appsettings.json</c> values are preserved.
    /// </remarks>
    public IEnumerable<KeyValuePair<string, string?>> ToConfigurationOverrides()
    {
        if (ClientProcessId is int clientProcessId) yield return new("Configuration:Server:ClientProcessId", clientProcessId.ToString());
        if (TraceLevel is LogLevel traceLevel) yield return new("Configuration:Server:TraceLevel", traceLevel.ToString());
        if (Verbose is bool verbose) yield return new("Configuration:Server:Verbose", verbose.ToString());
        if (ConnectTimeoutSeconds is int connectTimeout) yield return new("Configuration:Server:ConnectTimeoutSeconds", connectTimeout.ToString());
        if (HealthCheckIntervalSeconds is int healthCheckInterval) yield return new("Configuration:Server:HealthCheckIntervalSeconds", healthCheckInterval.ToString());
        if (ShutdownTimeoutSeconds is int shutdownTimeout) yield return new("Configuration:Server:ShutdownTimeoutSeconds", shutdownTimeout.ToString());
        if (UnsafeDevMode is bool unsafeDevMode) yield return new("Configuration:Server:UnsafeDevMode", unsafeDevMode.ToString());
        if (WorkspaceUri is string workspaceUri) yield return new("Configuration:Workspace:WorkspaceUri", workspaceUri);
        if (DefaultLocation is string defaultLocation) yield return new("Configuration:Workspace:DefaultLocation", defaultLocation);
        if (Type is ServerTransportLayerMode transportType) yield return new("Configuration:Platform:Transport:Type", transportType.ToString());
        if (PipeName is string pipeName) yield return new("Configuration:Platform:Transport:PipeConfig:PipeName", pipeName);
    }
}

/// <summary>
/// Configuration settings, bound from <c>appsettings.json</c> or overridden from command-line arguments.
/// </summary>
public record class SdkAppOptions
{
    /// <summary>
    /// ServerApp options.
    /// </summary>
    /// <remarks>
    /// Configures a platform server application within the RDCore platform.
    /// </remarks>
    public SdkServerAppOptions ServerApp { get; set; } = new();
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
    /// <summary>
    /// Runtime environment profile options — the host facts VBA semantics depend on.
    /// </summary>
    public SdkEnvironmentOptions Environment { get; set; } = new();
}

/// <summary>
/// Runtime environment settings, bound from <c>appsettings.json</c>. These become the session's
/// <c>IRuntimeEnvironmentProfile</c>.
/// </summary>
public record class SdkEnvironmentOptions
{
    /// <summary>
    /// <c>true</c> for a 64-bit environment (<c>LongPtr</c> width, <c>#If Win64</c> / <c>#If VBA7</c>).
    /// </summary>
    public bool Is64Bit { get; set; } = true;
    /// <summary>
    /// The environment locale identifier. <c>0</c> (the default) means the invariant locale.
    /// </summary>
    public int Lcid { get; set; }
    /// <summary>
    /// The ANSI code page for <c>Byte()</c> ↔ <c>String</c> and non-Unicode <c>Chr</c> / <c>Asc</c>.
    /// </summary>
    public int AnsiCodePage { get; set; } = 1252;
    /// <summary>
    /// <c>true</c> when the host supports the <c>Option Compare Database</c> directive (Microsoft Access).
    /// </summary>
    public bool SupportsOptionCompareDatabase { get; set; }
}

public record class SdkServerAppOptions
{
    /// <summary>
    /// The platform server component type for this application.
    /// </summary>
    public CoreServerComponent Component { get; set; } = CoreServerComponent.Extension;
}

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
    /// How build-machine source-file paths in error / stack-trace detail are rewritten before that
    /// text is packaged into a DTO and sent to another process. Defaults to
    /// <see cref="SourcePathScrubMode.RepoRelative"/>.
    /// </summary>
    /// <remarks>
    /// The dev platform is published <c>Debug</c> with PDBs, so a caught exception's text carries the
    /// build machine's absolute paths and user name. This does not affect the unredacted copy written
    /// to the process log file.
    /// </remarks>
    public SourcePathScrubMode WireErrorDetail { get; set; } = SourcePathScrubMode.RepoRelative;
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
    /// How many times a supervised child connection is restarted after it fails unexpectedly, before the failure is escalated.
    /// </summary>
    public int MaxRestartAttempts { get; set; } = 3;
    /// <summary>
    /// The base delay, in milliseconds, before the first restart attempt. Doubles per attempt, capped at <see cref="RestartBackoffMaxMs"/>.
    /// </summary>
    public int RestartBackoffBaseMs { get; set; } = 500;
    /// <summary>
    /// The maximum delay, in milliseconds, between restart attempts.
    /// </summary>
    public int RestartBackoffMaxMs { get; set; } = 10_000;
    /// <summary>
    /// ⚠️ Allows the server platform to load <strong>unsigned plug-ins</strong>.
    /// </summary>
    /// <remarks>
    /// 🧩 This is useful for developing plug-ins with a private fork.<br/>
    /// This setting is <strong>read-only</strong> and must be explicitly overridden via a command-line argument.
    /// </remarks>
    public bool UnsafeDevMode { get; set; }
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
    private const string _defaultServerExecutable = "../RDCore.LanguageServer/RDCore.LanguageServer.exe";
    private const string _defaultParserExecutable = "../RDCore.Parsing/RDCore.ParseServer.exe";

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
    /// </remarks>
    public string ServerExecutable { get; set; } = _defaultServerExecutable;
    /// <summary>
    /// The location of the RDCore LSP parsing server executable.
    /// </summary>
    /// <remarks>
    /// 👉 This setting is used by the <strong>language server</strong> to locate the RDCore LSP parser executable.<br/>
    /// </remarks>
    public string ParserExecutable { get; set; } = _defaultParserExecutable;
    /// <summary>
    /// Configures platform-wide transport layer settings.
    /// </summary>
    public TransportOptions Transport { get; set; } = new();
    /// <summary>
    /// Configures extension settings.
    /// </summary>
    public ExtensionsOptions Extensions { get; set; } = new();
}
/// <summary>
/// Platform-level extension settings, bound from <c>appsettings.json</c>.
/// </summary>
public record class ExtensionsOptions
{
    private const string _defaultExtensionsLocation = "Extensions";
    private const string _defaultExtensionManifestName = "extension.manifest.json";

    /// <summary>
    /// The location RDCore extensions are discovered from. Plugins/extensions are identified by a folder containing a RDCore extension manifest.<br/>
    /// </summary>
    public string Path { get; set; } = _defaultExtensionsLocation;
    /// <summary>
    /// The name of the RDCore extension manifest file.
    /// </summary>
    /// <remarks>
    /// ⚠️ This value is constant.
    /// </remarks>
    public string Manifest { get; } = _defaultExtensionManifestName;
    /// <summary>
    /// A list of allowed extension titles.
    /// </summary>
    /// <remarks>
    /// 👉 RDCore will only load extensions present in this list.
    /// </remarks>
    public List<string> Allowed { get; set; } = ["RDCore.Diagnostics"];
    /// <summary>
    /// A list of blocked extension titles.
    /// </summary>
    /// <remarks>
    /// 👉 RDCore may block a repeatedly problematic extension from being loaded automatically.
    /// </remarks>
    public List<BlockedExtensionOption> Blocked { get; set; } = [];
}

public record class BlockedExtensionOption
{
    public string Title { get; set; } = default!;
    public ExtensionValidationFlags Flags { get; set; }
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
    public string PipeName { get; set; } = null!;
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
