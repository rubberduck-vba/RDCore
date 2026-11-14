using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11036</c>: a line number below <c>0</c>, defined (<c>-5:</c>) or jumped to (<c>GoTo -5</c>, <c>Resume -5</c>): a line number of
/// <strong>MS-VBAL §5.4.1.1</strong> is a decimal literal, with no sign, and MS-VBA compiles one with a minus.
/// </summary>
/// <remarks>
/// <c>On Error GoTo -1</c> names no line: it is a statement of its own, and <c>RDC00211</c>. The number of a jump is a negative literal, or a literal
/// negated, as the lowering of the statement reads it.
/// </remarks>
internal sealed class NegativeLineNumberAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        foreach (var node in module.Descendants())
        {
            var range = node switch
            {
                LineNumberNode { Number: < 0 } line => line.SourceLocation.Range,
                GoToStatementNode jump when IsNegative(jump.LabelExpression) => jump.LabelExpression.SourceLocation.Range,
                GoSubStatementNode jump when IsNegative(jump.LabelExpression) => jump.LabelExpression.SourceLocation.Range,
                ResumeStatementNode { LabelExpression: { } label } when IsNegative(label) => label.SourceLocation.Range,
                OnErrorGoToStatementNode jump when IsNegative(jump.LabelExpression) && !IsMinusOne(jump.LabelExpression) => jump.LabelExpression.SourceLocation.Range,
                _ => (SourceRange?)null,
            };

            if (range is { } finding)
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.NegativeLineNumber.ToCode(),
                    finding,
                    DiagnosticSeverity.Warning,
                    RDCoreDiagnosticsResources.NegativeLineNumber_Message);
            }
        }
    }

    private static bool IsNegative(ExpressionNode label) => label switch
    {
        LiteralExpressionNode { StaticValue: VBIntegerValue { Value: < 0 } or VBLongValue { Value: < 0 } } => true,
        VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp, Operand: LiteralExpressionNode } => true,
        _ => false,
    };

    private static bool IsMinusOne(ExpressionNode label) => label switch
    {
        LiteralExpressionNode { StaticValue: VBIntegerValue { Value: -1 } } => true,
        VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp, Operand: LiteralExpressionNode { StaticValue: VBIntegerValue { Value: 1 } } } => true,
        _ => false,
    };
}
