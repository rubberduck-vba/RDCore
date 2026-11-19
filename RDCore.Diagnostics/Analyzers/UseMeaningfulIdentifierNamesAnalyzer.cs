using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using System.Globalization;
using System.Text;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01001</c>: a name that does not say what it is: shorter than 3 characters, ending with a digit, without a vowel, or without a consonant.
/// </summary>
/// <remarks>
/// The rules are those of Rubberduck's <em>meaningful names</em> inspection. A name that breaks one and that is approved is listed in
/// <see cref="DiagnosticsOptions.MeaningfulNames"/>: the counter of a loop is <c>i</c>, and not worth a diagnostic everywhere it is written. The names this applies to are
/// those the module chooses (<see cref="DeclaredNames"/>). The first rule a name breaks is the one reported.
/// </remarks>
/// <param name="options">The settings of the extension.</param>
internal sealed class UseMeaningfulIdentifierNamesAnalyzer(IOptions<DiagnosticsOptions> options) : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.UseMeaningfulIdentifierNames.ToCode()];

    /// <summary>
    /// The least number of characters a name has.
    /// </summary>
    public const int MinimumLength = 3;

    // the names are read when a module is analyzed: they are a live setting, which the client can change while the extension runs.
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var allowed = new HashSet<string>(options.Value.MeaningfulNames.AllowedNames ?? [], StringComparer.OrdinalIgnoreCase);
        return context.ParseResult.SyntaxTree is { } module ? Analyze(module, allowed, context.Semantics?.Declarations) : [];
    }

    private IEnumerable<AnalyzerFinding> Analyze(ModuleNode module, HashSet<string> allowed, IEnumerable<DeclarationFactDto>? declarations)
        => DeclaredNames.In(module, declarations)
            .Where(declared => !allowed.Contains(declared.Name))
            .Select(declared => (declared, message: MessageFor(declared.Name)))
            .Where(entry => entry.message is not null)
            .Select(entry => new AnalyzerFinding(
                RDCoreDiagnosticId.UseMeaningfulIdentifierNames.ToCode(),
                entry.declared.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(entry.message!, entry.declared.Name))
            {
                // only the user can say what the name means.
                Fixes = new DiagnosticFixes { Rename = new RenameFix() },
            });

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

        if (!name.Any(IsVowel))
        {
            return RDCoreDiagnosticsResources.UseMeaningfulIdentifierNames_NoVowel_Message;
        }

        return name.Any(character => char.IsLetter(character) && !IsVowel(character)) ? null : RDCoreDiagnosticsResources.UseMeaningfulIdentifierNames_NoConsonant_Message;
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
