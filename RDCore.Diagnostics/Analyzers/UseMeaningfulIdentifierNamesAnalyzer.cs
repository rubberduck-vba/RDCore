using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;
using System.Globalization;
using System.Text;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01001</c>: a name that does not say what it is: shorter than 3 characters, ending with a digit, or without a vowel.
/// </summary>
/// <remarks>
/// The rules are those of Rubberduck's <em>meaningful names</em> inspection. A name that breaks one and that is approved is listed in
/// <see cref="DiagnosticsOptions.MeaningfulNames"/>: the counter of a loop is <c>i</c>, and not worth a diagnostic everywhere it is written. The names this applies to are
/// those the module chooses (<see cref="DeclaredNames"/>). The first rule a name breaks is the one reported.
/// </remarks>
/// <param name="options">The settings of the extension.</param>
internal sealed class UseMeaningfulIdentifierNamesAnalyzer(IOptions<DiagnosticsOptions> options) : SyntaxTreeAnalyzer
{
    /// <summary>
    /// The least number of characters a name has.
    /// </summary>
    public const int MinimumLength = 3;

    private readonly HashSet<string> _allowed = new(options.Value.MeaningfulNames.AllowedNames ?? [], StringComparer.OrdinalIgnoreCase);

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => DeclaredNames.In(module)
            .Where(declared => !_allowed.Contains(declared.Name))
            .Select(declared => (declared, message: MessageFor(declared.Name)))
            .Where(entry => entry.message is not null)
            .Select(entry => new AnalyzerFinding(
                RDCoreDiagnosticId.UseMeaningfulIdentifierNames,
                entry.declared.Range,
                DiagnosticSeverity.Information,
                Say(entry.message!, entry.declared.Name)));

    private static string? MessageFor(string name)
    {
        if (name.Length < MinimumLength)
        {
            return RDCoreDiagnosticsResources.UseMeaningfulIdentifierNames_TooShort_Message;
        }

        if (char.IsDigit(name[^1]))
        {
            return RDCoreDiagnosticsResources.UseMeaningfulIdentifierNames_EndsWithDigit_Message;
        }

        return name.Any(IsVowel) ? null : RDCoreDiagnosticsResources.UseMeaningfulIdentifierNames_NoVowel_Message;
    }

    // a vowel of either language of the platform: é is an e, whatever the accent.
    private static bool IsVowel(char character)
    {
        var letter = char.ToLowerInvariant(character);
        if ("aeiou".Contains(letter))
        {
            return true;
        }

        var decomposed = letter.ToString().Normalize(NormalizationForm.FormD);
        return decomposed.Length > 1 && CharUnicodeInfo.GetUnicodeCategory(decomposed[1]) is UnicodeCategory.NonSpacingMark && "aeiou".Contains(decomposed[0]);
    }
}
