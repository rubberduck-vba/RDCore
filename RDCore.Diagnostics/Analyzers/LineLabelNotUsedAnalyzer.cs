using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01016</c>: a line label of a procedure that no <c>GoTo</c>, <c>GoSub</c>, <c>Resume</c> or <c>On Error GoTo</c> of the procedure names
/// (<strong>MS-VBAL §5.4.1.1</strong>): a label belongs to its procedure, and nothing else can jump to it.
/// </summary>
/// <remarks>
/// A line number is not reported: it may be there for <c>Erl</c>, which reads the number of the line an error was raised on.
/// </remarks>
internal sealed class LineLabelNotUsedAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        foreach (var procedure in module.Children.OfType<MemberDeclarationNode>())
        {
            var targets = procedure.Descendants()
                .Select(node => node switch
                {
                    GoToStatementNode jump => jump.LabelExpression,
                    GoSubStatementNode jump => jump.LabelExpression,
                    ResumeStatementNode jump => jump.LabelExpression,
                    OnErrorGoToStatementNode jump => jump.LabelExpression,
                    _ => null,
                })
                .OfType<SimpleNameExpressionNode>()
                .Select(name => name.IdentifierName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // a LineNumberNode is a LineLabelNode too.
            foreach (var label in procedure.Descendants().OfType<LineLabelNode>().Where(label => label is not LineNumberNode && !targets.Contains(label.Name)))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.LineLabelNotUsed.ToCode(),
                    label.SourceLocation.Range,
                    DiagnosticSeverity.Information,
                    Say(RDCoreDiagnosticsResources.LineLabelNotUsed_Message, label.Name));
            }
        }
    }
}
