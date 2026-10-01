using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Server;

/// <summary>
/// The languages the language server registers with a client.
/// </summary>
public static class ProtocolSupportedLanguage
{
    /// <summary>RD-VBA, the default language: <see cref="SupportedLanguages.RDVBA"/>.</summary>
    public static SupportedLanguage VBA => SupportedLanguages.RDVBA;
}
