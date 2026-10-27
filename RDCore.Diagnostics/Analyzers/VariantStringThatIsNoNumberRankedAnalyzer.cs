using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11020</c>: a relational operator compares a <c>Variant</c> holding a <c>String</c> that is no number with a number, a <c>Date</c> or a <c>Boolean</c>
/// that is no <c>Variant</c>, and MS-VBA ranks the <c>String</c> above it, where <strong>MS-VBAL §5.6.9.5</strong> specifies a type mismatch (13).
/// </summary>
/// <remarks>
/// That the two are ranked is a fact the language core states about the comparison
/// (<see cref="ComparisonOperatorSemanticFlags.IsVariantStringThatIsNoValueRanked"/>). RD-VBA compares as MS-VBA does: <c>"abc" &gt; 5</c> is <c>True</c>,
/// whatever the number, and raises nothing. A warning, equivalent to the run-time error the specification raises: a number read from a cell or an
/// <c>InputBox</c> that turns out to be no number is quietly greater than every number. Nothing is reported for a procedure that was not evaluated.
/// </remarks>
internal sealed class VariantStringThatIsNoNumberRankedAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Operations ?? [])
            .Where(op => op.ComparisonOpSemanticFlags?.HasFlag(ComparisonOperatorSemanticFlags.IsVariantStringThatIsNoValueRanked) ?? false)
            .Select(op => new AnalyzerFinding(
                RDCoreDiagnosticId.VariantStringThatIsNoNumberRanked,
                op.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(
                    RDCoreDiagnosticsResources.VariantStringThatIsNoNumberRanked_Message,
                    op.Operator)));
}
