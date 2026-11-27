using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Workspace;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00416</c>: a module annotation of a block's module that has a short form above the block's header: <c>'@ModuleDescription</c> is
/// <c>'@Description</c> there, and <c>'@IgnoreModule</c> is <c>'@Ignore</c>.
/// </summary>
/// <remarks>
/// The comment lines directly above a block's header annotate the block's module, and the module annotations of the block are those and the ones of its
/// declarations section (<see cref="Annotations.GetModuleAnnotations"/>). The fix renames an annotation above the header, and moves one of the declarations
/// section above the header under its short name.
/// </remarks>
internal sealed class ModuleAnnotationShortFormAnalyzer : IModuleAnalyzer
{
    private static readonly Dictionary<string, string> ShortForms = new(StringComparer.OrdinalIgnoreCase)
    {
        [DocStrings.ModuleDescriptionAnnotation] = DocStrings.MemberDescriptionAnnotation,
        ["IgnoreModule"] = "Ignore",
    };

    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ModuleAnnotationHasAShortForm.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var parse = context.ParseResult;
        if (!parse.BlockModules.Any())
        {
            yield break;
        }

        var document = new RDCoreDocument(context.Document, parse);
        foreach (var block in parse.BlockModules)
        {
            if (document.ParseOf(block.Uri) is not { } module)
            {
                continue;
            }

            var header = module.GetHeaderAnnotations();
            foreach (var annotation in module.GetModuleAnnotations())
            {
                if (!ShortForms.TryGetValue(annotation.Name, out var written))
                {
                    continue;
                }

                var fix = header.Contains(annotation) ? Renamed(parse, annotation, written) : Moved(parse, block, annotation, written);
                var message = AnalyzerMessages.Format(RDCoreDiagnosticsResources.ModuleAnnotationHasAShortForm_Message, annotation.Name, written);
                yield return new AnalyzerFinding(RDCoreDiagnosticId.ModuleAnnotationHasAShortForm.ToCode(), annotation.SourceLocation.Range,
                    DiagnosticSeverity.Information, message)
                {
                    Fixes = fix is null ? null : new DiagnosticFixes { Edits = [fix] },
                };
            }
        }
    }

    private static string Title(string written) => AnalyzerMessages.Format(RDCoreDiagnosticsResources.ModuleAnnotationHasAShortForm_Fix, written);

    // above the header already: written under its short name where it is. None for an annotation that runs on past its line.
    private static EditFix? Renamed(ModuleParseResult parse, AnnotationTriviaNode annotation, string written)
        => AnnotationEdits.Rename(parse, annotation, written) is { } edit ? SyntaxEdits.Fix(Title(written), edit) : null;

    // in the declarations section: written above the header under its short name, and removed where it is. None for an annotation that runs on past its
    // line.
    private static EditFix? Moved(ModuleParseResult parse, BlockModule block, AnnotationTriviaNode annotation, string written)
        => AnnotationEdits.MoveAbove(parse, annotation, block.Range, written) is { } edits ? SyntaxEdits.Fix(Title(written), edits) : null;
}
