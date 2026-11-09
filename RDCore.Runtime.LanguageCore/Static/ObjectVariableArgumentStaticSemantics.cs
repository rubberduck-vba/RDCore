using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Flags;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static;

// RD-VBAL §5.3.1.11; MS-VBAL §5.6.12, §5.3.1.11
public static class ObjectVariableArgumentStaticSemantics
{
    public static void MarkPassedAsCopies(StaticEvaluationContext context, ExpressionNode callee, ImmutableArray<ExpressionNode> arguments)
    {
        if (context.Facts is null)
        {
            return;
        }

        if (ObjectValueStaticSemantics.MemberOfCallee(context, callee) is { } member)
        {
            if (member.GetProperty(SymbolProperties.ExternalTarget) is null && ObjectValueStaticSemantics.ParametersOfCallee(context, callee) is { } declared)
            {
                MarkPassedAsCopies(context, [.. declared.Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef))],
                    arguments);
            }

            return;
        }

        if (IsCalledLate(context, callee))
        {
            foreach (var argument in arguments)
            {
                Mark(context, argument is NamedArgumentNode named ? named.Value : argument, ValueExpressionSemanticFlags.ObjectVariablePassedLateAsACopy);
            }
        }
    }

    // MS-VBAL §5.3.1.11, §5.4.2.20; RD-VBAL §5.3.1.11
    public static void MarkPassedAsCopies(StaticEvaluationContext context, ImmutableArray<VBParameterSymbol> parameters, IReadOnlyList<ExpressionNode> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var (name, passed) = arguments[index] is NamedArgumentNode named ? (named.Name, named.Value) : ((string?)null, arguments[index]);
            // what a ParamArray collects is passed by reference, as a ByRef parameter's argument is (MS-VBA, recorded in Excel: oracle\s58 F01).
            if (ParameterOf(parameters, index, name) is { } parameter && parameter.ParameterKind is not ParameterKind.ExplicitByVal)
            {
                Mark(context, passed, ValueExpressionSemanticFlags.ObjectVariablePassedAsACopy);
            }
        }
    }

    private static void Mark(StaticEvaluationContext context, ExpressionNode passed, ValueExpressionSemanticFlags flag)
    {
        if (context.Facts is { } facts && IsObjectVariableOrElement(context, passed) && facts.TryGet(passed.Identity, out var fact))
        {
            facts.Record(fact with { Flags = fact.Flags | flag });
        }
    }

    // b.Count, or b.V(0): an element of the array a Variant variable holds, whose variable is the copy that is passed (s58 B01); a class has no public array
    // variable. Of a variable of any other type (b.Items(1), Items a Collection) it is the value of the default member of what the variable holds, which
    // MS-VBAL passes as a copy too.
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
