using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Semantics.Flow;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11030</c>: a <c>GoTo</c> or a <c>Resume</c> that jumps into a <c>For</c> or a <c>For Each</c> loop from outside it
/// (<strong>MS-VBAL §5.4.2.3</strong>, <strong>§5.4.2.4</strong>). The specification has the loop's <c>Next</c> raise error 92 (For loop not
/// initialized); MS-VBA raises it only for a <c>For Each</c> loop and a counter declared <c>Variant</c>, and steps any other counter on a loop that
/// ends at 0, as RD-VBA does (<strong>RD-VBAL §5.4.2.3</strong>). A warning, for code that relies on what either of them does.
/// </summary>
/// <remarks>
/// That a statement jumps into a loop is a fact of the procedure (<see cref="JumpIntoLoopFact"/>), found in its lowered body, which the analyzer
/// reads: it does not look at the syntax tree.
/// </remarks>
internal sealed class JumpIntoForLoopAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.JumpIntoForLoop.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.JumpsIntoLoops ?? [])
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.JumpIntoForLoop.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.JumpIntoForLoop_Message,
                    fact.Statement is JumpStatementKind.GoTo ? "GoTo" : "Resume")));
}
