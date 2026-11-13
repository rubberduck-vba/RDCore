using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00211</c>: <c>On Error GoTo -1</c>, which MS-VBAL does not document (<strong>MS-VBAL §5.4.4.1</strong>) and MS-VBA compiles: it ends the error
/// being handled, and keeps the policy.
/// </summary>
/// <remarks>
/// A <c>Resume</c> ends a handler and says where the procedure goes on; <c>On Error GoTo 0</c> turns error handling off. The <c>-1</c> is a literal, negated
/// or not, as the lowering of the statement reads it.
/// </remarks>
internal sealed class OnErrorGoToMinusOneAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<OnErrorGoToStatementNode>()
            .Where(statement => IsMinusOne(statement.LabelExpression))
            .Select(statement => new AnalyzerFinding(
                RDCoreDiagnosticId.OnErrorGoToMinusOne.ToCode(),
                statement.SourceLocation.Range,
                DiagnosticSeverity.Hint,
                RDCoreDiagnosticsResources.OnErrorGoToMinusOne_Message));

    internal static bool IsMinusOne(ExpressionNode label) => label switch
    {
        LiteralExpressionNode { StaticValue: VBIntegerValue { Value: -1 } } => true,
        VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp, Operand: LiteralExpressionNode { StaticValue: VBIntegerValue { Value: 1 } } } => true,
        _ => false,
    };
}
