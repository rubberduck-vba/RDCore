using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.Runtime.Semantics.Precompiler;

/// <summary>
/// Evaluates every <c>#If</c>/<c>#ElseIf</c>/<c>#Else</c> block a module's
/// <c>ModuleParseResult.PrecompilerTrivia</c> carries, yielding the source ranges of every branch that
/// is <em>not</em> live (<strong>MS-VBAL §3.4.2</strong>). A body statement whose own
/// <see cref="StatementNode.SourceLocation"/> falls inside one of these ranges did not actually compile
/// — <c>InstructionListLowering</c> skips it, the same way the real MS-VBA preprocessor "logically
/// removes" an excluded block before the rest of the language ever sees it.
/// </summary>
/// <remarks>
/// <strong>Why ranges, not nodes.</strong> The parser blanks only the <c>#</c>-prefixed directive lines
/// before its main pass (<c>ModuleParser</c>) — both branches' statements survive into the ordinary body
/// AST as plain siblings, with no link back to which <c>#If</c>/branch they came from.
/// <c>ModuleParseResult.PrecompilerTrivia</c> is a completely separate array, built by a second,
/// independent parse of the same source text. The only thing the two passes still agree on is source
/// position, so that is what correlates a branch to the statements inside it.
/// <para>
/// A <c>#If</c>/<c>#ElseIf</c> condition is evaluated through the same <see cref="RuntimeExpressionEvaluator"/>
/// every other expression is — a conditional-compilation constant is an ordinary
/// <see cref="PrecompilerNameExpressionNode"/> that evaluator resolves against the global scope, and
/// every comparison/logical/arithmetic operator a condition can use already has real runtime semantics
/// there. When a condition's evaluation is not <see cref="RuntimeSemanticsEvaluationResult.IsSuccess"/>,
/// this reports no dead range for that whole <c>#If</c>/<c>#ElseIf</c>/<c>#Else</c> chain: an earlier
/// unresolved header could have been the one that mattered, so nothing after it can be trusted either.
/// </para>
/// </remarks>
public static class PrecompilerLiveBranchEvaluator
{
    /// <summary>
    /// Evaluates every <c>#If</c> block found (directly, or nested inside a live branch) in
    /// <paramref name="precompilerTrivia"/>.
    /// </summary>
    /// <param name="session">The runtime session <paramref name="evaluator"/> resolves conditional-compilation constants against.</param>
    /// <param name="evaluator">Evaluates a condition's expression tree.</param>
    /// <param name="context">The scope a condition's constants resolve from.</param>
    /// <param name="precompilerTrivia">A module's <c>ModuleParseResult.PrecompilerTrivia</c>.</param>
    /// <returns>The source ranges of every branch that is not live, in no particular order.</returns>
    public static ImmutableArray<SourceRange> GetDeadRanges(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, RuntimeEvaluationContext context, ImmutableArray<SyntaxNode> precompilerTrivia)
    {
        var deadRanges = ImmutableArray.CreateBuilder<SourceRange>();
        foreach (var node in precompilerTrivia)
        {
            Visit(node, session, evaluator, context, deadRanges);
        }
        return deadRanges.ToImmutable();
    }

    private static void Visit(SyntaxNode node, IRuntimeSession session, RuntimeExpressionEvaluator evaluator, RuntimeEvaluationContext context, ImmutableArray<SourceRange>.Builder deadRanges)
    {
        switch (node)
        {
            case PrecompilerIfBlockStatementNode ifBlock:
                EvaluateIfBlock(ifBlock, session, evaluator, context, deadRanges);
                break;
            case PrecompilerTriviaNode trivia:
                // a live branch's own body may itself contain a nested #Const/#If.
                foreach (var child in trivia.Children)
                {
                    Visit(child, session, evaluator, context, deadRanges);
                }
                break;
        }
    }

    private static void EvaluateIfBlock(PrecompilerIfBlockStatementNode ifBlock, IRuntimeSession session, RuntimeExpressionEvaluator evaluator, RuntimeEvaluationContext context, ImmutableArray<SourceRange>.Builder deadRanges)
    {
        var branches = CollectBranches(ifBlock);
        if (branches.Count == 0)
        {
            return;
        }

        var liveIndex = -1;
        for (var i = 0; i < branches.Count; i++)
        {
            var condition = branches[i].Condition;
            if (condition is null)
            {
                // #Else - reached only once every prior branch is determined false; always live then.
                liveIndex = i;
                break;
            }
            if (!TryEvaluateBoolean(session, evaluator, context, condition, out var isTrue))
            {
                return;
            }
            if (isTrue)
            {
                liveIndex = i;
                break;
            }
        }

        for (var i = 0; i < branches.Count; i++)
        {
            if (i == liveIndex)
            {
                continue;
            }
            if (branches[i].Body is { } deadBody)
            {
                deadRanges.Add(deadBody.SourceLocation.Range);
            }
        }

        if (liveIndex >= 0 && branches[liveIndex].Body is { } liveBody)
        {
            foreach (var child in liveBody.Children)
            {
                Visit(child, session, evaluator, context, deadRanges);
            }
        }
    }

    // One entry per #If/#ElseIf/#Else branch, in source order; Condition is null for #Else.
    private static List<(ExpressionNode? Condition, PrecompilerTriviaNode? Body)> CollectBranches(PrecompilerIfBlockStatementNode ifBlock)
    {
        var branches = new List<(ExpressionNode?, PrecompilerTriviaNode?)>();

        if (ifBlock.Children is [PrecompilerInlineIfStatementNode header, PrecompilerTriviaNode body, ..])
        {
            branches.Add((GetCondition(header.Children), body));
        }

        foreach (var elseIf in ifBlock.Children.OfType<PrecompilerElseIfBlockStatementNode>())
        {
            branches.Add((GetCondition(elseIf.Children), elseIf.Children.OfType<PrecompilerTriviaNode>().FirstOrDefault()));
        }

        if (ifBlock.Children.OfType<PrecompilerElseBlockStatementNode>().FirstOrDefault() is { } elseBlock)
        {
            branches.Add((null, elseBlock.Children.OfType<PrecompilerTriviaNode>().FirstOrDefault()));
        }

        return branches;
    }

    // The condition is whatever the ccExpression rule produced, directly at position 0.
    private static ExpressionNode? GetCondition(ImmutableArray<SyntaxNode> children)
        => children is [ExpressionNode condition, ..] ? condition : null;

    private static bool TryEvaluateBoolean(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, RuntimeEvaluationContext context, ExpressionNode condition, out bool value)
    {
        value = false;
        var result = evaluator.Evaluate(session, condition, context);
        return result.IsSuccess && result.Result is { } typed && TryCoerceBoolean(typed, out value);
    }

    private static bool TryCoerceBoolean(VBTypedValue value, out bool result)
    {
        if (value is VBBooleanValue boolean)
        {
            result = boolean.Value.StoredValue != 0;
            return true;
        }

        if (TryGetDouble(value, out var number))
        {
            result = number != 0;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryGetDouble(VBTypedValue value, out double result)
    {
        switch (value)
        {
            case VBBooleanValue boolean: result = boolean.Value.StoredValue != 0 ? -1 : 0; return true;
            case VBIntegerValue integer: result = integer.Value; return true;
            case VBLongValue longValue: result = longValue.Value; return true;
            case VBLongLongValue longLong: result = longLong.Value; return true;
            case VBSingleValue single: result = single.Value; return true;
            case VBDoubleValue @double: result = @double.Value; return true;
            default: result = 0; return false;
        }
    }
}
