using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00216</c>: a <c>Function</c> of the project called as a statement (<strong>MS-VBAL §5.4.2.1</strong>), which discards the value it returns.
/// </summary>
/// <remarks>
/// A function of a library (<c>MsgBox</c>) is not reported, nor is a call bound only when the code runs. A call written with
/// <c>Call</c> and an argument list is two expressions bound to the function, the name and the call: the name says it is a function.
/// </remarks>
internal sealed class FunctionReturnValueDiscardedAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.FunctionReturnValueDiscarded.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            return [];
        }

        return semantics.Procedures
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Binding is not null)
            .GroupBy(fact => (Binding: fact.Binding!.AbsoluteUri, fact.Location.Range.Start))
            .Where(call => call.Any(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.ResultDiscarded) && !fact.Flags.HasFlag(ValueExpressionSemanticFlags.BoundToLibrary))
                && call.Any(fact => fact.Classification == ExpressionClassification.Function))
            .Select(call => call.MinBy(fact => fact.Location.Range.End)!)
            .OrderBy(name => name.Location.Range.Start)
            .Select(name => new AnalyzerFinding(
                RDCoreDiagnosticId.FunctionReturnValueDiscarded.ToCode(),
                name.Location.Range,
                DiagnosticSeverity.Hint,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.FunctionReturnValueDiscarded_Message, SymbolUri.NameOf(name.Binding!))));
    }
}
