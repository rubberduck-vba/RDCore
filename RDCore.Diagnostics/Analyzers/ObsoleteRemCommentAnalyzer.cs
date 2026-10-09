using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00303</c>: a comment written with the <c>Rem</c> keyword, in a language where it is a relic of classic BASIC (<strong>MS-VBAL §3.3.1</strong>).
/// </summary>
/// <remarks>
/// A <c>'</c> starts the same comment. Whether the language keeps <c>Rem</c> as the way to write one (<see cref="SDK.Workspace.SupportedLanguage.UsesClassicBasicSyntax"/>) is a
/// fact the host that loaded the module vouches for: an analyzer that is not given it has nothing to say.
/// </remarks>
internal sealed class ObsoleteRemCommentAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => ModuleLanguage.UsesClassicBasicSyntax(context) is false
            ? context.ParseResult.Trivia
                .OfType<CommentTriviaNode>()
                .Where(comment => IsRem(comment.Value))
                .Select(comment => new AnalyzerFinding(
                    RDCoreDiagnosticId.ObsoleteCommentSyntax,
                    ModuleLanguage.KeywordAt(comment.SourceLocation.Range, "Rem".Length),
                    DiagnosticSeverity.Information,
                    RDCoreDiagnosticsResources.ObsoleteCommentSyntax_Message))
            : [];

    // `Rem` is a keyword only as a word of its own: a comment that begins with `Remember` is not one.
    private static bool IsRem(string comment)
        => comment.StartsWith("Rem", StringComparison.OrdinalIgnoreCase) && (comment.Length == 3 || !char.IsLetterOrDigit(comment[3]));
}
