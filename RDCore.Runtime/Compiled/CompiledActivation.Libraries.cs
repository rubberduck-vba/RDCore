using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.External;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

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
        if (!TryCallLibrary(pc, library, receiver: null, count, first, second, third, firstValue, secondValue, thirdValue, out var payload) || ScalarKinds.Of(payload.Type) != kind)
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
        if (!TryCallLibrary(pc, library, receiver: null, count, first, second, third, firstValue, secondValue, thirdValue, out var payload))
        {
            return false;
        }

        value = new VariantPayload(payload);
        return true;
    }

    /// <summary>
    /// Calls the function of the standard library numbered <paramref name="library"/> of the body for the <c>String</c> it returns, given its arguments as
    /// <see cref="CallLibrary"/> calls one.
    /// </summary>
    /// <param name="pc">The offset of the instruction that makes the call.</param>
    /// <param name="library">The number of the call among the body's libraries.</param>
    /// <param name="count">How many arguments the call is given.</param>
    /// <param name="first">The first argument, when it is given one.</param>
    /// <param name="second">The second argument, when it is given two.</param>
    /// <param name="third">The third argument, when it is given three.</param>
    /// <param name="firstValue">
    /// What is given in place of <paramref name="first"/>: what a Variant variable holds (<see cref="VariantOperations.ValueOf"/>), or a <c>Variant</c>
    /// holding a String (<see cref="TextArgument"/>); <see langword="null"/> when the argument is <paramref name="first"/>.
    /// </param>
    /// <param name="secondValue">The same, for the second argument.</param>
    /// <param name="thirdValue">The same, for the third argument.</param>
    /// <param name="text">The text the function returned.</param>
    /// <returns>
    /// Whether the function returned a String compiled code holds. When not, the interpreter is given the call (<see cref="AfterFailedCall"/>), with the
    /// error the function raised, or to make it as it always does.
    /// </returns>
    public bool TryCallLibraryVariantText(int pc, int library, int count, VariantPayload first, VariantPayload second, VariantPayload third,
        IRuntimeValue? firstValue, IRuntimeValue? secondValue, IRuntimeValue? thirdValue, out string? text)
    {
        text = null;
        if (!TryCallLibraryValue(pc, library, receiver: null, count, first, second, third, firstValue, secondValue, thirdValue, out var value)
            || value is not VBStringValue { TrailingByte: null } returned)
        {
            return false;
        }

        text = returned.Value;
        return true;
    }

    /// <summary>
    /// A <c>Variant</c> holding <paramref name="text"/>, as the interpreter gives a <c>String</c> to a <c>Variant</c> parameter passed by value
    /// (MS-VBAL §5.5.1.2.12): what a call of a function of the standard library is given in place of an argument's payload.
    /// </summary>
    /// <param name="text">The text of the argument; <see langword="null"/> for a null String.</param>
    /// <returns>The <c>Variant</c>'s runtime value.</returns>
    public static IRuntimeValue TextArgument(string? text)
        => VariantCell.Holding(VBStringType.TypeInfo, (text is null ? VBStringValue.VBNullString : new VBStringValue(text)).RuntimeValue);

    /// <summary>
    /// <paramref name="text"/>, as the interpreter gives a <c>String</c> to a <c>String</c> parameter passed by value: what a call of a function of the
    /// standard library is given in place of an argument's payload.
    /// </summary>
    /// <param name="text">The text of the argument; <see langword="null"/> for a null String.</param>
    /// <returns>The String's runtime value.</returns>
    public static IRuntimeValue StringArgument(string? text) => (text is null ? VBStringValue.VBNullString : new VBStringValue(text)).RuntimeValue;

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
    private bool TryCallLibrary(int pc, int library, IObjectInstance? receiver, int count, VariantPayload first, VariantPayload second, VariantPayload third,
        IRuntimeValue? firstValue, IRuntimeValue? secondValue, IRuntimeValue? thirdValue, out ScalarPayload payload)
    {
        payload = default;
        return TryCallLibraryValue(pc, library, receiver, count, first, second, third, firstValue, secondValue, thirdValue, out var value)
            && ScalarPayload.TryOf(value.TypeInfo, value.RuntimeValue, out payload);
    }

    // the call made, and what it returned (for a Variant, what it holds); none when the call failed, or returned nothing. A member of a class is called on
    // the receiver, given before the arguments as the interpreter gives it (InvokeProcedureFrom), and is the member of the receiver's class (MemberOn); a
    // receiver with none makes the call the interpreter's.
    private bool TryCallLibraryValue(int pc, int library, IObjectInstance? receiver, int count, VariantPayload first, VariantPayload second,
        VariantPayload third, IRuntimeValue? firstValue, IRuntimeValue? secondValue, IRuntimeValue? thirdValue, [NotNullWhen(true)] out VBTypedValue? value)
    {
        value = null;
        SetLibraryCall(pc, library);
        (_failed, _suspensionsBeforeCall) = (null, Suspensions);
        var called = _body.Libraries[library];
        if ((called.OnAnObject ? MemberOn(called, receiver) : called.Function) is not { } function)
        {
            return false;
        }

        var before = called.OnAnObject ? 1 : 0;
        var arguments = new IRuntimeValue[before + called.Parameters.Length];
        if (called.OnAnObject)
        {
            arguments[0] = new VBObjectValue(receiver!.ObjectId).RuntimeValue;
        }

        for (var i = 0; i < called.Parameters.Length; i++)
        {
            // a Variant variable given to a Variant parameter is given what it held when the argument was evaluated.
            if (i < count && (i == 0 ? firstValue : i == 1 ? secondValue : thirdValue) is { } held)
            {
                arguments[before + i] = held;
                continue;
            }

            if (i < count)
            {
                // a parameter of a scalar type is given the value compiled code let-coerced to it, as the interpreter gives it; a Variant one, a Variant.
                var given = (i == 0 ? first : i == 1 ? second : third).Value;
                arguments[before + i] = called.Parameters[i].ResolvedType is VBVariantType ? VariantCell.Holding(given.Type, given.Box()) : given.Box();
                continue;
            }

            var omitted = _executor.Expressions.OmittedArgument(_session, called.Parameters[i]);
            if (!omitted.IsSuccess)
            {
                _failed = omitted.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(omitted.ErrorInfo!);
                return false;
            }

            arguments[before + i] = omitted.Result!.RuntimeValue;
        }

        if (_executor.Expressions.Bindings is not { } bindings)
        {
            return false;
        }

        var result = LibraryCall(bindings, called, function, arguments);

        // Abs of the least value of a type overflows, but the interpreter decides it of the call (AbsOfTheLeast): the least Long of a constant expression is
        // itself, and of a Variant the next wider type. Compiled code declines, and the interpreter evaluates the instruction.
        if (result is { IsSuccess: false, ErrorInfo.ErrorId: (int)VBRuntimeErrorId.Overflow }
            && called.Function.GetProperty(SymbolProperties.ExternalTarget) is "IStdMathModule.Abs/1")
        {
            return false;
        }

        if (!result.IsSuccess)
        {
            _failed = result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
            return false;
        }

        value = result.Result is VBVariantValue { TypedValue: { } inner } ? inner : result.Result;
        return value is not null;
    }

    // the call of a function of the standard library compiled code makes: straight to the library when nothing needs to see the call
    // (ExternalCallPipeline.TryCallLibraryDirectly, TryCallLibraryMemberDirectly), through the pipeline otherwise.
    private RuntimeSemanticsEvaluationResult LibraryCall(
        ICallableBindingFactory bindings, CompiledLibraryCall called, VBTypeMemberSymbol function, IRuntimeValue[] arguments)
    {
        _body.CountLibraryCall();
        return bindings is RuntimeCallableBindingFactory { External: ExternalCallPipeline pipeline }
            && (called.OnAnObject
                ? pipeline.TryCallLibraryMemberDirectly(function, arguments, _session.Symbols.Resolver, out var direct)
                : pipeline.TryCallLibraryDirectly(function, arguments, _session.Symbols.Resolver, out direct))
                ? direct
                : bindings.ForMember(function).Call(_session.Symbols.Resolver, arguments);
    }

    // the member of the receiver's class the interpreter calls for the member compiled code calls, by its name or as the default member (MS-VBAL §5.6.12,
    // §5.6.13): the same member of the library, which its key names, whatever declaration of the class the call was bound through. None for Nothing,
    // which is error 91, and for an object of a class with no such member: the call is then the interpreter's.
    private static VBTypeMemberSymbol? MemberOn(CompiledLibraryCall called, IObjectInstance? receiver)
    {
        if (receiver is null)
        {
            return null;
        }

        var key = called.Function.GetProperty(SymbolProperties.ExternalTarget);
        foreach (var member in receiver.ClassModule.DefaultInterfaceMembers)
        {
            if (ReferenceEquals(member, called.Function)
                || member.GetProperty(SymbolProperties.ExternalTarget) == key && SDK.Runtime.StdLib.StandardLibrary.IsLibraryMember(member))
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    /// The object the object variable whose cell is <paramref name="cell"/> refers to, which a member of a class of the standard library is called on
    /// (<see cref="CallLibraryMember"/>), read before the arguments of the call are evaluated, as the interpreter reads it (MS-VBAL §5.6.13.1).
    /// </summary>
    /// <param name="cell">The cell of the object variable, or of the object field of a record.</param>
    /// <returns>
    /// The object; <see langword="null"/> for <c>Nothing</c>, for an object the session has no instance of, and under a debugger that observes member
    /// accesses: the call is then the interpreter's.
    /// </returns>
    public IObjectInstance? LibraryReceiverIn(object? cell) => InstanceIn(cell);

    /// <summary>
    /// The object of the <c>With</c> block opened at <paramref name="opener"/> (MS-VBAL §5.4.2.21), which a member of a class of the standard library is
    /// called on, as <see cref="LibraryReceiverIn"/> reads the object a variable refers to.
    /// </summary>
    /// <param name="opener">The offset of the <c>With</c> statement of the block.</param>
    /// <returns>The object; <see langword="null"/> for a block that holds none, or <c>Nothing</c>.</returns>
    public IObjectInstance? LibraryReceiverOfWith(int opener) => InstanceOfBlock(opener);

    /// <summary>
    /// Calls the member of a class of the standard library numbered <paramref name="library"/> of the body on <paramref name="receiver"/>, for the
    /// <c>Variant</c> it returns, given its arguments as <see cref="CallLibraryVariant"/> gives those of a function: as the interpreter calls it, through
    /// the member's binding with the object as its implicit <c>Me</c>, or straight to the library when nothing needs to see the call. The member is a
    /// pure one: the <c>Item</c> or the <c>Count</c> of a <c>Collection</c> (MS-VBAL §6.1.3.1.1).
    /// </summary>
    /// <param name="pc">The offset of the instruction that makes the call.</param>
    /// <param name="library">The number of the call among the body's libraries.</param>
    /// <param name="receiver">The object the member is called on (<see cref="LibraryReceiverIn"/>, <see cref="LibraryReceiverOfWith"/>).</param>
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
    /// <param name="value">What the <c>Variant</c> the member returned holds, when it holds a value of a scalar type.</param>
    /// <returns>
    /// Whether the <c>Variant</c> holds a value of a scalar type. When not, the interpreter is given the call (<see cref="AfterFailedCall"/>): with the
    /// error the member raised (5 or 9, of an <c>Item</c> no member of the collection answers to), which it raises; or to make the call itself, when the
    /// member returns an object or a <c>String</c>, or when the member is not called on the receiver here.
    /// </returns>
    public bool CallLibraryMember(int pc, int library, IObjectInstance? receiver, int count, VariantPayload first, VariantPayload second,
        VariantPayload third, IRuntimeValue? firstValue, IRuntimeValue? secondValue, IRuntimeValue? thirdValue, out VariantPayload value)
    {
        value = default;
        if (!TryCallLibrary(pc, library, receiver, count, first, second, third, firstValue, secondValue, thirdValue, out var payload))
        {
            return false;
        }

        value = new VariantPayload(payload);
        return true;
    }
}
