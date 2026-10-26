using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11013</c>: a relational operator compares a <c>Variant</c> holding a number, a <c>Date</c> or a <c>Boolean</c> with a <c>String</c> that is no
/// <c>Variant</c>, and MS-VBA compares the two as text, where <strong>MS-VBAL §5.6.9.5</strong> specifies a type mismatch (13).
/// </summary>
/// <remarks>
/// That the comparison is made as text is a fact the language core states about it
/// (<see cref="ComparisonOperatorSemanticFlags.IsVariantComparedWithStringAsText"/>). RD-VBA compares as MS-VBA does, which Microsoft Learn documents, and
/// MS-VBAL's table does not: <c>10 &gt; "9"</c> is <c>False</c>, because <c>"10"</c> comes before <c>"9"</c>. A warning, equivalent to the run-time error
/// the specification raises. Nothing is reported for a procedure that was not evaluated.
/// </remarks>
internal sealed class VariantComparedWithStringAsTextAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Operations ?? [])
            .Where(op => op.ComparisonOpSemanticFlags?.HasFlag(ComparisonOperatorSemanticFlags.IsVariantComparedWithStringAsText) ?? false)
            .Select(op => new AnalyzerFinding(
                RDCoreDiagnosticId.VariantComparedWithStringAsText,
                op.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(
                    RDCoreDiagnosticsResources.VariantComparedWithStringAsText_Message,
                    op.Operator)));
}
