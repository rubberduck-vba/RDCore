using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00308</c>: a <c>While…Wend</c> loop (<strong>MS-VBAL §5.4.2.2</strong>).
/// </summary>
/// <remarks>
/// <c>Do While…Loop</c> is the same loop, and the one that <c>Exit Do</c> can leave. The finding is the header, <c>While</c> and its condition.
/// </remarks>
internal sealed class ObsoleteWhileWendAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ObsoleteWhileWend.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<WhileWendStatementNode>()
            .Select(loop => new AnalyzerFinding(
                RDCoreDiagnosticId.ObsoleteWhileWend.ToCode(),
                new SourceRange(loop.SourceLocation.Range.Start, loop.ConditionExpression.Location.Range.End),
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.ObsoleteWhileWend_Message));
}
