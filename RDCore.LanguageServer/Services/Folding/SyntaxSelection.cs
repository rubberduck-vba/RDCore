using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;

namespace RDCore.LanguageServer.Services.Folding;

public static class SyntaxSelection
{
    public static IReadOnlyList<SourceRange> RangesAt(ModuleNode module, SourcePosition position)
    {
        if (module.Children.IsDefaultOrEmpty)
        {
            return [];
        }

        // a module's own node has no range: it is what its first and last declarations are written between - the last to end, which a declaration a branch of
        // a #If block writes within a procedure whose header the block splits is not (MemberDeclarationNode.HeaderAlternatives).
        var whole = new SourceRange(module.Children[0].SourceLocation.Range.Start, module.Children.Max(child => child.SourceLocation.Range.End));
        if (!Contains(whole, position))
        {
            return [];
        }

        var outward = new List<SourceRange> { whole };
        SyntaxNode node = module;
        while (InnermostAt(node, position) is { } inner)
        {
            var range = inner.SourceLocation.Range;
            if (!range.IsEmpty && range != outward[^1] && Contains(outward[^1], range))
            {
                outward.Add(range);
            }

            node = inner;
        }

        outward.Reverse();
        return outward;
    }

    // of the children that contain the position, the smallest: one within another wins, and of two that touch at the position the first does.
    private static SyntaxNode? InnermostAt(SyntaxNode node, SourcePosition position)
    {
        SyntaxNode? inner = null;
        foreach (var child in node.ChildNodes())
        {
            var range = child.SourceLocation.Range;
            if (Contains(range, position) && (inner is null || range != inner.SourceLocation.Range && Contains(inner.SourceLocation.Range, range)))
            {
                inner = child;
            }
        }

        return inner;
    }

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;

    private static bool Contains(SourceRange outer, SourceRange inner) => outer.Start <= inner.Start && inner.End <= outer.End;
}
