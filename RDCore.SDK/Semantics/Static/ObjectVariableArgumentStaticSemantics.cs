using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// <strong>RD-VBAL §5.3.1.11</strong>: a public variable of an object, named as a member of it, or an element of one, given to a <c>ByRef</c> parameter is
/// passed as a copy, as <strong>MS-VBA</strong> passes it (recorded in Excel), where <strong>MS-VBAL §5.6.12</strong> and <strong>§5.3.1.11</strong> pass it
/// by reference.
/// </summary>
/// <remarks>
/// <para>
/// MS-VBA implements a public variable of a class as a property of the class's interface: what a caller is given of <c>b.Count</c> is the value its
/// <c>Get</c> returns, and what the callee assigns to the parameter is lost. Whichever way the member is named - through a variable of the class, through
/// <c>Me</c>, relative to a <c>With</c> block - and whatever takes it - a procedure of the workspace, a <c>ParamArray</c>, a <c>Declare</c>d procedure, a
/// procedure called late, the handlers of a raised event - the call is the same, and so is an element of one, <c>b.V(0)</c> (oracle\s55 B, s58 B01, F01-F05,
/// s59 M04). Named by itself in its own class, <c>Count</c> is the variable, and <c>V(0)</c> its element, as a variable of a standard module is however it
/// is named (<c>Counter</c>, <c>Globals.Counter</c>).
/// </para>
/// <para>
/// A member of an object bound late (<c>u.Count</c>, <c>u</c> an <c>Object</c> or a <c>Variant</c>) is passed as a copy too, but whether it is a variable is
/// not known until the program runs, and it is not flagged. Nor is what a member of a referenced library is given, whose <c>ByRef</c> parameters are mostly
/// what the library only reads.
/// </para>
/// </remarks>
public static class ObjectVariableArgumentStaticSemantics
{
    /// <summary>
    /// Flags each argument of a call that is a public variable of an object named as its member, or an element of one: for a <c>ByRef</c> parameter of the
    /// procedure called (<see cref="ValueExpressionSemanticFlags.ObjectVariablePassedAsACopy"/>), or for any parameter of a procedure called late, which may
    /// take it by reference (<see cref="ValueExpressionSemanticFlags.ObjectVariablePassedLateAsACopy"/>): a fact of the argument, for an analyzer to say.
    /// </summary>
    /// <param name="context">The compile-time context the call is evaluated against; nothing is recorded without its facts.</param>
    /// <param name="callee">What the call calls.</param>
    /// <param name="arguments">The arguments, as written; they have been evaluated.</param>
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

    /// <summary>
    /// Flags each argument given to <paramref name="parameters"/> that is a public variable of an object named as its member, or an element of one, for a
    /// <c>ByRef</c> parameter or a <c>ParamArray</c> (<see cref="ValueExpressionSemanticFlags.ObjectVariablePassedAsACopy"/>): a call's, or a
    /// <c>RaiseEvent</c>'s, whose arguments are its event's.
    /// </summary>
    /// <remarks>
    /// <strong>MS-VBAL §5.3.1.11</strong> passes such an argument of a <c>ByRef</c> parameter by reference, and so does <strong>§5.4.2.20</strong> for the
    /// arguments of a <c>RaiseEvent</c>, which it processes as a call's; MS-VBA passes a copy (RD-VBAL §5.3.1.11).
    /// </remarks>
    /// <param name="context">The compile-time context the arguments are evaluated against; nothing is recorded without its facts.</param>
    /// <param name="parameters">The parameters the arguments are given to, in order.</param>
    /// <param name="arguments">The arguments, as written; they have been evaluated.</param>
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
