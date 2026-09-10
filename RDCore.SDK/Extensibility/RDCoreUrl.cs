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
    /// The base URL for the published RD-VBAL documentation site (GitHub Pages).
    /// </summary>
    public const string RDCoreDocsBaseWebUrl = "https://rubberduck-vba.github.io/RDCore";
    /// <summary>
    /// The base URL for the per-code diagnostics documentation pages — one page per <c>VBC</c> /
    /// <c>VBR</c> / <c>VBA</c> / <c>RDC</c> code, pointed to by every emitted diagnostic's
    /// <c>codeDescription</c>.
    /// </summary>
    public const string RDCoreDiagnosticCodeDescriptionBaseWebUrl = $"{RDCoreDocsBaseWebUrl}/diagnostics";
}
