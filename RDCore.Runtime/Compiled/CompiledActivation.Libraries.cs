using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.External;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Compiled;

// a call of a function of the standard library inside an expression, made by compiled code as the interpreter makes it.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Calls the function of the standard library numbered <paramref name="library"/> of the body (<see cref="CompiledBody.Libraries"/>), given the first
    /// <paramref name="count"/> of <paramref name="first"/>, <paramref name="second"/> and <paramref name="third"/>, for what it returns of a scalar type: as
    /// the interpreter calls it, through the function's binding, each argument held in a <c>Variant</c> of its own subtype or, for a parameter of a scalar
    /// type, the value compiled code let-coerced to that type (MS-VBAL §5.3.1.11), and a parameter given none given what an omitted argument is. The
    /// function runs with the session's locale, as it does when the interpreter calls it. The instruction at <paramref name="pc"/> is the one that
    /// makes the call, which the frame says while the function runs.
    /// </summary>
    /// <param name="pc">The offset of the instruction that makes the call.</param>
    /// <param name="library">The number of the call among the body's libraries.</param>
    /// <param name="count">How many arguments the call is given.</param>
    /// <param name="first">The first argument, when it is given one.</param>
    /// <param name="second">The second argument, when it is given two.</param>
    /// <param name="third">The third argument, when it is given three.</param>
    /// <param name="firstValue">
    /// What the Variant variable given as the first argument holds (<see cref="VariantOperations.ValueOf"/>), given in place of <paramref name="first"/>;
    /// <see langword="null"/> when the argument is <paramref name="first"/>.
    /// </param>
    /// <param name="secondValue">The same, for the second argument.</param>
    /// <param name="thirdValue">The same, for the third argument.</param>
    /// <param name="kind">The kind of what the function returns.</param>
    /// <param name="integral">What the function returned, when <paramref name="kind"/> is integral.</param>
    /// <param name="floating">What the function returned, when <paramref name="kind"/> is floating-point.</param>
    /// <returns>
    /// Whether the call returned a payload of <paramref name="kind"/>. When not, the interpreter is given the call (<see cref="AfterFailedCall"/>), with the
    /// error the function raised, or to make it as it always does.
    /// </returns>
    public bool CallLibrary(int pc, int library, int count, VariantPayload first, VariantPayload second, VariantPayload third,
        IRuntimeValue? firstValue, IRuntimeValue? secondValue, IRuntimeValue? thirdValue, ScalarKind kind,
        out long integral, out double floating)
    {
        (integral, floating) = (0, 0);
        if (!TryCallLibrary(pc, library, count, first, second, third, firstValue, secondValue, thirdValue, out var payload) || ScalarKinds.Of(payload.Type) != kind)
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
    /// <param name="firstValue">
    /// What the Variant variable given as the first argument holds (<see cref="VariantOperations.ValueOf"/>), given in place of <paramref name="first"/>;
    /// <see langword="null"/> when the argument is <paramref name="first"/>.
    /// </param>
    /// <param name="secondValue">The same, for the second argument.</param>
    /// <param name="thirdValue">The same, for the third argument.</param>
    /// <param name="value">What the <c>Variant</c> the function returned holds, when it holds a value of a scalar type.</param>
    /// <returns>
    /// Whether the <c>Variant</c> holds a value of a scalar type. When not, the interpreter is given the call (<see cref="AfterFailedCall"/>), with the error
    /// the function raised, or to make it as it always does.
    /// </returns>
    public bool CallLibraryVariant(int pc, int library, int count, VariantPayload first, VariantPayload second, VariantPayload third,
        IRuntimeValue? firstValue, IRuntimeValue? secondValue, IRuntimeValue? thirdValue, out VariantPayload value)
    {
        value = default;
        if (!TryCallLibrary(pc, library, count, first, second, third, firstValue, secondValue, thirdValue, out var payload))
        {
            return false;
        }

        value = new VariantPayload(payload);
        return true;
    }

    /// <summary>
    /// What the <c>Variant</c> parameter or variable in <paramref name="slot"/> holds when the frame holds it in no Variant's cell
    /// (<see cref="VariantCellOf"/>): a parameter passed a <c>String</c>, an error, an object or no argument by value.
    /// </summary>
    /// <param name="slot">The slot of the parameter or variable.</param>
    /// <returns>
    /// <see langword="null"/> when the frame holds no <c>Variant</c> there, or one whose value is unknown or holds an array or a record: the interpreter
    /// is then given the call.
    /// </returns>
    public IRuntimeValue? VariantValueOf(int slot)
    {
        var frame = Frame;
        if (!(frame.TryGetSlotCell(slot, out var cell) || frame.TryResolve(_body.SlotSymbols[slot], out cell)))
        {
            return null;
        }

        if (cell is VariantCell)
        {
            return VariantOperations.ValueOf(cell);
        }

        return cell is not IKnowledgeBinding { IsKnown: false }
            && cell.Value is VBRuntimeVariantValue { WrappedValue.TypeInfo: not (VBArrayType or VBUserDefinedType) } held
                ? held
                : null;
    }

    // the call made, and what it returned: a Variant is what it holds, a value of a scalar type a payload of its subtype; anything else - Null, Empty, a
    // String - is the interpreter's.
    private bool TryCallLibrary(int pc, int library, int count, VariantPayload first, VariantPayload second, VariantPayload third,
        IRuntimeValue? firstValue, IRuntimeValue? secondValue, IRuntimeValue? thirdValue, out ScalarPayload payload)
    {
        payload = default;
        SetLibraryCall(pc, library);
        (_failed, _suspensionsBeforeCall) = (null, Suspensions);
        var called = _body.Libraries[library];
        var arguments = new IRuntimeValue[called.Parameters.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            // a Variant variable given to a Variant parameter is given what it held when the argument was evaluated.
            if (i < count && (i == 0 ? firstValue : i == 1 ? secondValue : thirdValue) is { } held)
            {
                arguments[i] = held;
                continue;
            }

            if (i < count)
            {
                // a parameter of a scalar type is given the value compiled code let-coerced to it, as the interpreter gives it; a Variant one, a Variant.
                var given = (i == 0 ? first : i == 1 ? second : third).Value;
                arguments[i] = called.Parameters[i].ResolvedType is VBVariantType ? VariantCell.Holding(given.Type, given.Box()) : given.Box();
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

        var result = LibraryCall(bindings, called, arguments);

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

    // the call of a function of the standard library compiled code makes: straight to the library when nothing needs to see the call
    // (ExternalCallPipeline.TryCallLibraryDirectly), through the pipeline otherwise.
    private RuntimeSemanticsEvaluationResult LibraryCall(ICallableBindingFactory bindings, CompiledLibraryCall called, IRuntimeValue[] arguments)
        => bindings is RuntimeCallableBindingFactory { External: ExternalCallPipeline pipeline }
            && pipeline.TryCallLibraryDirectly(called.Function, arguments, _session.Symbols.Resolver, out var direct)
                ? direct
                : bindings.ForMember(called.Function).Call(_session.Symbols.Resolver, arguments);
}
