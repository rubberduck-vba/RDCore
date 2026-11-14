using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// The <c>Step</c> of a <c>For</c> loop, which is <c>1</c> when there is none (<strong>MS-VBAL §5.4.2.3</strong>), as the convention says it is written
/// (<see cref="DiagnosticsOptions.ForStep"/>): <c>RDC00313</c>, a <c>Step 1</c> under the implicit convention; <c>RDC00116</c>, a loop with no <c>Step</c> under the
/// explicit one.
/// </summary>
/// <param name="options">The settings of the extension.</param>
internal sealed class ForStepAnalyzer(IOptions<DiagnosticsOptions> options) : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        var loops = module.Descendants().OfType<ForStatementNode>();
        return options.Value.ForStep is ForStepConvention.Explicit
            ? loops.Where(loop => loop.StepExpression is null)
                .Select(loop => new AnalyzerFinding(
                    RDCoreDiagnosticId.ImplicitForStep.ToCode(),
                    new SourceRange(loop.SourceLocation.Range.Start, loop.EndExpression.SourceLocation.Range.End),
                    DiagnosticSeverity.Information,
                    RDCoreDiagnosticsResources.ImplicitForStep_Message)
                {
                    Fixes = Fix(RDCoreDiagnosticsResources.ImplicitForStep_Fix, new SourceRange(loop.EndExpression.SourceLocation.Range.End, loop.EndExpression.SourceLocation.Range.End), " Step 1"),
                })
            : loops.Where(loop => loop.StepExpression is LiteralExpressionNode { StaticValue: VBIntegerValue { Value: 1 } })
                .Select(loop => new AnalyzerFinding(
                    RDCoreDiagnosticId.RedundantStepOne.ToCode(),
                    loop.StepExpression!.SourceLocation.Range,
                    DiagnosticSeverity.Information,
                    RDCoreDiagnosticsResources.RedundantStepOne_Message)
                {
                    // the Step clause, from the end of the bound before it.
                    Fixes = Fix(RDCoreDiagnosticsResources.RedundantStepOne_Fix, new SourceRange(loop.EndExpression.SourceLocation.Range.End, loop.StepExpression.SourceLocation.Range.End), string.Empty),
                });
    }

    // the Integer 1 is the step of a loop that states none: the loop counts the same with or without it.
    private static DiagnosticFixes Fix(string title, SourceRange range, string text)
        => new() { Edit = new EditFix { Title = title, Edits = [new TextEdit { Range = range.ToLsp(), NewText = text }] } };
}
