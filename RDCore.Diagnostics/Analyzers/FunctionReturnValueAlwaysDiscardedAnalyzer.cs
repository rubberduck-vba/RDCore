using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00217</c>: a <c>Function</c> that every call of the project calls as a statement (<strong>MS-VBAL §5.4.2.1</strong>): no caller uses the value it
/// returns, and it can be a <c>Sub</c>.
/// </summary>
/// <remarks>
/// Only a procedure whose references the host counts over the project is found (<see cref="RDCore.SDK.Platform.Protocol.DeclarationFactDto.ReferencedFrom"/>):
/// a <c>Friend</c> one, and a <c>Public</c> one of a program, whose run starts at its entry point. Another <c>Public</c> function can be called by another
/// project or by the host that runs the project, and the calls of a <c>Private</c> one are not counted. A function no code calls is not found here.
/// </remarks>
internal sealed class FunctionReturnValueAlwaysDiscardedAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.FunctionReturnValueAlwaysDiscarded.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Declarations ?? [])
            .Where(declaration => declaration.Kind == DeclarationKind.Procedure
                && declaration.ReferencedFrom is { Length: > 0 } sites && sites.All(site => site.DiscardsResult))
            .OrderBy(declaration => declaration.Location.Range.Start)
            .Select(declaration => new AnalyzerFinding(
                RDCoreDiagnosticId.FunctionReturnValueAlwaysDiscarded.ToCode(),
                declaration.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.FunctionReturnValueAlwaysDiscarded_Message, declaration.Name)));
}
