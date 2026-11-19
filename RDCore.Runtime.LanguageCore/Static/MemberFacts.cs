using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.3.1.7: the accessors of a property are the Property Get, Let and Set its module or its class declares of one name. MS-VBAL §5.6.2.3: the default
// member of a class is what a value of an expression of the class is read or assigned through.
internal static class MemberFacts
{
    // the accessors of the property a name or a member access is bound to, and the type its Property Let is given.
    public static ExpressionFact WithAccessors(StaticEvaluationContext context, IExpressionFactSink facts, ExpressionNode expression, ExpressionFact fact, Symbol? bound)
    {
        if (bound is not (VBTypeMemberSymbol property and IVBPropertyMemberSymbol))
        {
            return fact;
        }

        var accessors = AccessorsOf(context, facts, expression, property);
        return fact with
        {
            Accessors = Of(accessors),
            LetValueType = accessors.OfType<VBPropertyLetMemberSymbol>().FirstOrDefault() is { Parameters: [.., var value] } ? value.ResolvedType : null,
        };
    }

    // the default member of the class an expression is of, as the class's current composition has it (CurrentTypes).
    public static ExpressionFact WithDefaultMember(StaticEvaluationContext context, ExpressionFact fact)
    {
        if (fact.DeclaredType is not VBClassType declaredClass || CurrentTypes.Of(context.Resolver, declaredClass) is not { DefaultMember: { } member } classType)
        {
            return fact;
        }

        var named = MembersOf(context, classType.Symbol, member.Name);
        return fact with
        {
            DefaultMember = new DefaultMemberFact(member.Name, ClassificationOf(member),
                member is IVBPropertyMemberSymbol ? Of(named) : PropertyAccessors.None, RequiredParametersOf(member)),
        };
    }

    // a member of an object is its class's, by its name; a name is what its module declares of it.
    private static Symbol[] AccessorsOf(StaticEvaluationContext context, IExpressionFactSink facts, ExpressionNode expression, VBTypeMemberSymbol property)
    {
        var ownerType = expression switch
        {
            MemberAccessExpressionNode { Owner: { } owner } => facts.TryGet(owner.Identity, out var ownerFact) ? ownerFact.DeclaredType : null,
            MemberAccessExpressionNode => context.EnclosingWithTargetType,
            _ => null,
        };

        return ownerType is VBClassType { Symbol: { } owned }
            ? MembersOf(context, owned, property.Name)
            : [.. context.Scope.SelfAndAncestors().SelectMany(scope => scope.DeclaredAs(property.Name))
                .Where(member => member.ParentUri.AbsoluteUri == property.ParentUri.AbsoluteUri)];
    }

    private static Symbol[] MembersOf(StaticEvaluationContext context, VBClassModuleSymbol snapshot, string name)
        => [.. (VBProjectSymbol.ResolveClass(context.Resolver, snapshot) ?? snapshot).DefaultInterfaceMembers
            .Where(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))];

    private static PropertyAccessors Of(IEnumerable<Symbol> accessors) => accessors.Aggregate(PropertyAccessors.None, (found, accessor) => found | accessor switch
    {
        VBPropertyGetMemberSymbol => PropertyAccessors.Get,
        VBPropertyLetMemberSymbol => PropertyAccessors.Let,
        VBPropertySetMemberSymbol => PropertyAccessors.Set,
        _ => PropertyAccessors.None,
    });

    private static ExpressionClassification ClassificationOf(Symbol member) => member switch
    {
        IVBPropertyMemberSymbol => ExpressionClassification.Property,
        VBFunctionMemberSymbol or VBExternalFunctionMemberSymbol => ExpressionClassification.Function,
        VBProcedureMemberSymbol or VBExternalSubMemberSymbol => ExpressionClassification.Subroutine,
        _ => ExpressionClassification.Variable,
    };

    // the parameters a call must be given, leaving out Me, the implicit first parameter of a member of a class.
    private static int RequiredParametersOf(Symbol member) => (member switch
    {
        VBReturningMemberSymbol returning => returning.Parameters,
        VBProcedureMemberSymbol procedure => procedure.Parameters,
        _ => [],
    }).Count(parameter => !(parameter.Name == Tokens.Me && parameter.ParameterKind == ParameterKind.ImplicitByRef)
        && !parameter.IsOptional && parameter is not ParamArrayParameterSymbol);
}
