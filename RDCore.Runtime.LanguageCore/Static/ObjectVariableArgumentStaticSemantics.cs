using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Static;

// RD-VBAL §5.3.1.11; MS-VBAL §5.6.12, §5.3.1.11
// MS-VBA implements a public variable of a class as a property: the caller passes what its Get returns, and what the callee assigns is lost.
public static class ObjectVariableArgumentStaticSemantics
{
    // whether an argument is a copy of what it names: a public variable of an object, or an element of one, given to a procedure of the project for a
    // parameter it does not take ByVal, or to a member called late. What a ParamArray collects is passed by reference, as a ByRef parameter's argument is
    // (MS-VBA, recorded in Excel).
    public static bool IsPassedAsACopy(StaticEvaluationContext context, ExpressionNode callee, int position, string? name, ExpressionNode passed)
    {
        if (!IsObjectVariableOrElement(context, passed))
        {
            return false;
        }

        if (ObjectValueStaticSemantics.MemberOfCallee(context, callee) is { } member)
        {
            return member.GetProperty(SymbolProperties.ExternalTarget) is null
                && ObjectValueStaticSemantics.ParametersOfCallee(context, callee) is { } declared
                && ParameterOf([.. declared.Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef))], position, name)
                    is { ParameterKind: not ParameterKind.ExplicitByVal };
        }

        return IsCalledLate(context, callee);
    }

    // b.Count, or b.V(0): an element of the array a Variant variable holds, whose variable is the copy that is passed; a class has no public array variable.
    // Of a variable of any other type (b.Items(1), Items a Collection) it is the value of the default member of what the variable holds, which MS-VBAL passes
    // as a copy too.
    private static bool IsObjectVariableOrElement(StaticEvaluationContext context, ExpressionNode passed) => passed switch
    {
        MemberAccessExpressionNode access => ObjectVariableOf(context, access) is not null,
        IndexExpressionNode { Callee: MemberAccessExpressionNode access, Arguments.IsEmpty: false }
            => ObjectVariableOf(context, access) is ITypedSymbol { ResolvedType: VBVariantType },
        _ => false,
    };

    // a member of an object bound late (Object, Variant), or of the target of a With block that is one: whose parameters are not known until it runs.
    private static bool IsCalledLate(StaticEvaluationContext context, ExpressionNode callee) => callee switch
    {
        MemberAccessExpressionNode { Owner: null } => context.EnclosingWithTargetType is VBObjectType or VBVariantType,
        MemberAccessExpressionNode { Owner: { } owner } when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is null
            => ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = null }, owner) is { IsSuccess: true, Result: VBObjectType or VBVariantType },
        _ => false,
    };

    // a public variable of a class, named as a member of an object of it, or of the target of the enclosing With block (MS-VBAL §5.6.15) - never a member of a
    // module the owner names, which is the module's variable.
    private static Symbol? ObjectVariableOf(StaticEvaluationContext context, MemberAccessExpressionNode access)
    {
        var classType = access.Owner switch
        {
            null => context.EnclosingWithTargetType as VBClassType,
            { } owner when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is null
                => ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = null }, owner) is { IsSuccess: true, Result: VBClassType owned }
                    ? owned
                    : null,
            _ => null,
        };

        // a class's own variables are among its interface's members as the variables of its module they are declared as.
        return classType is null ? null : (VBProjectSymbol.ResolveClass(context.Resolver, classType.Symbol) ?? classType.Symbol).DefaultInterfaceMembers
            .FirstOrDefault(candidate => candidate is VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol
                && string.Equals(candidate.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase));
    }

    // the parameter an argument is for: the one named, or the one at its position - a ParamArray's for every position from its own on.
    internal static VBParameterSymbol? ParameterOf(ImmutableArray<VBParameterSymbol> parameters, int index, string? name)
    {
        if (name is not null)
        {
            return parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        for (var position = 0; position < parameters.Length; position++)
        {
            if (parameters[position] is ParamArrayParameterSymbol || position == index)
            {
                return parameters[position];
            }
        }

        return null;
    }
}
