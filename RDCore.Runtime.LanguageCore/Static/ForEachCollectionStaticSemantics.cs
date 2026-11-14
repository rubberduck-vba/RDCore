using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.4.2.4 leaves the enumeration of a collection implementation-defined. A Set to Nothing of the variable a For Each walks, inside that loop
// (`Set C = Nothing` in `For Each V In C`), leaves the variable as it is while the loop runs: the loop holds the object, which an iteration does not
// destroy. MS-VBA lets go of a Collection there, which is destroyed with the members the loop has not reached, and the loop ends (observed in Excel).
public static class ForEachCollectionStaticSemantics
{
    // the Set statements of a loop, in its body or a block of it, that set the variable it walks to Nothing.
    public static IEnumerable<AssignmentStatementNode> SetsToNothingOfItsCollection(ForEachStatementNode loop)
        => loop.CollectionExpression is SimpleNameExpressionNode walked
            ? loop.Descendants().OfType<AssignmentStatementNode>().Where(set => IsSetToNothingOf(set, walked))
            : [];

    // the openers of the loops the statement at offset is in whose variable it sets to Nothing, the innermost first.
    public static IEnumerable<int> LoopsWhoseCollectionItSetsToNothing(InstructionList body, int offset)
    {
        if (body.Items[offset].Node is not AssignmentStatementNode { Kind: AssignmentKind.Set, Target: SimpleNameExpressionNode, Value: var value } set
            || !IsNothing(value))
        {
            yield break;
        }

        for (var at = offset - 1; at >= 0; at--)
        {
            if (body.Items[at] is { Kind: InstructionKind.ForEachOpener, End: { } end, Node: ForEachStatementNode { CollectionExpression: SimpleNameExpressionNode walked } }
                && offset < end && IsSetToNothingOf(set, walked))
            {
                yield return at;
            }
        }
    }

    private static bool IsSetToNothingOf(AssignmentStatementNode set, SimpleNameExpressionNode walked)
        => set is { Kind: AssignmentKind.Set, Target: SimpleNameExpressionNode target } && IsNothing(set.Value)
            && string.Equals(target.IdentifierName, walked.IdentifierName, StringComparison.OrdinalIgnoreCase);

    private static bool IsNothing(ExpressionNode expression) => expression is LiteralExpressionNode { StaticValue: VBNothingValue };
}
