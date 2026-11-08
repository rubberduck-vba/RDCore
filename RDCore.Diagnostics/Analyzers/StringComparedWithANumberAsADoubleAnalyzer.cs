using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11027</c>: a relational operator compares a <c>String</c> with a <c>Byte</c>, an <c>Integer</c> or a <c>Long</c> - or a <c>Variant</c> holding a
/// <c>String</c> with a <c>Single</c> or a <c>LongLong</c> - and MS-VBA compares the two as <c>Double</c> values, where <strong>MS-VBAL §5.6.9.5</strong>
/// compares them in the number's type.
/// </summary>
/// <remarks>
/// That the two are compared as <c>Double</c> values is a fact the language core states about the comparison
/// (<see cref="ComparisonOperatorSemanticFlags.IsStringComparedAsADouble"/>), where its result can differ from the specification's. RD-VBA compares as
/// MS-VBA does: <c>"2.5" = 2</c> is <c>False</c>, where the specification Let-coerces <c>"2.5"</c> to <c>2</c>; and <c>"40000"</c> compared with an
/// <c>Integer</c> does not overflow. A warning, equivalent to the departure. Nothing is reported for a procedure that was not evaluated.
/// </remarks>
internal sealed class StringComparedWithANumberAsADoubleAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Operations ?? [])
            .Where(op => op.ComparisonOpSemanticFlags?.HasFlag(ComparisonOperatorSemanticFlags.IsStringComparedAsADouble) ?? false)
            .Select(op => new AnalyzerFinding(
                RDCoreDiagnosticId.StringComparedWithANumberAsADouble,
                (op.OperatorLocation ?? op.Location).Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(
                    RDCoreDiagnosticsResources.StringComparedWithANumberAsADouble_Message,
                    op.Operator)));
}
