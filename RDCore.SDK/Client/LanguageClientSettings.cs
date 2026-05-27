using Microsoft.Extensions.Logging;
using RDCore.SDK.Extensibility;
using System.Collections.Immutable;

namespace RDCore.SDK.Client;

/// <summary>
/// Encapsulates the startup configuration of a LSP language client application.
/// </summary>
public abstract record class LanguageClientSettings
{
    /// <summary>
    /// The environment <em>process ID</em> of this <em>language server client</em> application.
    /// </summary>
    /// <remarks>
    /// The language server monitors this process ID with its own health checks to avoid leaving any orphaned processes behind.
    /// </remarks>
    public required int ClientProcessId { get; init; }
    /// <summary>
    /// The path and <em>command-line arguments</em> to be used to launch a <c>RDCore.LanguageServer</c> process.
    /// </summary>
    public required ServerStartupSettings ServerStartupSettings { get; init; }

    public ImmutableArray<ServerStartupArg> ToServerStartupArgs()
    {
        var items = new List<ServerStartupArg>
        {
            ServerStartupArg.ClientProcessId(ClientProcessId),
            ServerStartupArg.WorkspaceUri(ServerStartupSettings.WorkspaceUri),
            ServerStartupArg.SetTrace(ServerStartupSettings.Trace)
        };
        if (ServerStartupSettings.Verbose)
        {
            items.Add(ServerStartupArg.SetVerbose());
        }
        return [.. items];
    }
}

public record class ServerStartupArg(string Name, string Alias, string? Parameter = default, bool UseAlias = true) 
{
    public static ServerStartupArg ClientProcessId(int value) => new("pid", "p", value.ToString());
    public static ServerStartupArg WorkspaceUri(Uri value) => new("workspace", "w", value.AbsolutePath);
    public static ServerStartupArg SetTrace(LogLevel value) => new("trace", "t", value.ToString().ToLowerInvariant());
    public static ServerStartupArg SetVerbose() => new("verbose", "v");

    public override string ToString() => (UseAlias ? $"-{Alias}" : $"--{Name}") + (Parameter != null ? ' ' + Parameter : null) + ' ';
}

/// <summary>
/// Encapsulates the command-line instruction and arguments needed to start a LSP language server application.
/// </summary>
public record class ServerStartupSettings
{
    public const string RDCoreLanguageServerExecutable = "RDCore.LanguageServer.exe";
    /// <summary>
    /// The name of the LSP language server executable this application connects to.
    /// </summary>
    public required string LanguageServer { get; init; } = $"./{RDCoreLanguageServerExecutable}";
    /// <summary>
    /// The <c>Uri</c> of the <em>workspace</em> to initialize the language server with.
    /// </summary>
    /// <remarks>
    /// Given an absolute <c>file://uri</c>, the LSP language server initializes a project/workspace (<c>.rdproj</c>) at that location through <c>IFileSystem</c>.
    /// </remarks>
    public required Uri WorkspaceUri { get; init; } = new($"{RDCoreUriNamespaces.RDCoreWorkspaceUri}/.rdc");
    /// <summary>
    /// The <em>minimum log level</em> of the LSP server trace logs.
    /// </summary>
    public LogLevel Trace { get; init; } = LogLevel.Trace;
    /// <summary>
    /// <c>true</c> if the LSP language server should be started with <c>VERBOSE ON</c> (<c>--verbose</c>).
    /// </summary>
    /// <remarks>
    /// Structured messages that include an additional <em>verbose</em> string are shown when this setting is enabled; this setting does not otherwise control the volume of trace logs.
    /// </remarks>
    public bool Verbose { get; init; } = true;

    public bool CreateNoWindow { get; init; } = true;
    public bool RedirectStdOut { get; init; } = true;
    public bool RedirectStdErr { get; init; } = true;
}
