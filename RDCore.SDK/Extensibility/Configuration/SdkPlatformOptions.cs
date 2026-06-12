namespace RDCore.SDK.Extensibility.Configuration;

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
