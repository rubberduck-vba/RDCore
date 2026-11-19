using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §3.4: a branch of a #If that is excluded is no part of the program in this configuration, and is written to be compiled in another one. The parser reads
// every branch, so the syntax tree has the names its code uses. A declaration that such a branch can refer to has a count that this configuration cannot state:
// the branch can be its only use.
public static class ExcludedBranchNames
{
    public static IReadOnlySet<string> Of(SyntaxNode module, IReadOnlyCollection<SourceRange> deadRanges)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (deadRanges.Count == 0)
        {
            return names;
        }

        // the member of an access is a simple name too, so `a.b` gives both.
        foreach (var (node, _) in NameOccurrences.Walk(module))
        {
            if (node is SimpleNameExpressionNode name && deadRanges.Any(dead => dead.Start <= name.Location.Range.Start && name.Location.Range.End <= dead.End))
            {
                names.Add(name.IdentifierName);
            }
        }

        return names;
    }
}
