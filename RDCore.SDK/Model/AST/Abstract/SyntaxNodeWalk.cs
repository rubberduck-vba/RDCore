using RDCore.SDK.Model.AST.Statements;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// Walks the syntax tree the way it is written, statements in the blocks of structured statements included.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="SyntaxNode.Children"/> of a node are the nodes it is made of, but not the statements of the blocks it guards: the body of an <c>If</c>, a loop, a
/// <c>With</c> or a <c>Case</c> is a <see cref="StatementBlock"/> that the statement holds on its own, and so are the <c>ElseIf</c> and <c>Else</c> branches of an
/// <c>If</c> and the <c>Case</c> branches of a <c>Select Case</c>. Walking <see cref="SyntaxNode.Children"/> alone therefore never gets inside any of them: a
/// declaration, or a statement, written in one is not found. This is where the tree is walked for whoever needs to see all of it.
/// </para>
/// <para>
/// The bounds of a declared array (<c>Dim a(1 To n)</c>) are held the same way: an <see cref="Declarations.ArrayBoundsNode"/> has no children, and keeps the
/// expression of each bound in its <see cref="Declarations.ArrayBoundsNode.Bounds"/>. They are walked as what the clause is made of.
/// </para>
/// <para>
/// So is the variable a <c>Next</c> names (<c>Next i</c>): a <c>For</c> or <c>For Each</c> loop keeps it in its <see cref="ForStatementNode.NextExpression"/>
/// or <see cref="ForEachStatementNode.NextExpression"/>, and it is walked after the statements of the loop's body, where it is written. It is a reference to
/// the loop's control variable, which a rename rewrites and a search for references finds.
/// </para>
/// </remarks>
public static class SyntaxNodeWalk
{
    /// <summary>
    /// The nodes <paramref name="node"/> is directly made of: its <see cref="SyntaxNode.Children"/>, the branches of a structured statement, the statements of
    /// its own blocks, the variable the <c>Next</c> of a loop names, and the expressions of the bounds of a declared array.
    /// </summary>
    /// <param name="node">A node of the tree.</param>
    /// <returns>The nodes, in the order they are written.</returns>
    public static IEnumerable<SyntaxNode> ChildNodes(this SyntaxNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
        }

        foreach (var held in HeldNodes(node))
        {
            yield return held;
        }

        foreach (var block in BlocksOf(node))
        {
            foreach (var statement in block.Children)
            {
                yield return statement;
            }
        }

        // the variable a Next names is written after the loop's body.
        if (NextOf(node) is { } next)
        {
            yield return next;
        }
    }

    // the variable named by the Next that ends a loop: Next i.
    private static ExpressionNode? NextOf(SyntaxNode node) => node switch
    {
        ForStatementNode loop => loop.NextExpression,
        ForEachStatementNode loop => loop.NextExpression,
        _ => null,
    };

    /// <summary>
    /// Every node under <paramref name="node"/>, depth first and in the order they are written, however deep in the blocks of structured statements; not the node itself.
    /// </summary>
    /// <param name="node">A node of the tree.</param>
    /// <remarks>
    /// The walk keeps its own stack of the nodes it is in: however deep the tree is, it uses no more of the caller's thread's stack than for a shallow one.
    /// </remarks>
    public static IEnumerable<SyntaxNode> Descendants(this SyntaxNode node)
    {
        var pending = new Stack<IEnumerator<SyntaxNode>>();
        pending.Push(node.ChildNodes().GetEnumerator());
        try
        {
            while (pending.TryPeek(out var siblings))
            {
                if (!siblings.MoveNext())
                {
                    pending.Pop().Dispose();
                    continue;
                }

                var child = siblings.Current;
                yield return child;
                pending.Push(child.ChildNodes().GetEnumerator());
            }
        }
        finally
        {
            while (pending.TryPop(out var unfinished))
            {
                unfinished.Dispose();
            }
        }
    }

    /// <summary>
    /// The lists of sibling nodes that <paramref name="node"/> holds: its <see cref="SyntaxNode.Children"/>, and the statements of each block it holds, each a list of
    /// its own.
    /// </summary>
    /// <param name="node">A node of the tree.</param>
    /// <remarks>
    /// Whether one node follows another is a question about siblings: the statements of two blocks are not each other's, however they are written.
    /// </remarks>
    public static IEnumerable<IReadOnlyList<SyntaxNode>> SiblingLists(this SyntaxNode node)
    {
        yield return node.Children;
        foreach (var block in BlocksOf(node))
        {
            yield return block.Children;
        }
    }

    // the branches that a structured statement holds as nodes of their own, which are not among its children.
    private static IEnumerable<SyntaxNode> HeldNodes(SyntaxNode node)
    {
        switch (node)
        {
            case IfBlockStatementNode ifBlock:
                foreach (var elseIf in ifBlock.ElseIfBlocks)
                {
                    yield return elseIf;
                }

                if (ifBlock.ElseBlock is { } elseBlock)
                {
                    yield return elseBlock;
                }

                break;

            case SelectCaseStatementNode select:
                foreach (var caseBlock in select.CaseExpressionBlocks)
                {
                    yield return caseBlock;
                }

                if (select.CaseElseBlock is { } caseElse)
                {
                    yield return caseElse;
                }

                break;

            case CaseExpressionStatementNode caseExpression:
                foreach (var clause in caseExpression.RangeClauses)
                {
                    yield return clause;
                }

                break;

            case Declarations.ArrayBoundsNode arrayBounds when !arrayBounds.Bounds.IsDefault:
                foreach (var bound in arrayBounds.Bounds)
                {
                    if (bound.LowerExpression is { } lower)
                    {
                        yield return lower;
                    }

                    if (bound.UpperExpression is { } upper)
                    {
                        yield return upper;
                    }
                }

                break;
        }
    }

    // the blocks of statements that a statement guards.
    private static IEnumerable<StatementBlock> BlocksOf(SyntaxNode node)
    {
        switch (node)
        {
            case IfBlockStatementNode statement:
                yield return statement.Body;
                break;
            case ElseIfBlockStatementNode statement:
                yield return statement.Body;
                break;
            case ElseBlockStatementNode statement:
                yield return statement.Body;
                break;
            case InlineIfStatementNode statement:
                yield return statement.ThenBody;
                if (statement.ElseBody is { } elseBody)
                {
                    yield return elseBody;
                }

                break;
            case CaseExpressionStatementNode statement:
                yield return statement.Block;
                break;
            case CaseElseClauseStatementNode statement:
                yield return statement.Body;
                break;
            case WhileWendStatementNode statement:
                yield return statement.Body;
                break;
            case DoLoopStatementNode statement:
                yield return statement.Body;
                break;
            case DoWhileLoopStatementNode statement:
                yield return statement.Body;
                break;
            case DoUntilLoopStatementNode statement:
                yield return statement.Body;
                break;
            case DoLoopWhileStatementNode statement:
                yield return statement.Body;
                break;
            case DoLoopUntilStatementNode statement:
                yield return statement.Body;
                break;
            case ForStatementNode statement:
                yield return statement.Body;
                break;
            case ForEachStatementNode statement:
                yield return statement.Body;
                break;
            case WithStatementNode statement:
                yield return statement.Body;
                break;
        }
    }
}
