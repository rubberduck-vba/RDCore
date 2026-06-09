namespace RDCore.SDK.Extensibility;

/// <summary>
/// Defines a number of useful URL addresses and endpoints.
/// </summary>
/// <remarks>
/// 🧩 <c>const</c> values are compiled in-place into any referencing libraries that use them; if you need different values, you need a different build of <c>RDCore.SDK</c>.
/// </remarks>
public static class RDCoreUrl
{
    /// <summary>
    /// The base URL for the RDCore parent company website.
    /// </summary>
    public const string RDCoreBaseWebUrl = "https://rubberduckvba.ca";
    /// <summary>
    /// The base URL for the RDCore parent company web API.
    /// </summary>
    public const string RDCoreWebApiBaseUrl = $"{RDCoreBaseWebUrl}/api";
    /// <summary>
    /// The base URL for diagnostics documentation.
    /// </summary>
    public const string RDCoreDiagnosticCodeDescriptionBaseWebUrl = $"{RDCoreBaseWebUrl}/rdcore/diagnostics";
}
