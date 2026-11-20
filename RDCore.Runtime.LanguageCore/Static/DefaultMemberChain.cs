using System.Collections.Immutable;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.13, §5.6.14: an argument list given to an object of a class, or the name of a dictionary access, goes to the class's default member and,
// while that member takes no parameters and returns an object, on to the default member of the class it returns. The members are listed in the order they
// are called, as each class's current composition has it (CurrentTypes), and the last one takes the arguments. They are not listed when an Object, a Variant
// or a type that did not resolve is on the way, when the arguments index an array a member returns, nor when a class comes back: its default member returns
// the class itself, or one already called.
public static class DefaultMemberChain
{
    // givenArguments: the access gives the last member arguments, or a name; an empty argument list is the call of the first one with none.
    public static ImmutableArray<DefaultMemberFact>? Of(ISymbolResolver resolver, VBType? declared, bool givenArguments)
    {
        var chain = ImmutableArray.CreateBuilder<DefaultMemberFact>();
        var called = new HashSet<string>(StringComparer.Ordinal);
        var type = declared;
        while (type is VBClassType declaredClass && CurrentTypes.Of(resolver, declaredClass) is { DefaultMember: { } member } classType
            && called.Add(classType.Symbol.Uri.AbsoluteUri))
        {
            chain.Add(MemberFacts.DefaultMemberOf(resolver, classType, member));
            if (!givenArguments || !ObjectValueStaticSemantics.ReturnsWhatArgumentsApplyTo(member))
            {
                return chain.ToImmutable();
            }

            type = CurrentTypes.Of(resolver, member.ResolvedType);
        }

        return null;
    }
}
