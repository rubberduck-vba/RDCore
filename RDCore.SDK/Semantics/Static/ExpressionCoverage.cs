using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Whether the static pass evaluated every expression a body refers to a name in.
/// </summary>
/// <remarks>
/// A fact about the references to a declaration is only true when every reference is among the facts: an expression the pass did not evaluate - an
/// operand that follows one that is an error, the bounds of an array that is declared, a statement the pass does not look into - may refer to anything.
/// This looks at the body itself, apart from what the pass did with it, for each place a name is written that is a reference (<see cref="SimpleNameExpressionNode"/>,
/// <see cref="MemberAccessExpressionNode"/>, <see cref="DictionaryAccessExpressionNode"/> and <see cref="IndexExpressionNode"/>), and asks the pass for its fact:
/// a body is covered when each has one.
/// <para>
/// What is not a reference is not looked for: the name of a member is the access that has it, the type a <c>New</c> expression names is a type and no value, a
/// label a jump names is no symbol, nor is the name of a named argument, and the first operand of a <c>RaiseEvent</c> is an event. A statement or an expression
/// in an excluded <c>#If</c> branch is not there (<strong>MS-VBAL §3.4.2</strong>).
/// </para>
/// </remarks>
internal static class ExpressionCoverage
{
    /// <summary>
    /// Whether every reference written in <paramref name="body"/> has a fact in <paramref name="facts"/>.
    /// </summary>
    /// <param name="body">The procedure's body.</param>
    /// <param name="options">What decides which of the body's statements there are.</param>
    /// <param name="facts">What the pass recorded of the expressions it evaluated.</param>
    public static bool IsCovered(StatementBlock body, StaticSemanticsOptions options, IExpressionFactSink facts)
    {
        var covered = true;
        VisitBlock(body, options, facts, ref covered);
        return covered;
    }

    private static void VisitBlock(StatementBlock block, StaticSemanticsOptions options, IExpressionFactSink facts, ref bool covered)
    {
        foreach (var child in block.Children)
        {
            if (!options.IsDead(child.SourceLocation.Range))
            {
                Visit(child, options, facts, ref covered);
            }
        }
    }

    private static void Visit(SyntaxNode node, StaticSemanticsOptions options, IExpressionFactSink facts, ref bool covered)
    {
        if (!covered || options.IsDead(node.SourceLocation.Range))
        {
            return;
        }

        switch (node)
        {
            // a label is no symbol, and what a jump names is a label.
            case GoToStatementNode or GoSubStatementNode or OnErrorGoToStatementNode or ResumeStatementNode:
                return;
            case OnGoToStatementNode onGoTo:
                Visit(onGoTo.Selector, options, facts, ref covered);
                return;
            case OnGoSubStatementNode onGoSub:
                Visit(onGoSub.Selector, options, facts, ref covered);
                return;
            // the first operand of RaiseEvent names an event.
            case KeywordStatementNode { Token: Tokens.RaiseEvent } raiseEvent:
                foreach (var operand in raiseEvent.Inputs.Skip(1))
                {
                    Visit(operand, options, facts, ref covered);
                }

                return;
            // the bounds of an array are expressions that are not among its children.
            case RDCore.SDK.Model.AST.Declarations.ArrayBoundsNode arrayBounds:
                foreach (var bound in arrayBounds.Bounds.IsDefault ? [] : arrayBounds.Bounds)
                {
                    if (bound.LowerExpression is { } lower)
                    {
                        Visit(lower, options, facts, ref covered);
                    }

                    if (bound.UpperExpression is { } upper)
                    {
                        Visit(upper, options, facts, ref covered);
                    }
                }

                return;
            // a type is no value, and a name written in a declaration's As clause is a type.
            case AsTypeExpressionNode or NewExpressionNode:
                return;
            case TypeOfIsExpressionNode typeOfIs:
                Visit(typeOfIs.Operand, options, facts, ref covered);
                return;
            case SimpleNameExpressionNode name:
                covered &= facts.TryGet(name.Identity, out _);
                return;
            // the member is named by the access, which is what has the fact.
            case MemberAccessExpressionNode { Owner: var owner } access:
                covered &= facts.TryGet(access.Identity, out _);
                if (owner is not null)
                {
                    Visit(owner, options, facts, ref covered);
                }

                return;
            case DictionaryAccessExpressionNode { Owner: var owner } dictionary:
                covered &= facts.TryGet(dictionary.Identity, out _);
                if (owner is not null)
                {
                    Visit(owner, options, facts, ref covered);
                }

                return;
            case IndexExpressionNode index:
                covered &= facts.TryGet(index.Identity, out _);
                break;
        }

        foreach (var child in node.Children)
        {
            Visit(child, options, facts, ref covered);
        }

        // the bodies a statement has are not among its children.
        if (node is StatementNode statement)
        {
            foreach (var nested in NestedBlocksOf(statement))
            {
                VisitBlock(nested, options, facts, ref covered);
            }
        }
    }

    // the blocks of statements a statement holds, as the walk of the pass reaches them.
    private static IEnumerable<StatementBlock> NestedBlocksOf(StatementNode statement)
    {
        switch (statement)
        {
            case IfBlockStatementNode ifBlock:
                yield return ifBlock.Body;
                foreach (var elseIf in ifBlock.ElseIfBlocks)
                {
                    yield return elseIf.Body;
                }

                if (ifBlock.ElseBlock is { } elseBlock)
                {
                    yield return elseBlock.Body;
                }

                break;
            case InlineIfStatementNode inlineIf:
                yield return inlineIf.ThenBody;
                if (inlineIf.ElseBody is { } elseBody)
                {
                    yield return elseBody;
                }

                break;
            case DoLoopStatementNode doLoop:
                yield return doLoop.Body;
                break;
            case DoLoopUntilStatementNode doLoopUntil:
                yield return doLoopUntil.Body;
                break;
            case DoLoopWhileStatementNode doLoopWhile:
                yield return doLoopWhile.Body;
                break;
            case DoUntilLoopStatementNode doUntilLoop:
                yield return doUntilLoop.Body;
                break;
            case DoWhileLoopStatementNode doWhileLoop:
                yield return doWhileLoop.Body;
                break;
            case WhileWendStatementNode whileWend:
                yield return whileWend.Body;
                break;
            case ForStatementNode forStatement:
                yield return forStatement.Body;
                break;
            case ForEachStatementNode forEach:
                yield return forEach.Body;
                break;
            case WithStatementNode with:
                yield return with.Body;
                break;
            case SelectCaseStatementNode selectCase:
                foreach (var caseBlock in selectCase.CaseExpressionBlocks)
                {
                    yield return caseBlock.Block;
                }

                if (selectCase.CaseElseBlock is { } caseElse)
                {
                    yield return caseElse.Body;
                }

                break;
        }
    }
}
