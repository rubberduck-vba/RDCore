using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00209</c>: an <c>If…Else</c>, on several lines or on one, whose two branches each assign one Boolean literal to the same name, <c>True</c> in one and
/// <c>False</c> in the other (<strong>MS-VBAL §5.4.2.8</strong>, <strong>§5.4.2.9</strong>): the assignment of the condition, or of its negation, says the same.
/// </summary>
/// <remarks>
/// An <c>ElseIf</c>, a second statement or a <c>Set</c> in a branch makes it something else. The name is a variable, or a member of one (<c>Me.Done</c>,
/// <c>o.Done</c>); an element of an array is not compared. The finding is the header of the <c>If</c>.
/// <para>
/// The fix that assigns the condition is offered where the condition is a Boolean whatever its operands hold: an <c>Is</c> or <c>TypeOf…Is</c> operation, a
/// Boolean literal, and <c>Not</c>, <c>And</c>, <c>Or</c>, <c>Xor</c>, <c>Eqv</c>, <c>Imp</c> and parentheses of those. Another condition can be <c>Null</c>, which
/// an <c>If</c> takes as <c>False</c> (<strong>MS-VBAL §5.4.2.8</strong>), or a number other than <c>0</c> and <c>-1</c>, whose <c>Not</c> is not <c>False</c>.
/// </para>
/// </remarks>
internal sealed class BooleanAssignedInIfElseAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module)
        {
            yield break;
        }

        foreach (var node in module.Descendants())
        {
            var (statement, condition, then, otherwise) = node switch
            {
                IfBlockStatementNode { ElseIfBlocks.IsDefaultOrEmpty: true, ElseBlock: { } elseBlock } block => (block, block.ConditionExpression, block.Body, elseBlock.Body),
                InlineIfStatementNode { ElseBody: { } elseBody } inline => ((StatementNode?)inline, inline.ConditionExpression, inline.ThenBody, elseBody),
                _ => (null, null, null, null),
            };

            if (statement is not null && condition is not null && then is not null && otherwise is not null
                && Assigned(then) is ({ } name, { } value)
                && Assigned(otherwise) is ({ } otherName, { } otherValue)
                && string.Equals(name, otherName, StringComparison.OrdinalIgnoreCase)
                && value != otherValue)
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.BooleanAssignedInIfElse.ToCode(),
                    new SourceRange(statement.SourceLocation.Range.Start, condition.SourceLocation.Range.End),
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.BooleanAssignedInIfElse_Message, name))
                {
                    Fixes = FixOf(context.ParseResult, statement, condition, name, value) is { } fix ? new DiagnosticFixes { Edit = fix } : null,
                };
            }
        }
    }

    // the one statement of the branch, when it Let-assigns True or False to a name.
    private static (string? Name, bool? Value) Assigned(StatementBlock body)
        => body.Children is [AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet } assignment]
            && assignment.Value is LiteralExpressionNode { StaticValue: VBBooleanValue literal }
            && NameOf(assignment.Target) is { } name
                ? (name, literal.Value.Equals(VBBooleanValue.True.Value))
                : (null, null);

    private static string? NameOf(ExpressionNode target) => target switch
    {
        SimpleNameExpressionNode simple => simple.IdentifierName,
        MemberAccessExpressionNode { Owner: { } owner } member when NameOf(owner) is { } qualifier => $"{qualifier}.{member.Member.IdentifierName}",
        InstanceExpressionNode => "Me",
        _ => null,
    };

    // `name = condition` (or `name = Not (condition)`) in place of the statement, the condition kept as it is written; not where a comment would go with it.
    private static EditFix? FixOf(ModuleParseResult parse, StatementNode statement, ExpressionNode conditionExpression, string name, bool value)
    {
        var range = statement.SourceLocation.Range;
        var condition = conditionExpression.SourceLocation.Range;
        if (!IsBoolean(conditionExpression)
            || parse.Trivia.Concat(parse.PrecompilerTrivia).Any(trivia => trivia.SourceLocation.Range is var at && at.End > range.Start && at.Start < range.End))
        {
            return null;
        }

        return new EditFix
        {
            Title = AnalyzerMessages.Format(value ? RDCoreDiagnosticsResources.BooleanAssignedInIfElse_Fix : RDCoreDiagnosticsResources.BooleanAssignedInIfElse_NegatedFix, name),
            Edits =
            [
                new TextEdit { Range = new SourceRange(range.Start, condition.Start).ToLsp(), NewText = value ? $"{name} = " : $"{name} = Not (" },
                new TextEdit { Range = new SourceRange(condition.End, range.End).ToLsp(), NewText = value ? string.Empty : ")" },
            ],
        };
    }

    // a Boolean, whatever the operands hold: never Null, never another number.
    private static bool IsBoolean(ExpressionNode expression) => expression switch
    {
        TypeOfIsExpressionNode => true,
        LiteralExpressionNode { StaticValue: VBBooleanValue } => true,
        VBBinaryOperatorExpressionNode { Token: Tokens.CompareIsOp } => true,
        VBBinaryOperatorExpressionNode { Token: Tokens.LogicalAndOp or Tokens.LogicalOrOp or Tokens.LogicalXOrOp or Tokens.LogicalEqvOp or Tokens.LogicalImpOp } operation
            => IsBoolean(operation.Left) && IsBoolean(operation.Right),
        VBUnaryOperatorExpressionNode { Token: Tokens.LogicalNotOp or OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp } operation
            => IsBoolean(operation.Operand),
        _ => false,
    };
}
