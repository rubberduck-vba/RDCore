using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Whether two parameter lists are compatible, the way the declarations that must match one another need
/// (<strong>MS-VBAL §5.3.1.8</strong> for an event handler and its event, <strong>§5.3.1.9</strong> for an implemented
/// name declaration and its interface member).
/// </summary>
/// <remarks>
/// Both want the same number of parameters, each of the same declared type and the same parameter mechanism, though not
/// necessarily the same name, nor with the mechanism written out in the same way. An implemented name declaration also
/// wants the same optional parameters, the same <c>ParamArray</c>, and the same constant default of each optional
/// parameter, which an event's parameters, being only a description of arguments, do not have.
/// </remarks>
internal static class ParameterLists
{
    /// <summary>
    /// The first way <paramref name="candidate"/> is not compatible with <paramref name="required"/>.
    /// </summary>
    /// <param name="candidate">The parameter list of the declaration that has to match.</param>
    /// <param name="required">The parameter list it has to match, with the implicit <c>Me</c> of a member of a class left out.</param>
    /// <param name="optionalsAndDefaults">Whether <c>Optional</c>, <c>ParamArray</c> and a constant default must match as well.</param>
    /// <returns>What is different, or <see langword="null"/> when the lists are compatible.</returns>
    public static string? Incompatibility(
        ImmutableArray<VBParameterSymbol> candidate, ImmutableArray<VBParameterSymbol> required, bool optionalsAndDefaults = false)
    {
        candidate = WithoutMe(candidate);
        required = WithoutMe(required);

        if (candidate.Length != required.Length)
        {
            return $"it has {candidate.Length} parameter(s) and {required.Length} are required";
        }

        for (var i = 0; i < required.Length; i++)
        {
            if (!SameType(candidate[i].ResolvedType, required[i].ResolvedType))
            {
                return $"parameter {i + 1} is a {candidate[i].ResolvedType.Name} and a {required[i].ResolvedType.Name} is required";
            }

            if (IsByRef(candidate[i].ParameterKind) != IsByRef(required[i].ParameterKind))
            {
                return $"parameter {i + 1} is passed {Mechanism(candidate[i])} and {Mechanism(required[i])} is required";
            }

            if (optionalsAndDefaults && DifferenceInOptionality(candidate[i], required[i]) is { } difference)
            {
                return $"parameter {i + 1} {difference}";
            }
        }

        return null;
    }

    /// <summary>
    /// The parameters a procedure, function or property accessor declares, as they were written: the implicit <c>Me</c>
    /// of a member of a class module is among them, when it has been built with one.
    /// </summary>
    /// <param name="member">The member.</param>
    public static ImmutableArray<VBParameterSymbol> Of(VBTypeMemberSymbol member) => member switch
    {
        VBProcedureMemberSymbol procedure => procedure.Parameters,
        VBFunctionMemberSymbol function => function.Parameters,
        _ => [],
    };

    /// <summary>
    /// Whether two declared types are the same one. A type that is not known is not a type that differs.
    /// </summary>
    public static bool SameType(VBType left, VBType right)
        => left is VBUnknownType || right is VBUnknownType || string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

    // the implicit Me every member of a class module has at parameter 0 is not a parameter of its parameter list.
    private static ImmutableArray<VBParameterSymbol> WithoutMe(ImmutableArray<VBParameterSymbol> parameters)
        => parameters is [{ Name: "Me" }, ..] ? parameters.RemoveAt(0) : parameters;

    private static bool IsByRef(ParameterKind kind) => kind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef;

    private static string Mechanism(VBParameterSymbol parameter) => IsByRef(parameter.ParameterKind) ? "ByRef" : "ByVal";

    private static string? DifferenceInOptionality(VBParameterSymbol candidate, VBParameterSymbol required)
    {
        if (candidate is ParamArrayParameterSymbol != required is ParamArrayParameterSymbol)
        {
            return required is ParamArrayParameterSymbol ? "is not a ParamArray" : "is a ParamArray and is not required to be";
        }

        if (candidate.IsOptional != required.IsOptional)
        {
            return required.IsOptional ? "is not Optional" : "is Optional and is not required to be";
        }

        // 🚧 TODO only a default that is a literal is compared: a constant expression is not reduced here.
        return candidate.DefaultValue is LiteralExpressionNode left && required.DefaultValue is LiteralExpressionNode right
            && !Equals(left.StaticValue, right.StaticValue)
                ? "has a different default value"
                : null;
    }
}
