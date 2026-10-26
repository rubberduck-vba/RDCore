using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11017</c>: the pattern of a <c>Like</c> operation is one MS-VBA matches otherwise than <strong>MS-VBAL §5.6.9.6</strong> says it does.
/// </summary>
/// <remarks>
/// That the pattern departs is a fact the language core states about the operation
/// (<see cref="ComparisonOperatorSemanticFlags.LikePatternHasEmptyCharList"/>, <see cref="ComparisonOperatorSemanticFlags.LikePatternIsInvalid"/>):
/// a <c>[]</c> or a <c>[!]</c>, which MS-VBAL makes a list of no character, or an invalid element, which MS-VBAL makes error 93 wherever the pattern
/// is used. RD-VBA matches what MS-VBA matches, so the code does what it does in MS-VBA; a warning, because it is not what the specification says.
/// Only a pattern written in the code is reported, and nothing is reported for a procedure that was not evaluated.
/// </remarks>
internal sealed class LikePatternDepartureAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        foreach (var op in (context.Semantics?.Procedures ?? []).SelectMany(procedure => procedure.Runtime?.Operations ?? []))
        {
            var flags = op.ComparisonOpSemanticFlags ?? 0;
            if (flags.HasFlag(ComparisonOperatorSemanticFlags.LikePatternHasEmptyCharList))
            {
                yield return Finding(op.Location.Range, RDCoreDiagnosticsResources.LikePatternDeparture_EmptyCharList_Message);
            }

            if (flags.HasFlag(ComparisonOperatorSemanticFlags.LikePatternIsInvalid))
            {
                yield return Finding(op.Location.Range, RDCoreDiagnosticsResources.LikePatternDeparture_InvalidPattern_Message);
            }
        }
    }

    private static AnalyzerFinding Finding(SourceRange range, string message)
        => new(RDCoreDiagnosticId.LikePatternDeparture, range, DiagnosticSeverity.Warning, message);
}
