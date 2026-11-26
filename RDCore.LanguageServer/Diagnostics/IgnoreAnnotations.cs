using System.Globalization;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Diagnostics;

// '@Ignore and '@IgnoreModule: the findings of a document that its annotations suppress, which the language server drops once it has every provider's, and
// RDC00418 for each argument that names nothing they can suppress. An annotation suppresses findings in the module it is written in, which in a file of one
// module is the whole file. In a file that has blocks, '@IgnoreModule suppresses in the module it is in, and '@Ignore only on the comment lines directly
// above a block's header, in that block's module; elsewhere in such a file it suppresses nothing. An annotation with no argument suppresses every code that
// can be disabled; a language error (VBC, VBR, VBA) is never suppressed.
internal static class IgnoreAnnotations
{
    private const string Ignore = "Ignore";
    private const string IgnoreModule = "IgnoreModule";

    // RDC00418, which the language server reports itself: the core extension's registry of RDC codes names it IgnoreNamesNoDiagnostic.
    internal static readonly RDCoreDiagnosticCode NamesNoDiagnostic = new(RDCoreDiagnosticCode.CorePrefix, 418);

    private static readonly DiagnosticFactory Findings = new();

    // what is found of the document, without the findings its annotations suppress; with RDC00418 for each argument that names nothing they can suppress
    // when the phase found is the static phase, which tells it once, and the code is not disabled.
    public static IReadOnlyList<Diagnostic> Apply(
        Uri file, ModuleParseResult parse, IReadOnlyList<Diagnostic> found, AnalysisPhase phase, IReadOnlySet<RDCoreDiagnosticCode> disabled)
    {
        var annotations = parse.Trivia.OfType<AnnotationTriviaNode>()
            .Where(annotation => annotation.IsNamed(Ignore) || annotation.IsNamed(IgnoreModule)).ToArray();
        if (annotations.Length == 0)
        {
            return found;
        }

        IReadOnlyList<Diagnostic> all = !phase.HasFlag(AnalysisPhase.Static) || disabled.Contains(NamesNoDiagnostic) ? found
            : [.. found, .. annotations.SelectMany(NamingNothing)];
        var scopes = ScopesOf(file, parse);
        return scopes.Count == 0 ? all : [.. all.Where(diagnostic => !scopes.Any(scope => scope.Suppresses(diagnostic)))];
    }

    // the module an annotation suppresses findings in (none: the whole file), and the codes it suppresses (none: every code that can be disabled).
    private sealed record Scope(RDCoreDocument? Document, Uri? Module, IReadOnlySet<RDCoreDiagnosticCode>? Codes)
    {
        public bool Suppresses(Diagnostic diagnostic)
            => diagnostic.Code is { IsString: true, String: var text }
                && RDCoreDiagnosticCode.TryParse(text, out var code)
                && code.CanBeDisabled
                && (Codes is null || Codes.Contains(code))
                && (Module is null || Document?.ModuleAt(new SourcePosition(diagnostic.Range.Start.Line, diagnostic.Range.Start.Character)) == Module);
    }

    private static List<Scope> ScopesOf(Uri file, ModuleParseResult parse)
    {
        if (!parse.BlockModules.Any())
        {
            return [.. parse.Trivia.OfType<AnnotationTriviaNode>()
                .Where(annotation => annotation.IsNamed(Ignore) || annotation.IsNamed(IgnoreModule))
                .Select(annotation => new Scope(null, null, CodesOf(annotation)))];
        }

        // a block's module has the comment lines directly above its header, whose annotations are its module's (Annotations.GetHeaderAnnotations).
        var document = new RDCoreDocument(file, parse);
        return [.. document.ModuleUris.SelectMany(module => SuppressionsOf(document.ParseOf(module))
            .Select(annotation => new Scope(document, module, CodesOf(annotation))))];
    }

    // '@IgnoreModule anywhere in the module's parse, and '@Ignore above a block's header.
    private static IEnumerable<AnnotationTriviaNode> SuppressionsOf(ModuleParseResult? module)
        => module is null ? []
            : [.. module.Trivia.OfType<AnnotationTriviaNode>().Where(annotation => annotation.IsNamed(IgnoreModule)),
                .. module.GetHeaderAnnotations().Where(annotation => annotation.IsNamed(Ignore))];

    // every code that can be disabled when the annotation names none; the codes its arguments name otherwise, which are none when no argument names any.
    private static HashSet<RDCoreDiagnosticCode>? CodesOf(AnnotationTriviaNode annotation)
        => annotation.Children.IsEmpty ? null
            : [.. Arguments(annotation).SelectMany(argument => TryResolve(argument.Text, out var codes) ? codes : [])];

    private static IEnumerable<Diagnostic> NamingNothing(AnnotationTriviaNode annotation)
    {
        foreach (var (argument, text) in Arguments(annotation))
        {
            var message = !TryResolve(text, out var codes) ? SuppressionMessages.NamesNoDiagnostic
                : codes.All(code => !code.CanBeDisabled) ? SuppressionMessages.NamesALanguageError
                : null;
            if (message is not null)
            {
                yield return Findings.FromAnalyzerFinding(new AnalyzerFinding(NamesNoDiagnostic, argument.SourceLocation.Range, DiagnosticSeverity.Warning,
                    string.Format(CultureInfo.CurrentCulture, message, annotation.Name, text)));
            }
        }
    }

    // an argument as it names a diagnostic: a name (RDC00315, ProcedureNotUsed), or a string that writes one.
    private static IEnumerable<(SyntaxNode Argument, string Text)> Arguments(AnnotationTriviaNode annotation)
        => annotation.Children.Select((argument, index) => (argument, annotation.GetArgumentText(index) ?? string.Empty));

    // a diagnostic code of any provider, written as a code, or the name of a legacy Rubberduck inspection that a core code took the place of.
    private static bool TryResolve(string text, out IReadOnlyList<RDCoreDiagnosticCode> codes)
    {
        if (RDCoreDiagnosticCode.TryParse(text, out var code))
        {
            codes = [code];
            return true;
        }

        return LegacyInspectionNames.TryGetCodes(text, out codes);
    }
}
