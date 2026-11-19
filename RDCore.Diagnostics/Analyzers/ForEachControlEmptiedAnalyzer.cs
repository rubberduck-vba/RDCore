using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11032</c>: a read of a <c>For Each</c> control variable that sees it as the loop left it when it ran out: <c>Empty</c>, or <c>Nothing</c> for an object
/// variable, where MS-VBAL §5.4.2.4 says it holds the last element.
/// </summary>
/// <remarks>
/// That the read sees it emptied on every code path is a fact of the procedure (<see cref="EmptiedControlReadFact"/>), found by following its code paths, which
/// the analyzer reads. MS-VBA empties the variable, and so does RD-VBA (RD-VBAL §5.4.2.4): the warning shows where code depends on what MS-VBA does rather than
/// on what the specification says.
/// </remarks>
internal sealed class ForEachControlEmptiedAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ForEachControlEmptiedAfterLoop.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.EmptiedControlReads ?? [])
            .Select(read => new AnalyzerFinding(
                RDCoreDiagnosticId.ForEachControlEmptiedAfterLoop.ToCode(), read.Location.Range, DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(
                    read.IsObject ? RDCoreDiagnosticsResources.ForEachControlEmptiedAfterLoop_Nothing_Message : RDCoreDiagnosticsResources.ForEachControlEmptiedAfterLoop_Empty_Message,
                    read.Name)));
}
