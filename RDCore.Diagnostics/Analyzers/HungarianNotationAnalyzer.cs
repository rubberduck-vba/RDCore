using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01002</c>: a name that begins with a prefix that states its type: <c>strName</c>, <c>lngCount</c>.
/// </summary>
/// <remarks>
/// <para>
/// The rule is that of Rubberduck's <em>Hungarian notation</em> inspection: a name begins with one of its lowercase prefixes, and what follows starts a new word, which an
/// uppercase letter or a digit says: <c>sName</c>, <c>str1</c>. <c>stringent</c> is no <c>str</c> followed by a word. A name that breaks it and that is approved is listed in
/// <see cref="DiagnosticsOptions.HungarianNotation"/>: <c>colWidth</c> is a width, and not a collection. The names this applies to are those the module chooses
/// (<see cref="DeclaredNames"/>).
/// </para>
/// <para>
/// The names of the controls of a form (<c>txtName</c>, <c>cmdOK</c>) are not reported: the platform has no source range for a control. A name that
/// begins with a control's prefix is reported wherever the module declares it. The handler of a control's event (<c>txtName_Change</c>) is reported at
/// the handler, and is changed by renaming the control: the finding offers no rename of its own. So is the handler of an event, and the implementation of
/// an interface member, declared by a project of the workspace, which renaming the event or the member changes.
/// </para>
/// </remarks>
/// <param name="options">The settings of the extension.</param>
internal sealed class HungarianNotationAnalyzer(IOptions<DiagnosticsOptions> options) : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.HungarianNotation.ToCode()];

    /// <summary>
    /// The lowercase prefixes that state the type of what they begin the name of: those of Rubberduck's inspection, and <c>dic</c>, <c>rng</c>, <c>rs</c>,
    /// <c>wb</c> and <c>ws</c>.
    /// </summary>
    public static IReadOnlyList<string> Prefixes { get; } =
    [
        "a", "arr", "b", "bas", "bln", "btn", "by", "byt", "c", "cbo", "chk", "chr", "cls", "cmd", "col", "cur", "dao", "dat", "dbl", "dic", "dlg", "drv", "dt", "dtm",
        "fld", "fra", "frm", "g", "grd", "i", "idx", "img", "int", "l", "lbl", "lng", "lst", "m", "mnu", "n", "o", "obj", "opt", "pgr", "pic", "rng", "rpt", "rs", "s",
        "shp", "sng", "str", "tbd", "tbl", "tmr", "txt", "udt", "var", "vnt", "wb", "ws",
    ];

    // the names are read when a module is analyzed: they are a live setting, which the client can change while the extension runs.
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var allowed = new HashSet<string>(options.Value.HungarianNotation.AllowedNames ?? [], StringComparer.OrdinalIgnoreCase);
        return context.ParseResult.SyntaxTree is { } module ? Analyze(module, allowed, context.Semantics?.Declarations) : [];
    }

    private static IEnumerable<AnalyzerFinding> Analyze(ModuleNode module, HashSet<string> allowed, IEnumerable<DeclarationFactDto>? declarations)
        => DeclaredNames.In(module, declarations)
            .Where(declared => !allowed.Contains(declared.Name) && IsHungarian(declared.Name))
            .Select(declared => new AnalyzerFinding(
                RDCoreDiagnosticId.HungarianNotation.ToCode(),
                declared.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.HungarianNotation_Message, declared.Name))
            {
                Fixes = declared.IsModule || declared.IsBound
                    ? null
                    : new DiagnosticFixes { Rename = new RenameFix { Suggestions = WithoutPrefix(declared.Name) is { } suggested ? [suggested] : [] } },
            });

    /// <summary>
    /// Whether a name begins with a prefix that states a type, and a new word or a digit follows it.
    /// </summary>
    /// <param name="name">A name.</param>
    public static bool IsHungarian(string name)
        => Prefixes.Any(prefix => BeginsWith(name, prefix) && (char.IsUpper(name[prefix.Length]) || char.IsAsciiDigit(name[prefix.Length])));

    /// <summary>
    /// The name without the prefix that states its type, which is the word that follows it: <c>Name</c> for <c>strName</c>; <see langword="null"/> when what
    /// follows is no name code can declare (<c>strNext</c>), or the name has no such prefix.
    /// </summary>
    /// <param name="name">A name.</param>
    public static string? WithoutPrefix(string name)
        => Prefixes
            .Where(prefix => BeginsWith(name, prefix) && char.IsUpper(name[prefix.Length]))
            .Select(prefix => name[prefix.Length..])
            .FirstOrDefault(rest => VBIdentifier.Validate(rest) == VBIdentifierValidity.Valid);

    // whether the name begins with the prefix, and goes on after it.
    private static bool BeginsWith(string name, string prefix) => name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal);
}
