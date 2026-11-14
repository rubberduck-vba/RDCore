using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00309</c>: an <c>On Local Error</c> statement (<strong>MS-VBAL §5.4.4.1</strong>).
/// </summary>
/// <remarks>
/// Every error is local to the procedure it is handled in, so <c>Local</c> says nothing: <c>On Error</c> is the same statement, and the fix writes it again
/// without the keyword, up to its label or to its end.
/// </remarks>
internal sealed class ObsoleteOnLocalErrorStatementAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ObsoleteOnLocalErrorStatement.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .Where(node => node is OnErrorGoToStatementNode { IsLocal: true } or OnErrorResumeStatementNode { IsLocal: true })
            .Select(statement => new AnalyzerFinding(
                RDCoreDiagnosticId.ObsoleteOnLocalErrorStatement.ToCode(),
                statement.SourceLocation.Range,
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.ObsoleteOnLocalErrorStatement_Message)
            {
                Fixes = new DiagnosticFixes
                {
                    Edit = SyntaxEdits.Fix(RDCoreDiagnosticsResources.ObsoleteOnLocalErrorStatement_Fix, statement is OnErrorGoToStatementNode goTo
                        ? SyntaxEdits.Replace(new SourceRange(goTo.SourceLocation.Range.Start, goTo.LabelExpression.SourceLocation.Range.Start), "On Error GoTo ")
                        : SyntaxEdits.Replace(statement.SourceLocation.Range, "On Error Resume Next")),
                },
            });
}
