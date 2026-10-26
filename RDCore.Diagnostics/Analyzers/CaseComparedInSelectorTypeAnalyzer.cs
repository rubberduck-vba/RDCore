using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11014</c>: a <c>Case Is</c> clause compares its value with the selector in the selector's declared type, where <strong>MS-VBAL §5.4.2.10</strong>
/// states the comparison as the expression <c>select-expression comparison-operator expression</c>, of another effective type.
/// </summary>
/// <remarks>
/// That the value of the clause is converted to the selector's type is a fact the language core states about it
/// (<see cref="CaseClauseSemanticFlags.ComparedInSelectorType"/>). RD-VBA converts it, as MS-VBA does: with <c>i</c> an <c>Integer</c> of <c>5</c>,
/// <c>Case Is &lt; 5.4</c> does not match, because <c>5.4</c> is the <c>Integer</c> <c>5</c>; the expression <c>i &lt; 5.4</c> is <c>True</c>. A warning,
/// because the specification states a comparison the code does not make. A single value (<c>Case 5</c>) is converted the same way, but MS-VBAL only says it
/// "is compared with" the selector, and is not reported. Nothing is reported for a procedure that was not evaluated.
/// </remarks>
internal sealed class CaseComparedInSelectorTypeAnalyzer : IModuleAnalyzer
{
    private const CaseClauseSemanticFlags Reported = CaseClauseSemanticFlags.ComparisonForm | CaseClauseSemanticFlags.ComparedInSelectorType;

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Statements ?? [])
            .Where(statement => statement.CaseClauseFlags is { } flags && (flags & Reported) == Reported)
            .DistinctBy(statement => statement.Node)
            .Select(statement => new AnalyzerFinding(
                RDCoreDiagnosticId.CaseComparedInSelectorType,
                statement.Location.Range,
                DiagnosticSeverity.Warning,
                RDCoreDiagnosticsResources.CaseComparedInSelectorType_Message));
}
