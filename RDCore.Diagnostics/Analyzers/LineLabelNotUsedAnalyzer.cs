using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01016</c>: a line label of a procedure that no <c>GoTo</c>, <c>GoSub</c>, <c>On</c> ... <c>GoTo</c>, <c>On</c> ... <c>GoSub</c>, <c>Resume</c> or
/// <c>On Error GoTo</c> of the procedure names (<strong>MS-VBAL §5.4.1.1</strong>): a label belongs to its procedure, and nothing else can jump to it.
/// </summary>
/// <remarks>
/// <para>
/// A line number is not reported: it may be there for <c>Erl</c>, which reads the number of the line an error was raised on.
/// </para>
/// <para>
/// The fix removes the label: its line, when nothing else is written on it, or else the label and the spaces after it, which leaves the statement, the line
/// number or the comment of the line where it was. Nothing jumps to it, and the program does what it did.
/// </para>
/// </remarks>
internal sealed class LineLabelNotUsedAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.LineLabelNotUsed.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module)
        {
            yield break;
        }

        foreach (var procedure in module.Children.OfType<MemberDeclarationNode>())
        {
            var targets = procedure.Descendants()
                .SelectMany<SyntaxNode, ExpressionNode?>(node => node switch
                {
                    GoToStatementNode jump => [jump.LabelExpression],
                    GoSubStatementNode jump => [jump.LabelExpression],
                    OnGoToStatementNode jump => (IEnumerable<ExpressionNode?>)jump.Labels,
                    OnGoSubStatementNode jump => (IEnumerable<ExpressionNode?>)jump.Labels,
                    ResumeStatementNode jump => [jump.LabelExpression],
                    OnErrorGoToStatementNode jump => [jump.LabelExpression],
                    _ => [],
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
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.LineLabelNotUsed_Message, label.Name))
                {
                    Fixes = new DiagnosticFixes { Edits = [Removal(label, procedure, context.ParseResult)] },
                };
            }
        }
    }

    // the label's line when nothing else is written on it; otherwise the label, up to what is written after it on its line (the statement or the comment it
    // labels), or the label alone (after a line number).
    private static EditFix Removal(LineLabelNode label, MemberDeclarationNode procedure, ModuleParseResult parse)
    {
        var title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.LineLabelNotUsed_Fix, label.Name);
        if (LineEdits.WholeLinesOf(parse, label) is { } lines)
        {
            return SyntaxEdits.Fix(title, SyntaxEdits.Remove(lines));
        }

        var range = label.SourceLocation.Range;
        var next = procedure.Descendants().Concat(parse.Trivia)
            .Where(node => !ReferenceEquals(node, label))
            .Select(node => node.SourceLocation.Range.Start)
            .Where(start => start.Line == range.End.Line && start.CompareTo(range.End) >= 0)
            .OrderBy(start => start)
            .Cast<SourcePosition?>()
            .FirstOrDefault();

        return SyntaxEdits.Fix(title, SyntaxEdits.Remove(next is { } after ? new SourceRange(range.Start, after) : range));
    }
}
