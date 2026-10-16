using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The default member of an object, called the way something that wants the object's value calls it: with no arguments.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.5.1.2.13</strong>'s "simple data value" of an object is what its default member returns, when that member is
/// a property or function whose parameters are "compatible with an argument list containing 0 parameters". Let-coercion asks for it,
/// and so does each standard-library function that looks at the value of its argument rather than at the argument - <c>VarType</c>,
/// <c>IsNumeric</c> and the rest - so that what makes a member callable this way, and what it is called with, are said once.
/// </remarks>
internal static class ObjectDefaultMember
{
    /// <summary>
    /// Finds the default member of <paramref name="classModule"/> that can be called with no arguments.
    /// </summary>
    /// <param name="classModule">The class of the object.</param>
    /// <param name="member">The default member, when there is one that can be called with no arguments.</param>
    /// <returns>
    /// <see langword="false"/> when the class has no default member, or when it is not a property or function, or when it has a
    /// parameter that is neither optional nor a <c>ParamArray</c> - a <c>Collection</c>'s <c>Item</c> takes the key it reads.
    /// </returns>
    public static bool TryFind(VBClassModuleSymbol classModule, [NotNullWhen(true)] out VBReturningMemberSymbol? member)
    {
        member = VBClassType.FromClassModule(classModule).DefaultMember as VBReturningMemberSymbol;

        // SymbolBuilder.BuildParameters gives every member of a class an implicit Me at slot 0; anything after it is the member's own.
        var parameters = member is null ? [] : RuntimeProcedureInvoker.GetParameters(member);
        if (parameters.Length == 0 || parameters.Skip(1).Any(parameter => parameter is not ParamArrayParameterSymbol && !parameter.IsOptional))
        {
            member = null;
        }

        return member is not null;
    }

    /// <summary>
    /// The arguments the default member is called with: the object as its <c>Me</c>, and nothing for each of its own parameters.
    /// </summary>
    /// <remarks>
    /// An invoker takes an argument for every parameter, so "nothing" is spelled out: a <c>ParamArray</c> collects none - the same
    /// zero-element array a call that passes it nothing collects - and an optional parameter takes the value an omitted argument gives
    /// it, which can be an error: a default that overflows its parameter's type does, as it would on any call that omits it.
    /// </remarks>
    /// <param name="member">The default member, as <see cref="TryFind"/> found it.</param>
    /// <param name="receiver">The object.</param>
    /// <param name="omitted">The value an optional parameter takes when its argument is omitted, or the error giving it that value raised.</param>
    /// <param name="arguments">The arguments, one per parameter; empty when an omitted argument's value was an error.</param>
    /// <returns>The error an omitted argument's value raised, or <see langword="null"/> when every argument was filled in.</returns>
    public static RuntimeSemanticsEvaluationResult? ArgumentsOf(
        VBReturningMemberSymbol member, VBObjectValue receiver, Func<VBParameterSymbol, RuntimeSemanticsEvaluationResult> omitted, out IRuntimeValue[] arguments)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(member);
        var filled = new IRuntimeValue[parameters.Length];
        filled[0] = receiver.RuntimeValue;
        arguments = [];
        for (var index = 1; index < parameters.Length; index++)
        {
            if (parameters[index] is ParamArrayParameterSymbol)
            {
                filled[index] = new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(new VBFixedSizeArrayValue([(0, -1)])));
                continue;
            }

            var value = omitted(parameters[index]);
            if (!value.IsSuccess)
            {
                return value;
            }

            filled[index] = value.Result!.RuntimeValue;
        }

        arguments = filled;
        return null;
    }

    /// <summary>
    /// Calls the default member of <paramref name="value"/> with no arguments, through whatever the session binds members with.
    /// </summary>
    /// <param name="session">The session the object lives in.</param>
    /// <param name="value">The object. <c>Nothing</c> has no default member.</param>
    /// <returns>
    /// What the default member returned, or the error it raised; <see langword="null"/> when the object has no default member that can
    /// be called with no arguments, or the session cannot run one.
    /// </returns>
    public static RuntimeSemanticsEvaluationResult? Call(IRuntimeSession session, VBObjectValue value)
    {
        if (value.IsNothing()
            || session.Callables is not { } callables
            || !session.Symbols.TryGetInstance(value.Value, out var instance)
            || !TryFind(instance.ClassModule, out var member))
        {
            return null;
        }

        // what a variable starts as has no error channel: a default whose coercion fails stands in as its type's default here.
        _ = ArgumentsOf(member, value, parameter => RuntimeSemanticsEvaluationResult.Success(
            session.Symbols.Defaults?.DefaultValueOf(parameter) ?? RuntimeProcedureInvoker.OmittedArgumentValue(parameter)), out var arguments);

        // the arguments already carry Me at index 0, so the binding is asked for with no receiver of its own.
        return callables.ForMember(member).Call(session.Symbols.Resolver, arguments);
    }
}
