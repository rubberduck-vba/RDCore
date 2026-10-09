using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00309</c>: an <c>On Local Error</c> statement (<strong>MS-VBAL §5.4.4.1</strong>).
/// </summary>
/// <remarks>
/// Every error is local to the procedure it is handled in, so <c>Local</c> says nothing: <c>On Error</c> is the same statement.
/// </remarks>
internal sealed class ObsoleteOnLocalErrorStatementAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .Where(node => node is OnErrorGoToStatementNode { IsLocal: true } or OnErrorResumeStatementNode { IsLocal: true })
            .Select(statement => new AnalyzerFinding(
                RDCoreDiagnosticId.ObsoleteOnLocalErrorStatement,
                statement.SourceLocation.Range,
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.ObsoleteOnLocalErrorStatement_Message));
}
