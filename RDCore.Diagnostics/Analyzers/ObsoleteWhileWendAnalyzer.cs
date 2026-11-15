using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
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
/// <para>
/// <c>Do While…Loop</c> is the same loop, and the one that <c>Exit Do</c> can leave. The finding is the header, <c>While</c> and its condition.
/// </para>
/// <para>
/// The fix writes <c>Do</c> before <c>While</c> and <c>Loop</c> in place of <c>Wend</c>. An <c>Exit Do</c> in the body that is not in a <c>Do</c> loop of its
/// own leaves a <c>Do</c> loop around this one, and would leave the new loop instead, so the fix is not offered while there is one.
/// </para>
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
                RDCoreDiagnosticsResources.ObsoleteWhileWend_Message) { Fixes = FixOf(loop) });

    private static DiagnosticFixes? FixOf(WhileWendStatementNode loop)
    {
        var range = loop.SourceLocation.Range;
        var descendants = loop.Descendants().ToList();
        var doLoops = descendants.Where(IsDoLoop).Select(doLoop => doLoop.SourceLocation.Range).ToList();
        if (descendants.Any(node => node is KeywordStatementNode { Token: Tokens.ExitDo } exit
            && !doLoops.Any(doLoop => doLoop.Contains(exit.SourceLocation.Range.Start))))
        {
            return null;
        }

        return new DiagnosticFixes
        {
            Edits = [SyntaxEdits.Fix(RDCoreDiagnosticsResources.ObsoleteWhileWend_Fix,
                SyntaxEdits.Insert(range.Start, "Do "),
                SyntaxEdits.Replace(new SourceRange(new SourcePosition(range.End.Line, range.End.Character - "Wend".Length), range.End), "Loop"))],
        };
    }

    private static bool IsDoLoop(SyntaxNode node)
        => node is DoLoopStatementNode or DoWhileLoopStatementNode or DoUntilLoopStatementNode or DoLoopWhileStatementNode or DoLoopUntilStatementNode;
}
