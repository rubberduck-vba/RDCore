namespace RDCore.SDK.Extensibility.Configuration;

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
