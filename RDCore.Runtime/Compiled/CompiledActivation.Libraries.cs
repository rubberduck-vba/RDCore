using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.External;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Compiled;

// a call of a function of the standard library inside an expression, made by compiled code as the interpreter makes it.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Calls the function of the standard library numbered <paramref name="library"/> of the body (<see cref="CompiledBody.Libraries"/>), given the first
    /// <paramref name="count"/> of <paramref name="first"/>, <paramref name="second"/> and <paramref name="third"/>, for what it returns of a scalar type: as
    /// the interpreter calls it, through the function's binding, each argument held in a <c>Variant</c> of its own subtype (MS-VBAL §5.3.1.11), and a
    /// parameter given none given what an omitted argument is. The function runs with the session's locale, as it does when the interpreter calls it. The
    /// instruction at <paramref name="pc"/> is the one that makes the call, which the frame says while the function runs.
    /// </summary>
    /// <param name="pc">The offset of the instruction that makes the call.</param>
    /// <param name="library">The number of the call among the body's libraries.</param>
    /// <param name="count">How many arguments the call is given.</param>
    /// <param name="first">The first argument, when it is given one.</param>
    /// <param name="second">The second argument, when it is given two.</param>
    /// <param name="third">The third argument, when it is given three.</param>
    /// <param name="kind">The kind of what the function returns.</param>
    /// <param name="integral">What the function returned, when <paramref name="kind"/> is integral.</param>
    /// <param name="floating">What the function returned, when <paramref name="kind"/> is floating-point.</param>
    /// <returns>
    /// Whether the call returned a payload of <paramref name="kind"/>. When not, the interpreter is given the call (<see cref="AfterFailedCall"/>), with the
    /// error the function raised, or to make it as it always does.
    /// </returns>
    public bool CallLibrary(int pc, int library, int count, VariantPayload first, VariantPayload second, VariantPayload third, ScalarKind kind,
        out long integral, out double floating)
    {
        (integral, floating) = (0, 0);
        if (!TryCallLibrary(pc, library, count, first, second, third, out var payload) || ScalarKinds.Of(payload.Type) != kind)
        {
            return false;
        }

        (integral, floating) = (payload.Integral, payload.Floating);
        return true;
    }

    /// <summary>
    /// Calls the function of the standard library numbered <paramref name="library"/> of the body for the <c>Variant</c> it returns, as
    /// <see cref="CallLibrary"/> calls one: <paramref name="value"/> is the payload the <c>Variant</c> holds, of its own subtype.
    /// </summary>
    /// <param name="pc">The offset of the instruction that makes the call.</param>
    /// <param name="library">The number of the call among the body's libraries.</param>
    /// <param name="count">How many arguments the call is given.</param>
    /// <param name="first">The first argument, when it is given one.</param>
    /// <param name="second">The second argument, when it is given two.</param>
    /// <param name="third">The third argument, when it is given three.</param>
    /// <param name="value">What the <c>Variant</c> the function returned holds, when it holds a value of a scalar type.</param>
    /// <returns>
    /// Whether the <c>Variant</c> holds a value of a scalar type. When not, the interpreter is given the call (<see cref="AfterFailedCall"/>), with the error
    /// the function raised, or to make it as it always does.
    /// </returns>
    public bool CallLibraryVariant(int pc, int library, int count, VariantPayload first, VariantPayload second, VariantPayload third, out VariantPayload value)
    {
        value = default;
        if (!TryCallLibrary(pc, library, count, first, second, third, out var payload))
        {
            return false;
        }

        value = new VariantPayload(payload);
        return true;
    }

    // the call made, and what it returned: a Variant is what it holds, a value of a scalar type a payload of its subtype; anything else - Null, Empty, a
    // String - is the interpreter's.
    private bool TryCallLibrary(int pc, int library, int count, VariantPayload first, VariantPayload second, VariantPayload third, out ScalarPayload payload)
    {
        payload = default;
        SetLibraryCall(pc, library);
        (_failed, _suspensionsBeforeCall) = (null, Suspensions);
        var called = _body.Libraries[library];
        var arguments = new IRuntimeValue[called.Parameters.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i < count)
            {
                var given = (i == 0 ? first : i == 1 ? second : third).Value;
                arguments[i] = VariantCell.Holding(given.Type, given.Box());
                continue;
            }

            var omitted = _executor.Expressions.OmittedArgument(_session, called.Parameters[i]);
            if (!omitted.IsSuccess)
            {
                _failed = omitted.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(omitted.ErrorInfo!);
                return false;
            }

            arguments[i] = omitted.Result!.RuntimeValue;
        }

        if (_executor.Expressions.Bindings is not { } bindings)
        {
            return false;
        }

        // straight to the library when nothing needs to see the call (ExternalCallPipeline.TryCallLibraryDirectly), through the pipeline otherwise.
        var result = bindings is RuntimeCallableBindingFactory { External: ExternalCallPipeline pipeline }
            && pipeline.TryCallLibraryDirectly(called.Function, arguments, _session.Symbols.Resolver, out var direct)
                ? direct
                : bindings.ForMember(called.Function).Call(_session.Symbols.Resolver, arguments);

        // Abs of the least value of a type overflows, but the interpreter decides it of the call (AbsOfTheLeast): the least Long of a constant expression is
        // itself, and of a Variant the next wider type. Compiled code declines, and the interpreter evaluates the instruction.
        if (result is { IsSuccess: false, ErrorInfo.ErrorId: (int)VBRuntimeErrorId.Overflow }
            && called.Function.GetProperty(SDK.Model.Symbols.Abstract.SymbolProperties.ExternalTarget) is "IStdMathModule.Abs/1")
        {
            return false;
        }

        if (!result.IsSuccess)
        {
            _failed = result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
            return false;
        }

        var value = result.Result is VBVariantValue { TypedValue: { } inner } ? inner : result.Result;
        return value is not null && ScalarPayload.TryOf(value.TypeInfo, value.RuntimeValue, out payload);
    }
}
