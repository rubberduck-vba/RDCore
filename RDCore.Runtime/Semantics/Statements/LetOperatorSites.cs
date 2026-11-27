using System.Runtime.CompilerServices;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Operators;

namespace RDCore.Runtime.Semantics.Statements;

// the let-assignment operator a statement or a loop control assigns with, made the first time it runs and kept with its node: a node does not change,
// so a statement given the same operands assigns with the same operator, in a loop and in a call it makes to itself alike.
internal static class LetOperatorSites
{
    private static readonly ConditionalWeakTable<SyntaxNode, VBBinaryOperatorExpressionNode> Kept = [];

    public static VBBinaryOperatorExpressionNode Of(
        SyntaxNode owner, SyntaxNodeId identity, SourceLocation location, ExpressionNode target, ExpressionNode source)
    {
        if (Kept.TryGetValue(owner, out var kept) && ReferenceEquals(kept.Left, target) && ReferenceEquals(kept.Right, source))
        {
            return kept;
        }

        var made = new VBBinaryOperatorExpressionNode(OperatorSymbolNames.BinaryAssignmentValueOp, identity, location, target, source);
        Kept.AddOrUpdate(owner, made);
        return made;
    }
}
