using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Plans;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Binding;

// MS-VBAL §5.3.1.11
public enum ArgumentPassing
{
    Value,

    Reference,

    ParamArray,

    Omitted,
}

// MS-VBAL §5.3.1.11
public sealed record class BoundArgument(
    VBParameterSymbol Parameter, ArgumentPassing Passing, ExpressionNode? Expression, BoundNode? Value, ConversionPlan? Plan, ImmutableArray<BoundNode> Elements)
{
    // MS-VBAL §5.3.1.5
    public static bool IsByRef(VBParameterSymbol parameter) => parameter.ParameterKind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef;
}

// MS-VBAL §5.3.1.11
public enum ArgumentMappingError
{
    None,

    NamedArgumentNotFound,

    WrongNumberOfArguments,

    MissingRequiredPositional,

    ArgumentNotOptional,
}

public sealed record class ArgumentMap(ExpressionNode?[]? Mapped, ImmutableArray<ExpressionNode> ParamArrayArguments, ArgumentMappingError Error, ExpressionNode? ErrorAt);

// MS-VBAL §5.3.1.11
public static class ArgumentMapping
{
    public static ArgumentMap Map(ImmutableArray<VBParameterSymbol> parameters, ImmutableArray<ExpressionNode> arguments)
    {
        // `Area()` is a call that supplies nothing; the parser reads the empty parentheses as one omitted argument, which a procedure that takes none has no
        // parameter for, and a ParamArray that comes first does not collect: `Total()` passes it no element, as MS-VBA has it (oracle\ismissing I05).
        if (arguments is [MissingArgumentNode] && (parameters.IsEmpty || parameters[0] is ParamArrayParameterSymbol))
        {
            arguments = [];
        }

        var paramArrayIndex = parameters.Length > 0 && parameters[^1] is ParamArrayParameterSymbol ? parameters.Length - 1 : -1;
        var mapped = new ExpressionNode?[parameters.Length];
        var paramArrayArguments = ImmutableArray.CreateBuilder<ExpressionNode>();
        var positionalIndex = 0;

        foreach (var argument in arguments)
        {
            if (argument is NamedArgumentNode named)
            {
                var index = IndexOfParameter(parameters, named.Name);
                if (index < 0 || index == paramArrayIndex || mapped[index] is not null)
                {
                    return Failed(ArgumentMappingError.NamedArgumentNotFound, argument);
                }

                mapped[index] = named.Value;
                continue;
            }

            if (paramArrayIndex >= 0 && positionalIndex >= paramArrayIndex)
            {
                paramArrayArguments.Add(argument);
                positionalIndex++;
                continue;
            }

            if (positionalIndex >= parameters.Length)
            {
                return Failed(ArgumentMappingError.WrongNumberOfArguments, argument);
            }

            if (argument is MissingArgumentNode && !parameters[positionalIndex].IsOptional)
            {
                return Failed(ArgumentMappingError.MissingRequiredPositional, argument);
            }

            mapped[positionalIndex] = argument;
            positionalIndex++;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            // a ParamArray is always satisfied, even with nothing collected: an empty array, not an argument that is not optional.
            if (i != paramArrayIndex && mapped[i] is null && !parameters[i].IsOptional)
            {
                return Failed(ArgumentMappingError.ArgumentNotOptional, null);
            }
        }

        return new ArgumentMap(mapped, paramArrayArguments.ToImmutable(), ArgumentMappingError.None, null);
    }

    public static ImmutableArray<BoundArgument>? Bind(
        ImmutableArray<VBParameterSymbol> parameters, ImmutableArray<ExpressionNode> arguments, Func<ExpressionNode, BoundNode?> nodeOf, Func<ExpressionNode, VBType?> declaredTypeOf)
    {
        if (parameters.IsDefault || arguments.IsDefault || Map(parameters, arguments) is not { Error: ArgumentMappingError.None, Mapped: { } mapped } map)
        {
            return null;
        }

        BoundNode NodeOf(ExpressionNode argument) => nodeOf(argument) ?? new BoundLate(argument);

        var bound = ImmutableArray.CreateBuilder<BoundArgument>(parameters.Length);
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter is ParamArrayParameterSymbol)
            {
                bound.Add(new BoundArgument(parameter, ArgumentPassing.ParamArray, null, null, null, [.. map.ParamArrayArguments.Select(NodeOf)]));
                continue;
            }

            if (mapped[i] is not { } argument || argument is MissingArgumentNode)
            {
                bound.Add(new BoundArgument(parameter, ArgumentPassing.Omitted, null, null, null, []));
                continue;
            }

            var value = NodeOf(argument);
            if (BoundArgument.IsByRef(parameter) && !IsValue(argument, value))
            {
                bound.Add(new BoundArgument(parameter, ArgumentPassing.Reference, argument, value, null, []));
                continue;
            }

            var plan = declaredTypeOf(argument) is { } argumentType && parameter.ResolvedType is { } parameterType and not VBUnknownType
                ? ConversionPlanner.ForDeclared(argumentType, parameterType)
                : null;
            bound.Add(new BoundArgument(parameter, ArgumentPassing.Value, argument, value, plan, []));
        }

        return bound.MoveToImmutable();
    }

    // what is no variable however it is evaluated (MS-VBAL §5.3.1.11's argument not "classified as a variable"): a literal, an operation - a pair of
    // parentheses among them - a call of a procedure, a test, a new object, a bound of an array, and an argument written ByVal. Anything else may be a
    // variable - a name of one, a function's name in its own code, an element, a member, a field, what is bound late - which is the call's to find out.
    private static bool IsValue(ExpressionNode argument, BoundNode value)
        => argument is ByValArgumentExpressionNode
            || value is BoundLiteral or BoundBinary or BoundUnary or BoundCall or BoundTypeOf or BoundNew or BoundArrayBound;

    private static ArgumentMap Failed(ArgumentMappingError error, ExpressionNode? at) => new(null, [], error, at);

    private static int IndexOfParameter(ImmutableArray<VBParameterSymbol> parameters, string name)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
