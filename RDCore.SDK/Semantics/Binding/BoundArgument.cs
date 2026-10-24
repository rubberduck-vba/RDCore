using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Plans;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Binding;

/// <summary>
/// How the argument of a parameter is passed (<strong>MS-VBAL §5.3.1.11</strong>), as far as the declarations and the argument as it is written decide it.
/// </summary>
public enum ArgumentPassing
{
    /// <summary>
    /// Let-coerced to the parameter's declared type, into a variable of the activation's own: a <c>ByVal</c> parameter, and a <c>ByRef</c> one whose argument
    /// is no variable - a literal, an operation, a call, an expression in parentheses.
    /// </summary>
    Value,

    /// <summary>
    /// A <c>ByRef</c> parameter whose argument may be a variable - a name, an element of an array, a member of an object, a field of a record, what is bound
    /// late: the parameter is a second name for it when it is a variable of the parameter's declared type, which is the call's to find out, and a copy of
    /// its value otherwise.
    /// </summary>
    Reference,

    /// <summary>
    /// A <c>ParamArray</c> parameter: what the trailing positional arguments are collected into (<see cref="BoundArgument.Elements"/>).
    /// </summary>
    ParamArray,

    /// <summary>
    /// An <c>Optional</c> parameter the call gives no argument: its declared default, or what an omitted argument of its type is.
    /// </summary>
    Omitted,
}

/// <summary>
/// The argument a call gives one parameter of the procedure it calls, bound (<strong>MS-VBAL §5.3.1.11</strong>): which of the arguments written at the call it
/// is, and how it is passed - decided once, from the declarations and the arguments as they are written, rather than each time the call is made.
/// </summary>
/// <remarks>
/// What only the call can decide stays the call's: whether a variable given to a <c>ByRef</c> parameter is of its declared type (an element type, a field of an
/// object only known at run time), what a <c>Variant</c> argument holds, an error the argument raises.
/// </remarks>
/// <param name="Parameter">The parameter.</param>
/// <param name="Passing">How the argument is passed.</param>
/// <param name="Expression">The argument as it is written - the value of a named argument; <see langword="null"/> for one that is <see cref="ArgumentPassing.Omitted"/>
/// or <see cref="ArgumentPassing.ParamArray"/>.</param>
/// <param name="Value">The argument, bound; <see langword="null"/> when there is no <paramref name="Expression"/>.</param>
/// <param name="Plan">How an argument passed by <see cref="ArgumentPassing.Value"/> is let-coerced to the parameter's declared type, made for the argument's declared
/// type - deferred when it is a <c>Variant</c>; <see langword="null"/> when the argument has no declared type, and for any other passing.</param>
/// <param name="Elements">The arguments a <see cref="ArgumentPassing.ParamArray"/> collects, bound; empty for any other passing.</param>
public sealed record class BoundArgument(
    VBParameterSymbol Parameter, ArgumentPassing Passing, ExpressionNode? Expression, BoundNode? Value, ConversionPlan? Plan, ImmutableArray<BoundNode> Elements)
{
    /// <summary>
    /// Whether the parameter is declared to be passed by reference: <c>ByRef</c>, said or not (<strong>MS-VBAL §5.3.1.5</strong>).
    /// </summary>
    public static bool IsByRef(VBParameterSymbol parameter) => parameter.ParameterKind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef;
}

/// <summary>
/// What goes wrong mapping the arguments of a call to the parameters of the procedure it calls (<strong>MS-VBAL §5.3.1.11</strong>).
/// </summary>
public enum ArgumentMappingError
{
    /// <summary>The arguments map to the parameters.</summary>
    None,

    /// <summary>A named argument names no parameter, the <c>ParamArray</c>, or a parameter that is already given an argument.</summary>
    NamedArgumentNotFound,

    /// <summary>More positional arguments than there are parameters.</summary>
    WrongNumberOfArguments,

    /// <summary>A positional argument left out where the parameter is not <c>Optional</c>.</summary>
    MissingRequiredPositional,

    /// <summary>A parameter that is not <c>Optional</c>, given no argument.</summary>
    ArgumentNotOptional,
}

/// <summary>
/// The arguments of a call mapped to the parameters of the procedure it calls (<see cref="ArgumentMapping.Map"/>).
/// </summary>
/// <param name="Mapped">The argument each parameter is given, by the parameter's position - the value of a named argument; <see langword="null"/> for a parameter
/// given none, or when <paramref name="Error"/> is not <see cref="ArgumentMappingError.None"/>.</param>
/// <param name="ParamArrayArguments">The positional arguments a trailing <c>ParamArray</c> parameter collects.</param>
/// <param name="Error">What went wrong, if anything did.</param>
/// <param name="ErrorAt">The argument that is wrong, when one is.</param>
public sealed record class ArgumentMap(ExpressionNode?[]? Mapped, ImmutableArray<ExpressionNode> ParamArrayArguments, ArgumentMappingError Error, ExpressionNode? ErrorAt);

/// <summary>
/// Maps the arguments of a call to the parameters of the procedure it calls, and binds how each is passed (<strong>MS-VBAL §5.3.1.11</strong>).
/// </summary>
public static class ArgumentMapping
{
    /// <summary>
    /// Maps <paramref name="arguments"/>, as they are written, to <paramref name="parameters"/>: a positional argument to the parameter at its position, a
    /// named one to the parameter of its name, and the trailing positional arguments to a <c>ParamArray</c> parameter, which is always satisfied.
    /// </summary>
    /// <param name="parameters">The parameters of the procedure - after its <c>Me</c>, for a member called on an object.</param>
    /// <param name="arguments">The arguments of the call.</param>
    public static ArgumentMap Map(ImmutableArray<VBParameterSymbol> parameters, ImmutableArray<ExpressionNode> arguments)
    {
        // `Area()` is a call that supplies nothing; the parser reads the empty parentheses as one omitted argument, which a procedure that takes none has no
        // parameter for.
        if (parameters.IsEmpty && arguments is [MissingArgumentNode])
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

    /// <summary>
    /// Binds how each parameter of a call is given its argument: one <see cref="BoundArgument"/> for each of <paramref name="parameters"/>, in order.
    /// </summary>
    /// <param name="parameters">The parameters of the procedure - after its <c>Me</c>, for a member called on an object.</param>
    /// <param name="arguments">The arguments of the call, as they are written.</param>
    /// <param name="nodeOf">The bound node of an argument; <see langword="null"/> for one that is not bound, which is then bound late.</param>
    /// <param name="declaredTypeOf">The declared type of an argument; <see langword="null"/> for one that has none, whose coercion is planned when it is made.</param>
    /// <returns>The bound arguments; <see langword="null"/> when the arguments do not map to the parameters - which the call reports when it is made.</returns>
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
