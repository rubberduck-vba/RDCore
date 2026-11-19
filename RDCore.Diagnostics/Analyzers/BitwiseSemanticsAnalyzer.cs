using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11005</c>: a logical operator works on the bits of numbers rather than on <c>Boolean</c> values (<strong>MS-VBAL §5.6.9.8</strong>).
/// </summary>
/// <remarks>
/// That the operation is bitwise is a fact the language core states about it (<see cref="LogicalOperatorSemanticFlags.IsBitwiseSemantics"/>), from the effective type of
/// the operation. <c>a And b</c> of two numbers is not <c>a &lt;&gt; 0 And b &lt;&gt; 0</c>: <c>1 And 2</c> is <c>0</c>. A hint, because it is often what is meant.
/// Nothing is reported for a procedure that was not evaluated.
/// </remarks>
internal sealed class BitwiseSemanticsAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.BitwiseOperator.ToCode()];

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Operations ?? [])
            .Where(op => op.LogicalOpSemanticFlags?.HasFlag(LogicalOperatorSemanticFlags.IsBitwiseSemantics) ?? false)
            .Select(op => new AnalyzerFinding(
                RDCoreDiagnosticId.BitwiseOperator.ToCode(),
                (op.OperatorLocation ?? op.Location).Range,
                DiagnosticSeverity.Hint,
                AnalyzerMessages.Format(
                    RDCoreDiagnosticsResources.BitwiseOperator_Message,
                    op.Operator)));
}
