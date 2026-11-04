using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// <strong>RD-VBAL §5.3.1.11</strong>: a public variable of an object, named as a member of it, given to a <c>ByRef</c> parameter is passed as a copy, as
/// <strong>MS-VBA</strong> passes it (recorded in Excel), where <strong>MS-VBAL §5.6.12</strong> and <strong>§5.3.1.11</strong> pass it by reference.
/// </summary>
/// <remarks>
/// <para>
/// MS-VBA implements a public variable of a class as a property of the class's interface: what a caller is given of <c>b.Count</c> is the value its
/// <c>Get</c> returns, and what the callee assigns to the parameter is lost. Whichever way the member is named - through a variable of the class, through
/// <c>Me</c>, relative to a <c>With</c> block - the call is the same. Named by itself in its own class, <c>Count</c> is the variable, as a variable of a
/// standard module is however it is named (<c>Counter</c>, <c>Globals.Counter</c>).
/// </para>
/// <para>
/// An object bound late (<c>Object</c>, <c>Variant</c>) is given a copy too: what it has is not known until the program runs, and nothing is flagged.
/// </para>
/// </remarks>
public static class ObjectVariableArgumentStaticSemantics
{
    /// <summary>
    /// Flags each argument of a call that is a public variable of an object named as its member, for a <c>ByRef</c> parameter of a procedure of the workspace
    /// (<see cref="ValueExpressionSemanticFlags.ObjectVariablePassedAsACopy"/>): a fact of the argument, for an analyzer to say.
    /// </summary>
    /// <param name="context">The compile-time context the call is evaluated against; nothing is recorded without its facts.</param>
    /// <param name="callee">What the call calls.</param>
    /// <param name="arguments">The arguments, as written; they have been evaluated.</param>
    public static void MarkPassedAsCopies(StaticEvaluationContext context, ExpressionNode callee, ImmutableArray<ExpressionNode> arguments)
    {
        if (context.Facts is not { } facts
            || ObjectValueStaticSemantics.MemberOfCallee(context, callee) is not { } member
            || member is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol || member.GetProperty(SymbolProperties.ExternalTarget) is not null
            || ObjectValueStaticSemantics.ParametersOfCallee(context, callee) is not { } declared)
        {
            return;
        }

        var parameters = declared.Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef)).ToImmutableArray();
        for (var index = 0; index < arguments.Length; index++)
        {
            var (name, passed) = arguments[index] is NamedArgumentNode named ? (named.Name, named.Value) : ((string?)null, arguments[index]);
            if (passed is not MemberAccessExpressionNode access
                || ParameterOf(parameters, index, name) is not { } parameter
                || parameter is ParamArrayParameterSymbol || parameter.ParameterKind is ParameterKind.ExplicitByVal
                || !IsObjectVariable(context, access)
                || !facts.TryGet(access.Identity, out var fact))
            {
                continue;
            }

            facts.Record(fact with { Flags = fact.Flags | ValueExpressionSemanticFlags.ObjectVariablePassedAsACopy });
        }
    }

    // a public variable of a class, named as a member of an object of it, or of the target of the enclosing With block (MS-VBAL §5.6.15) - never a member of a
    // module the owner names, which is the module's variable.
    private static bool IsObjectVariable(StaticEvaluationContext context, MemberAccessExpressionNode access)
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
        return classType is not null && (VBProjectSymbol.ResolveClass(context.Resolver, classType.Symbol) ?? classType.Symbol).DefaultInterfaceMembers
            .Any(candidate => candidate is VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol
                && string.Equals(candidate.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase));
    }

    // the parameter an argument is for: the one named, or the one at its position - a ParamArray's for every position from its own on.
    private static VBParameterSymbol? ParameterOf(ImmutableArray<VBParameterSymbol> parameters, int index, string? name)
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
