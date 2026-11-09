using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.LanguageServer.Diagnostics;

// what a diagnostics provider found, as the platform reports it. The core extension's findings are the platform's own (the language's errors and the
// first-party diagnostics), and are reported as they are. Another extension's are reported when their code is of the prefix it declares (RD-VBAL §1.1.4),
// under its name, and not as described by the platform's documentation; one of another code is dropped, and logged.
internal static class ExtensionFindings
{
    public static IReadOnlyList<Diagnostic> Admit(ExtensionInfo? extension, IReadOnlyList<Diagnostic> found, ILogger logger)
    {
        if (extension?.IsCoreExtension == true || found.Count == 0)
        {
            return found;
        }

        var title = extension?.Title ?? "extension";
        var admitted = new List<Diagnostic>(found.Count);
        var dropped = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var diagnostic in found)
        {
            if (diagnostic.Code is { IsString: true, String: var text }
                && RDCoreDiagnosticCode.TryParse(text, out var code)
                && code.Prefix == extension?.DiagnosticPrefix)
            {
                admitted.Add(diagnostic with { Source = title, CodeDescription = IsThePlatforms(diagnostic.CodeDescription) ? null : diagnostic.CodeDescription });
            }
            else
            {
                dropped.Add(diagnostic.Code switch
                {
                    { IsString: true, String: { } written } => written,
                    { IsLong: true, Long: var number } => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => "no code",
                });
            }
        }

        if (dropped.Count > 0 && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning("🚫 {count} finding(s) of extension '{title}' are dropped: their codes ({codes}) are not of the diagnostic prefix it declares ({prefix}).",
                found.Count - admitted.Count, title, string.Join(", ", dropped), extension?.DiagnosticPrefix is { } prefix ? $"'{prefix}'" : "none");
        }

        return admitted;
    }

    // the platform's documentation describes the platform's codes, not another publisher's.
    private static bool IsThePlatforms(CodeDescription? description)
        => description?.Href is { } href
            && href.ToString().StartsWith(RDCoreUrl.RDCoreDiagnosticCodeDescriptionBaseWebUrl, StringComparison.OrdinalIgnoreCase);
}
