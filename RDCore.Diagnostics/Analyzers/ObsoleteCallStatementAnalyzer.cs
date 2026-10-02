using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00302</c>: a <c>Call</c> statement written with the <c>Call</c> keyword, which is obsolete.
/// </summary>
/// <remarks>
/// The call is the same without the keyword. Whether a callee is the callee of a statement written with it is a fact of the expression
/// (<see cref="ValueExpressionSemanticFlags.ExplicitCallKeyword"/>), which is what this reads: it does not look at the syntax tree.
/// </remarks>
internal sealed class ObsoleteCallStatementAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.ExplicitCallKeyword))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.ObsoleteCallStatement, fact.Location.Range, DiagnosticSeverity.Hint, RDCoreDiagnosticsResources.ObsoleteCallStatement_Message));
}
