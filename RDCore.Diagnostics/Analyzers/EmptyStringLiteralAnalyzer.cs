using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00312</c>: the string literal <c>""</c> (<strong>MS-VBAL §3.3.4</strong>), where the constant <c>vbNullString</c> can be written.
/// </summary>
/// <remarks>
/// Both are a String of no characters, and compare equal; only the address of their characters tells them apart (<c>StrPtr</c>, and a String given to a procedure of a
/// DLL), which is <c>0</c> for <c>vbNullString</c>. The finding is the literal: the only string literal two characters long. Where the literal is an operand of a
/// comparison (<strong>MS-VBAL §5.6.9.5</strong>, and <c>Like</c>) or a value of a <c>Case</c> clause, only its characters count, and the fix replaces it with
/// <c>vbNullString</c>. Anywhere else the String may reach code that tells them apart, and there is no fix.
/// </remarks>
internal sealed class EmptyStringLiteralAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        var nodes = module.Descendants().ToArray();
        var compared = nodes.SelectMany(ComparedOperands).Select(operand => operand.Identity).ToHashSet();
        return nodes
            .OfType<LiteralExpressionNode>()
            .Where(literal => literal is { StaticValue: VBStringValue, SourceLocation.Range: var range }
                && range.Start.Line == range.End.Line && range.End.Character - range.Start.Character == 2)
            .Select(literal => new AnalyzerFinding(
                RDCoreDiagnosticId.EmptyStringLiteral.ToCode(),
                literal.SourceLocation.Range,
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.EmptyStringLiteral_Message)
            {
                Fixes = compared.Contains(literal.Identity)
                    ? new DiagnosticFixes
                    {
                        Edit = new EditFix
                        {
                            Title = RDCoreDiagnosticsResources.EmptyStringLiteral_Fix,
                            Edits = [new TextEdit { Range = literal.SourceLocation.Range.ToLsp(), NewText = "vbNullString" }],
                        },
                    }
                    : null,
            });
    }

    // the operands whose value is only compared: the result of the comparison is a Boolean, whatever the address of the characters.
    private static IEnumerable<ExpressionNode> ComparedOperands(SyntaxNode node) => node switch
    {
        VBBinaryOperatorExpressionNode
        {
            Token: Tokens.CompareEqualOp or Tokens.CompareNotEqualOp or Tokens.CompareLessThanOp or Tokens.CompareLessThanOrEqualOp
                or Tokens.CompareGreaterThanOp or Tokens.CompareGreaterThanOrEqualOp or Tokens.CompareLikeOp,
        } operation => [operation.Left, operation.Right],
        CaseValueRangeClauseNode clause => [clause.Value],
        CaseComparisonRangeClauseNode clause => [clause.Value],
        CaseToRangeClauseNode clause => [clause.Start, clause.End],
        _ => [],
    };
}
