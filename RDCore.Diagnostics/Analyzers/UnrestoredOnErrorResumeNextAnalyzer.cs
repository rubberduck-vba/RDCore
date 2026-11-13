using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00212</c>: an <c>On Error Resume Next</c> that no <c>On Error GoTo</c> follows in its procedure (<strong>MS-VBAL §5.4.4.1</strong>): every error of the
/// rest of the procedure is gone past, and the caller never sees it.
/// </summary>
/// <remarks>
/// The statements are read in the order they are written: an <c>On Error GoTo</c> to a label or <c>0</c> after the <c>Resume Next</c> sets another policy, and
/// the <c>Resume Next</c> is not reported. An <c>On Error GoTo -1</c> ends the error being handled and keeps the policy (RD-VBAL §5.4.4.1), and does not
/// count. Which statements run is not considered.
/// </remarks>
internal sealed class UnrestoredOnErrorResumeNextAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        foreach (var member in module.Children.OfType<MemberDeclarationNode>())
        {
            var unrestored = new List<OnErrorResumeStatementNode>();
            foreach (var statement in member.Descendants()
                .Where(node => node is OnErrorResumeStatementNode or OnErrorGoToStatementNode)
                .OrderBy(node => node.SourceLocation.Range.Start))
            {
                if (statement is OnErrorResumeStatementNode resume)
                {
                    unrestored.Add(resume);
                }
                else if (statement is OnErrorGoToStatementNode goTo && !OnErrorGoToMinusOneAnalyzer.IsMinusOne(goTo.LabelExpression))
                {
                    unrestored.Clear();
                }
            }

            foreach (var resume in unrestored)
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.UnrestoredOnErrorResumeNext.ToCode(),
                    resume.SourceLocation.Range,
                    DiagnosticSeverity.Warning,
                    RDCoreDiagnosticsResources.UnrestoredOnErrorResumeNext_Message);
            }
        }
    }
}
