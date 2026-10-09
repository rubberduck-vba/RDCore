using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01002</c>: a name that begins with a prefix that states its type: <c>strName</c>, <c>lngCount</c>.
/// </summary>
/// <remarks>
/// <para>
/// The rule is that of Rubberduck's <em>Hungarian notation</em> inspection: a name begins with one of a few common lowercase prefixes, and what follows starts a new word, which
/// an uppercase letter says. <c>stringent</c> is no <c>str</c> followed by a word. A name that breaks it and that is approved is listed in
/// <see cref="DiagnosticsOptions.HungarianNotation"/>: <c>colWidth</c> is a width, and not a collection. The names this applies to are those the module chooses
/// (<see cref="DeclaredNames"/>).
/// </para>
/// <para>
/// 🚧 TODO the prefixes of the controls of a form (<c>txt</c>, <c>cmd</c>, <c>lbl</c>) are not among them, since the platform has no form controls yet; whether to exclude them
/// is an option for when it does.
/// </para>
/// </remarks>
/// <param name="options">The settings of the extension.</param>
internal sealed class HungarianNotationAnalyzer(IOptions<DiagnosticsOptions> options) : SyntaxTreeAnalyzer
{
    /// <summary>
    /// The lowercase prefixes that state the type of what they begin the name of.
    /// </summary>
    public static IReadOnlyList<string> Prefixes { get; } =
    [
        "arr", "bln", "byt", "col", "cur", "dbl", "dic", "dtm", "int", "lng", "lst", "obj", "rng", "rs", "sng", "str", "var", "vnt", "wb", "ws",
    ];

    private readonly HashSet<string> _allowed = new(options.Value.HungarianNotation.AllowedNames ?? [], StringComparer.OrdinalIgnoreCase);

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => DeclaredNames.In(module)
            .Where(declared => !_allowed.Contains(declared.Name) && IsHungarian(declared.Name))
            .Select(declared => new AnalyzerFinding(
                RDCoreDiagnosticId.HungarianNotation,
                declared.Range,
                DiagnosticSeverity.Information,
                Say(RDCoreDiagnosticsResources.HungarianNotation_Message, declared.Name)));

    /// <summary>
    /// Whether a name begins with a prefix that states a type, and a new word follows it.
    /// </summary>
    /// <param name="name">A name.</param>
    public static bool IsHungarian(string name)
        => Prefixes.Any(prefix => name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal) && char.IsUpper(name[prefix.Length]));
}
