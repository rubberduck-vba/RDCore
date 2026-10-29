using System.Globalization;

namespace RDCore.Diagnostics.Analyzers;

internal static class AnalyzerMessages
{
    /// <summary>
    /// A message of a diagnostic, with the particulars of the occurrence in it.
    /// </summary>
    /// <param name="message">The localized text of the message, with <c>{0}</c> for the particular.</param>
    /// <param name="particular">What the occurrence is about: usually a name.</param>
    public static string Format(string message, string particular) => string.Format(CultureInfo.CurrentUICulture, message, particular);

    /// <summary>
    /// A message of a diagnostic, with two particulars of the occurrence in it.
    /// </summary>
    /// <param name="message">The localized text of the message, with <c>{0}</c> and <c>{1}</c> for the particulars.</param>
    /// <param name="first">What the occurrence starts from.</param>
    /// <param name="second">What the occurrence ends at.</param>
    public static string Format(string message, string first, string second) => string.Format(CultureInfo.CurrentUICulture, message, first, second);

    /// <summary>
    /// A message of a diagnostic, with three particulars of the occurrence in it.
    /// </summary>
    /// <param name="message">The localized text of the message, with <c>{0}</c>, <c>{1}</c> and <c>{2}</c> for the particulars.</param>
    /// <param name="first">The first particular.</param>
    /// <param name="second">The second.</param>
    /// <param name="third">The third.</param>
    public static string Format(string message, string first, string second, string third)
        => string.Format(CultureInfo.CurrentUICulture, message, first, second, third);
}
