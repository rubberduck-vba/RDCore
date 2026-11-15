using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Semantics.Flow;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00206</c>: a function or a property getter with a code path that ends without assigning its return value (<strong>MS-VBAL §5.3.1</strong>).
/// </summary>
/// <remarks>
/// That a code path does not assign the return value is a fact of the procedure (<see cref="ReturnValueFact"/>), found by following the code paths of its body, which
/// the analyzer reads: it does not look at the syntax tree. The procedure is not reported when the fact is not stated, nor when its body has no statement, which is how a
/// member of an interface is written.
/// </remarks>
internal sealed class NotAllPathsReturnValueAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.NotAllPathsReturnValue.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .Select(procedure => procedure.ReturnValue)
            .OfType<ReturnValueFact>()
            .Where(fact => fact is { IsEmpty: false, Assignment: not ReturnValueAssignment.Always })
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.NotAllPathsReturnValue.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(
                    fact.Assignment is ReturnValueAssignment.Never
                        ? RDCoreDiagnosticsResources.NotAllPathsReturnValue_NeverAssigned_Message
                        : RDCoreDiagnosticsResources.NotAllPathsReturnValue_Message,
                    fact.Name)));
}
