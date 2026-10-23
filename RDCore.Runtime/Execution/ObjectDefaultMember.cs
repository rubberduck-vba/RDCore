using RDCore.SDK.Model.Errors;
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

    // how many objects deep a default member that returns an object is followed, before it is taken for one that never stops returning one.
    private const int DeepestDefaultMember = 64;

    /// <summary>
    /// The value a standard-library function reads of its argument: the argument itself, unless it is an object, which stands for its
    /// <em>simple data value</em> (<strong>MS-VBAL §5.5.1.2.13</strong>).
    /// </summary>
    /// <remarks>
    /// A function that computes with the value of a <c>Variant</c> argument - <c>CInt</c>, <c>Abs</c>, <c>Round</c> - is given an object as it
    /// is, a <c>Variant</c> holding objects as well as values; the Let-coercion its declared <c>Variant</c> parameter does not do is this one.
    /// It is what MS-VBA does (recorded in Excel): <c>Nothing</c> is error 91, and an error the default member raises is the function's. A default
    /// member that returns an object stands for that object's simple data value in turn, as Let-coercing the object it returned would make it.
    /// <para>
    /// What has no value is the error <strong>MS-VBAL §5.6.2.2</strong> and <strong>§5.6.2.3</strong> say it is, and what MS-VBA raises for most of
    /// its functions: an object with no default member is error 438; one whose default member takes an argument (a <c>Collection</c>'s <c>Item</c>)
    /// is error 450, which the specification leaves unsaid; and an object that a default member returned, and that has no value, is error 13 - "the
    /// implementation ... can raise error 9 (Subscript out of range) or 13 (Type mismatch)". 👉 MS-VBA's functions do not all agree: its
    /// <c>Format</c> raises 438 for the last, and its <c>Abs</c> and <c>Int</c> raise 438 for a <c>Collection</c>.
    /// </para>
    /// </remarks>
    /// <param name="session">The session the object lives in.</param>
    /// <param name="argument">The argument, as the function was given it.</param>
    /// <returns>The value, never an object or a <c>Variant</c>; or the error reading it raised.</returns>
    public static RuntimeSemanticsEvaluationResult ValueOf(IRuntimeSession session, VBTypedValue argument)
    {
        var value = Unwrapped(argument);
        for (var depth = 0; value is VBObjectValue reference; depth++)
        {
            if (reference.IsNothing())
            {
                return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, default,
                    "Nothing has no value."));
            }

            if (depth == DeepestDefaultMember)
            {
                return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.OutOfStackSpace, default,
                    $"A default member returned an object whose default member returned an object, {DeepestDefaultMember} times."));
            }

            if (Call(session, reference) is not { } called)
            {
                return RuntimeSemanticsEvaluationResult.Error(NoValue(session, reference, returnedByADefaultMember: depth > 0));
            }

            if (!called.IsSuccess)
            {
                return called;
            }

            value = Unwrapped(called.Result!);
        }

        return RuntimeSemanticsEvaluationResult.Success(value);
    }

    /// <summary>
    /// What a pair of parentheses makes of the object it encloses (<strong>MS-VBAL §5.6.6</strong>, "the simple data value of its enclosed expression"):
    /// what its default member returns - read once, whether that is a value or an object.
    /// </summary>
    /// <remarks>
    /// One read for each pair of parentheses, as MS-VBA has it (recorded in Excel): <c>(o)</c> of an object whose default member returns a
    /// <c>Collection</c> is that <c>Collection</c> - <c>TypeName((o))</c>, <c>Set x = (o)</c>, <c>(o) Is Nothing</c> - and <c>((o))</c> is the
    /// <c>Collection</c>'s default member in turn. A Let-coercion reads on until it has a value (<see cref="ValueOf"/>); parentheses do not.
    /// </remarks>
    /// <param name="session">The session the object lives in.</param>
    /// <param name="enclosed">The object.</param>
    /// <returns>What its default member returned; or error 91 for <c>Nothing</c>, and 438 or 450 for an object that has no default member to read.</returns>
    public static RuntimeSemanticsEvaluationResult EnclosedValueOf(IRuntimeSession session, VBObjectValue enclosed)
    {
        if (enclosed.IsNothing())
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, default,
                "Nothing has no value."));
        }

        return Call(session, enclosed) ?? RuntimeSemanticsEvaluationResult.Error(NoValue(session, enclosed, returnedByADefaultMember: false));
    }

    /// <summary>
    /// The error of an object that has no simple data value (<strong>MS-VBAL §5.6.2.2</strong>, <strong>§5.6.2.3</strong>): 13 for one a default member
    /// returned; otherwise 450 when its default member takes an argument, and 438 when it has none.
    /// </summary>
    /// <param name="session">The session the object lives in.</param>
    /// <param name="reference">The object, which is not <c>Nothing</c>.</param>
    /// <param name="returnedByADefaultMember">Whether the object is what a default member returned, rather than the value that was read.</param>
    public static VBRuntimeErrorInfo NoValue(IRuntimeSession session, VBObjectValue reference, bool returnedByADefaultMember)
    {
        if (returnedByADefaultMember)
        {
            return VBRuntimeErrorInfo.For(VBRuntimeErrorId.TypeMismatch, default,
                "A default member returned an object that has no value of its own (MS-VBAL 5.6.2.3).");
        }

        return session.Symbols.TryGetInstance(reference.Value, out var instance) && VBClassType.FromClassModule(instance.ClassModule).DefaultMember is not null
            ? VBRuntimeErrorInfo.For(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, default,
                "The object's default member takes an argument, which reading its value does not give it.")
            : VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, default,
                "The object has no default member to read its value from.");
    }

    // a Variant holds whatever it was given, another Variant included, so this goes all the way down.
    private static VBTypedValue Unwrapped(VBTypedValue value)
    {
        while (value is VBVariantValue { TypedValue: var wrapped })
        {
            value = wrapped;
        }

        return value;
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
