using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11006</c>: the addition operator concatenates two <c>String</c> values (<strong>MS-VBAL §5.6.9.3.2</strong>).
/// </summary>
/// <remarks>
/// That the <em>effective type</em> of the operation is <c>String</c> is a fact the language core states about it (<see cref="RDCore.SDK.Platform.Protocol.OperatorFactDto"/>):
/// <c>+</c> then concatenates, which <c>&amp;</c> says without the reader having to know the types of both operands. Nothing is reported for a procedure that was not evaluated.
/// </remarks>
internal sealed class AmbiguousConcatenationAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.AmbiguousConcatenation.ToCode()];

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Operations ?? [])
            .Where(op => op.Operator == Tokens.AdditionOp && (op.ArithmeticOpSemanticFlags?.HasFlag(ArithmeticOperatorSemanticFlags.VBStringEffectiveType) ?? false))
            .Select(op => new AnalyzerFinding(
                RDCoreDiagnosticId.AmbiguousConcatenation.ToCode(),
                (op.OperatorLocation ?? op.Location).Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(
                    RDCoreDiagnosticsResources.AmbiguousConcatenation_Message,
                    op.Operator)));
}
