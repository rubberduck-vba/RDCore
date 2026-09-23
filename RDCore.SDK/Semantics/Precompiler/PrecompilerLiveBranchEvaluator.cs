using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Precompiler;

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
/// <strong>Indeterminate conditions.</strong> When a condition cannot be folded to a definite Boolean
/// (see <see cref="PrecompilerConstantExpressionEvaluator"/> — an unsupported operator, an unresolvable
/// name), this pass reports no dead range for that whole <c>#If</c>/<c>#ElseIf</c>/<c>#Else</c> chain at
/// all, rather than guessing: an earlier indeterminate header could have been the one that mattered, so
/// nothing after it can be trusted either.
/// </para>
/// </remarks>
public static class PrecompilerLiveBranchEvaluator
{
    /// <summary>
    /// Evaluates every <c>#If</c> block found (directly, or nested inside a live branch) in
    /// <paramref name="precompilerTrivia"/>.
    /// </summary>
    /// <param name="precompilerTrivia">A module's <c>ModuleParseResult.PrecompilerTrivia</c>.</param>
    /// <param name="resolver">Resolves a condition's named constants — see <see cref="PrecompilerConstantExpressionEvaluator"/>.</param>
    /// <returns>The source ranges of every branch that is not live, in no particular order.</returns>
    public static ImmutableArray<SourceRange> GetDeadRanges(ImmutableArray<SyntaxNode> precompilerTrivia, ISymbolResolver resolver)
    {
        var deadRanges = ImmutableArray.CreateBuilder<SourceRange>();
        foreach (var node in precompilerTrivia)
        {
            Visit(node, resolver, deadRanges);
        }
        return deadRanges.ToImmutable();
    }

    private static void Visit(SyntaxNode node, ISymbolResolver resolver, ImmutableArray<SourceRange>.Builder deadRanges)
    {
        switch (node)
        {
            case PrecompilerIfBlockStatementNode ifBlock:
                EvaluateIfBlock(ifBlock, resolver, deadRanges);
                break;
            case PrecompilerTriviaNode trivia:
                // a live branch's own body may itself contain a nested #Const/#If.
                foreach (var child in trivia.Children)
                {
                    Visit(child, resolver, deadRanges);
                }
                break;
        }
    }

    private static void EvaluateIfBlock(PrecompilerIfBlockStatementNode ifBlock, ISymbolResolver resolver, ImmutableArray<SourceRange>.Builder deadRanges)
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
            if (!PrecompilerConstantExpressionEvaluator.TryEvaluateBoolean(resolver, condition, out var isTrue))
            {
                // indeterminate: an earlier branch could be the live one for a reason this pass can't
                // see, so nothing in this whole #If chain can be reported as dead with confidence.
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
                Visit(child, resolver, deadRanges);
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

    // The condition is whatever the ccExpression rule produced, directly at position 0 - a
    // ConditionalExpressionNode wrapper is never actually emitted here (only PrecompilerNameExpressionNode/
    // VBBinaryOperatorExpressionNode/VBUnaryOperatorExpressionNode/LiteralExpressionNode are, confirmed
    // against the real parser output), but PrecompilerConstantExpressionEvaluator still unwraps one
    // defensively in case a future grammar change (or another cc-expression caller) produces one.
    private static ExpressionNode? GetCondition(ImmutableArray<SyntaxNode> children)
        => children is [ExpressionNode condition, ..] ? condition : null;
}
