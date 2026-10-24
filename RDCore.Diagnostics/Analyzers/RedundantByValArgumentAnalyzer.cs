using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00310</c>: an argument written <c>ByVal</c> for a parameter that is <c>ByVal</c> already, where the keyword changes nothing.
/// </summary>
/// <remarks>
/// MS-VBA compiles it and MS-VBAL §5.6.13.1 refuses it. Whether the parameter is <c>ByVal</c> is a fact of the argument
/// (<see cref="ValueExpressionSemanticFlags.RedundantByVal"/>), which is what this reads: it does not look at the syntax tree.
/// </remarks>
internal sealed class RedundantByValArgumentAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.RedundantByVal))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.RedundantByValArgument, fact.Location.Range, DiagnosticSeverity.Hint, RDCoreDiagnosticsResources.RedundantByValArgument_Message));
}
