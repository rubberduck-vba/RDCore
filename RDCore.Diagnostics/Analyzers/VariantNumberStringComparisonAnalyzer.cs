using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11011</c>: a relational operator compares a <c>Variant</c> holding a number with a <c>Variant</c> holding a <c>String</c>, and the number
/// is less than the <c>String</c> whatever their values (<strong>MS-VBAL §5.6.9.5</strong>).
/// </summary>
/// <remarks>
/// That the comparison is the one MS-VBAL makes the exception for is a fact the language core states about it
/// (<see cref="ComparisonOperatorSemanticFlags.IsVariantStringNumericException"/>): <c>5 &lt; "4"</c> is <c>True</c> when both are <c>Variant</c>
/// values, where it is <c>False</c> when either is declared. Legitimate, and almost never meant: a number read from an <c>InputBox</c> or a cell
/// is a <c>String</c>, and compares as one. A warning, because the result does not depend on the values. Nothing is reported for a procedure that was
/// not evaluated.
/// </remarks>
internal sealed class VariantNumberStringComparisonAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.VariantNumberStringComparison.ToCode()];

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Operations ?? [])
            .Where(op => op.ComparisonOpSemanticFlags?.HasFlag(ComparisonOperatorSemanticFlags.IsVariantStringNumericException) ?? false)
            .Select(op => new AnalyzerFinding(
                RDCoreDiagnosticId.VariantNumberStringComparison.ToCode(),
                (op.OperatorLocation ?? op.Location).Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(
                    RDCoreDiagnosticsResources.VariantNumberStringComparison_Message,
                    op.Operator)));
}
