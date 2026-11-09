using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.3.1.8, §5.3.1.9
internal static class ParameterLists
{
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

    public static ImmutableArray<VBParameterSymbol> Of(VBTypeMemberSymbol member) => member switch
    {
        VBProcedureMemberSymbol procedure => procedure.Parameters,
        VBFunctionMemberSymbol function => function.Parameters,
        _ => [],
    };

    public static bool SameType(VBType left, VBType right)
        => left is VBUnknownType || right is VBUnknownType
            || string.Equals(VBEnumType.AsDeclaredType(left).Name, VBEnumType.AsDeclaredType(right).Name, StringComparison.OrdinalIgnoreCase);

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
