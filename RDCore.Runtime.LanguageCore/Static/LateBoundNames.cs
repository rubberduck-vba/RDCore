using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.12: a member access whose owner is of no particular class (an Object, a Variant) is bound where the code runs, to the member of the object's
// class with the name the access gives: any class's member of that name can be what it calls. MS-VBAL §6.1.2.8.1.1: CallByName calls the member a string
// names, which the code need not write at all. Members is each name that a module's code accesses late; CallsByName, whether it calls CallByName.
public sealed record class LateBoundNames(IReadOnlySet<string> Members, bool CallsByName)
{
    private const string CallByName = "CallByName";

    public static LateBoundNames Of(SyntaxNode module, IEnumerable<ProcedureSemanticModel> procedures)
    {
        var models = procedures.ToList();
        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var callsByName = false;
        foreach (var (node, _) in NameOccurrences.Walk(module))
        {
            switch (node)
            {
                case MemberAccessExpressionNode access when models.Any(model => model.Expressions.TryGetValue(access.Identity, out var fact)
                    && fact.Flags.HasFlag(ValueExpressionSemanticFlags.LateBound)):
                    members.Add(access.Member.IdentifierName.Trim('[', ']'));
                    break;
                // a procedure of the project named CallByName hides VBA's, and is taken for it: what it does with a string is not known either.
                case SimpleNameExpressionNode name when string.Equals(name.IdentifierName, CallByName, StringComparison.OrdinalIgnoreCase):
                    callsByName = true;
                    break;
            }
        }

        return new LateBoundNames(members, callsByName);
    }
}
