using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00416</c>: in a document that allows file-level statements, a module annotation of a block's module that has a short form above the block's header:
/// <c>'@ModuleDescription</c> is <c>'@Description</c> there, and <c>'@IgnoreModule</c> is <c>'@Ignore</c>.
/// </summary>
/// <remarks>
/// The comment lines directly above a block's header annotate the block's module, and the module annotations of the block are those and the ones of its
/// declarations section (<see cref="Annotations.GetModuleAnnotations"/>). The fix renames an annotation above the header, and moves one of the declarations
/// section above the header under its short name.
/// </remarks>
internal sealed class ModuleAnnotationShortFormAnalyzer(IOptions<SdkWorkspaceOptions> workspace) : IModuleAnalyzer
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
        if (!parse.BlockModules.Any() || !workspace.Value.AllowsFileLevelStatements(context.Document.AbsolutePath))
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

                var fix = header.Contains(annotation) ? Renamed(annotation, written) : Moved(parse, block, annotation, written);
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

    // above the header already: its name, after the @, is the short one.
    private static EditFix Renamed(AnnotationTriviaNode annotation, string written)
    {
        var start = annotation.SourceLocation.Range.Start;
        var name = new SourceRange(new SourcePosition(start.Line, start.Character + 1),
            new SourcePosition(start.Line, start.Character + 1 + annotation.Name.Length));
        return SyntaxEdits.Fix(Title(written), SyntaxEdits.Replace(name, written));
    }

    // in the declarations section: written above the header as the header is indented, under its short name, and removed where it is. None for an
    // annotation that runs on past its line.
    private static EditFix? Moved(ModuleParseResult parse, BlockModule block, AnnotationTriviaNode annotation, string written)
    {
        if (AnnotationEdits.TextOf(parse, annotation) is not { } text)
        {
            return null;
        }

        var headerLine = parse.Trivia.OfType<BlockBoundaryTriviaNode>().FirstOrDefault(boundary => !boundary.IsEnd
            && boundary.SourceLocation.Range.Start.Line == block.Range.Start.Line);
        var indent = headerLine?.Source[..(headerLine.Source.Length - headerLine.Source.TrimStart().Length)] ?? string.Empty;
        return SyntaxEdits.Fix(Title(written),
            LineEdits.InsertLinesBefore(parse, block.Range, $"{indent}'@{written}{text[(1 + annotation.Name.Length)..]}"),
            SyntaxEdits.Remove(AnnotationEdits.RemovalOf(parse, annotation)));
    }
}
