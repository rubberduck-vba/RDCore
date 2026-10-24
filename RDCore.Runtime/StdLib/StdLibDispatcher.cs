using RDCore.External;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.StdLib;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;

namespace RDCore.Runtime.StdLib;

/// <summary>
/// The <see cref="IExternalDispatcher"/> for the standard library: reaches the implementation of an
/// <c>IStd*</c> member and runs it.
/// </summary>
/// <remarks>
/// The declarations are the only map between a symbol and the code behind it, and
/// <see cref="StdLibSymbolReader"/> already stamped each symbol with the declaration it was read off
/// (<see cref="SymbolProperties.ExternalTarget"/>). So this finds the method by the reader's own key rather
/// than by matching names, and an implementation that does not exist is a member that reports so rather
/// than a call that goes somewhere surprising.
/// <para>
/// 🚧 The composed pipeline this is eventually the innermost layer of — availability, then the workspace's
/// policy, then whatever is intercepting calls — does not exist yet. TODO: wrap this rather than change it,
/// so that a blocked <c>Declare</c> and a mocked <c>MsgBox</c> are the same mechanism.
/// </para>
/// </remarks>
public sealed class StdLibDispatcher : IExternalCallProvider
{
    private readonly ImmutableDictionary<string, MethodInfo> _methods;
    private readonly ImmutableDictionary<Type, object> _implementations;

    /// <summary>
    /// The dispatcher for <paramref name="session"/>, over every standard-library implementation there is.
    /// </summary>
    /// <remarks>
    /// The registry is written here rather than discovered, so that what the platform can actually run is one
    /// list somebody can read. An interface absent from it is a module nothing implements yet, and every
    /// member of it says so when called.
    /// <para>
    /// 🚧 TODO as each module lands: <c>RegExp</c>. A constant module needs no implementation: its
    /// constants are substituted where they are used, never called.
    /// </para>
    /// </remarks>
    /// <param name="session">The session the implementations read their state from.</param>
    /// <param name="outside">
    /// What the members that reach outside the platform reach - the dialogs, the desktop, the settings (<see cref="StdInteraction"/>). None of it unless said
    /// otherwise (<see cref="ExternalWorld.None"/>), which is what an analysis is composed with.
    /// </param>
    /// <param name="cancellation">The run's cancellation, which stops a member that waits - a dialog - from waiting.</param>
    public static StdLibDispatcher For(IRuntimeSession session, ExternalWorld? outside = null, CancellationToken cancellation = default)
    {
        // a collection makes the enumerator its For Each is driven by, which is an object of a class of its own.
        var enumerators = new StdEnumVariant(session);

        return new(new Dictionary<Type, object>
        {
            [typeof(IStdInformationModule)] = new StdInformation(session),
            [typeof(IStdFileSystemModule)] = new StdFileSystem(session),
            [typeof(IStdStringsModule)] = new StdStrings(session),
            [typeof(IStdFinancialModule)] = new StdFinancial(),
            [typeof(IStdMathModule)] = new StdMath(session),
            [typeof(IStdDateTimeModule)] = new StdDateTime(session),
            [typeof(IStdErrClass)] = new ErrObject(session),
            [typeof(IStdConversionModule)] = new StdConversion(session),
            [typeof(IStdHiddenModule)] = new StdHidden(session),
            [typeof(IStdInteractionModule)] = new StdInteraction(session, outside, cancellation),
            [typeof(IStdCollectionClass)] = new StdCollection(session, enumerators),
            [typeof(IStdEnumVariantClass)] = enumerators,
        });
    }

    /// <summary>
    /// Creates the dispatcher over a set of implementations.
    /// </summary>
    /// <param name="implementations">
    /// The implementations, keyed by the <c>IStd*</c> interface each one implements. A library member whose
    /// interface is absent here is one nothing implements yet, and reports that when called.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Two members of one declaration share a name and an arity, so the reader's key cannot tell them apart.
    /// That is a mistake in the declaration: it would otherwise dispatch one of them to the other.
    /// </exception>
    public StdLibDispatcher(IReadOnlyDictionary<Type, object> implementations)
    {
        _implementations = implementations.ToImmutableDictionary();

        var methods = ImmutableDictionary.CreateBuilder<string, MethodInfo>(StringComparer.Ordinal);
        foreach (var (declaringType, _) in implementations)
        {
            foreach (var method in declaringType.GetMethods())
            {
                var key = StdLibSymbolReader.ExternalTargetOf(declaringType, method);
                if (methods.ContainsKey(key))
                {
                    throw new InvalidOperationException(
                        $"'{declaringType.Name}' declares more than one '{method.Name}' of the same arity: a standard-library " +
                        "member is identified by its name and arity, so these two cannot be told apart.");
                }

                methods.Add(key, method);
            }
        }

        _methods = methods.ToImmutable();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A member the standard library declares — a member carrying the key the reader stamped on it, that no
    /// other library claims as its own. A referenced library's members carry a key of their own kind and the
    /// name of the library (<see cref="SymbolProperties.Library"/>); those are its provider's, and answering
    /// them here would report as unimplemented what only has not been asked of the right provider.
    /// </remarks>
    public bool CanDispatch(ExternalCallRequest request)
        => request.Member.GetProperty(SymbolProperties.ExternalTarget) is { Length: > 0 }
        && request.Member.GetProperty(SymbolProperties.Library) is null or StdLibSymbolProvider.LibraryName;

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, ISymbolResolver resolver)
    {
        if (EntryOf(request.Member) is not { } entry)
        {
            return NotImplemented(request, "nothing implements it yet");
        }

        if (!TryMarshalArguments(entry, request, resolver, out var arguments))
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.InvalidProcedureCallOrArgument, request.CallSite,
                $"'{request.Member.Name}' was called with arguments its implementation cannot accept."));
        }

        // a class whose instances have state of their own is told which instance the call is on: the implementation is one object for the
        // whole class, and what is in a collection is the collection's.
        if (entry.Implementation is IStdLibReceiverBound bound)
        {
            bound.Receiver = entry.HasReceiver && TypedValue(request.Arguments[0]) is VBObjectValue receiver ? receiver.Value : null;
        }

        // an implementation returns the outcome rather than throwing, the same as the rest of the semantics
        // layer - so an exception escaping one is a bug in it, not a program error, and must not be dressed
        // up as one.
        if (entry.Invoker is { } invoker)
        {
            return invoker(entry.Implementation, arguments);
        }

        var returned = entry.Method.Invoke(entry.Implementation, arguments);
        return returned is IRuntimeSemanticsEvaluationResult result
            ? new RuntimeSemanticsEvaluationResult(result.Result, result.ErrorInfo)
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    // a member of the library, bound once to the code behind it (IR lowering S1.9): its method and implementation, found by the key the reader stamped on
    // it, the call of it generated from its declaration (StdLibInvokers) - reflected, for a member none is generated for - and what reflection says of each
    // parameter, which it would otherwise be asked again on every call. A member nothing implements has no entry.
    private sealed record class StdLibEntry(
        MethodInfo Method, StdLibInvoker? Invoker, object Implementation, ImmutableArray<StdLibParameter> Parameters, bool HasReceiver);

    // a parameter of an implementation, as marshalling asks of it: its type, whether it is the `params` a ParamArray is declared as, and what is passed
    // for it when the call leaves it out - when that is the same every time.
    private sealed record class StdLibParameter(ParameterInfo Info, Type Type, bool IsParamArray, bool IsOptional, VBParameterSymbol? Symbol)
    {
        // a ParamArray told nothing is an empty array of its elements, which nothing can change.
        public Array? EmptyParamArray { get; } = IsParamArray ? System.Array.CreateInstance(Type.GetElementType()!, 0) : null;
    }

    private readonly ConcurrentDictionary<VBTypeMemberSymbol, StdLibEntry?> _entries = new(ReferenceEqualityComparer.Instance);

    // compared by reference: a symbol is a record, which two members of the same name and signature would be equal as.
    private StdLibEntry? EntryOf(VBTypeMemberSymbol member)
        => _entries.TryGetValue(member, out var entry) ? entry : _entries.GetOrAdd(member, BindEntry);

    private StdLibEntry? BindEntry(VBTypeMemberSymbol member)
    {
        if (member.GetProperty(SymbolProperties.ExternalTarget) is not { } target
            || !_methods.TryGetValue(target, out var method)
            || !_implementations.TryGetValue(method.DeclaringType!, out var implementation))
        {
            return null;
        }

        // a member of a class is called on an object, which arrives as the implicit Me at parameter 0. The library's classes keep no state in the
        // receiver - the error object is a view of the session's - so the implementation is written without it, and it is the arguments after it
        // that marshal.
        var hasReceiver = HasReceiver(member);
        var symbols = RuntimeProcedureInvoker.GetParameters(member);
        symbols = hasReceiver ? symbols.RemoveAt(0) : symbols;
        var parameters = method.GetParameters().Select((parameter, index) => new StdLibParameter(
            parameter, parameter.ParameterType, parameter.GetCustomAttribute<ParamArrayAttribute>() is not null, parameter.IsOptional,
            index < symbols.Length ? symbols[index] : null));

        return new StdLibEntry(method, StdLibInvokers.For(target), implementation, [.. parameters], hasReceiver);
    }

    // MS-VBAL 6.1.3.2.1.2's "Application-defined or object-defined error" is what VBA says about a member it
    // has no answer for, and it is the honest answer here too: the symbol resolves, the call is well-formed,
    // and the platform has not got the code.
    private static RuntimeSemanticsEvaluationResult NotImplemented(ExternalCallRequest request, string because)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
            VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, request.CallSite,
            $"'{request.Member.Name}' could not be called: {because}."));

    // a variable passed to a ByRef parameter (StdLibByRefAttribute) arrives as its address: what the member is given is the value the variable holds, read
    // as the variable is declared - a Variant, or what a Variant parameter was given instead (VBRuntimeReference.DeclaredType), which it is told of.
    private static bool TryDereference(
        IRuntimeValue argument, ParameterInfo parameter, VBParameterSymbol? symbol, ISymbolResolver resolver, out object? marshalled)
    {
        marshalled = null;
        if (argument is not VBRuntimeReference reference || symbol is null || !resolver.TryRead(reference.Value, out var handle))
        {
            return false;
        }

        var value = (reference.DeclaredType ?? symbol.ResolvedType).CreateValue(handle);
        marshalled = parameter.ParameterType == typeof(VBVariantValue)
            ? (value as VBVariantValue ?? new VBVariantValue(value)) with { ReferencedType = reference.DeclaredType }
            : value;
        return parameter.ParameterType.IsInstanceOfType(marshalled);
    }

    // the reader built each parameter's declared type from the method's own, so this runs that backwards: a
    // VBTypedValue parameter takes the argument as it is, and a CLR enum parameter takes its numeric value.
    private static bool TryMarshalArguments(StdLibEntry entry, ExternalCallRequest request, ISymbolResolver resolver, out object?[] arguments)
    {
        var parameters = entry.Parameters;
        arguments = new object?[parameters.Length];

        // the receiver is no argument of the implementation (BindEntry).
        var supplied = entry.HasReceiver ? request.Arguments.AsSpan(1) : request.Arguments;

        // a call from source never leaves an argument out: the interpreter fills an omitted Optional with what
        // an omitted argument gives it (RuntimeExpressionEvaluator.OmittedArgument) - its declared default, Missing
        // for a Variant that declares none, a typed one's own type's default, never null. A direct caller can stop
        // short, and what it left out is filled from the symbol (TryOmit). A Missing argument is an omitted one, as
        // it is to any COM member, whether the call left it out or passed on a parameter that was itself omitted; so
        // it is filled the same way, which for a VBTypedValue parameter that states no default is null - the
        // "Missing" its signature declares. An explicit Empty is passed as it is: whether it is omitted is the
        // implementation's to say.
        if (supplied.Length > parameters.Length)
        {
            return false;
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            if (index >= supplied.Length)
            {
                if (!TryOmit(parameter, out arguments[index]))
                {
                    return false;
                }

                continue;
            }

            var argument = supplied[index];
            if (argument is VBRuntimeReference)
            {
                if (!TryDereference(argument, parameter.Info, parameter.Symbol, resolver, out arguments[index]))
                {
                    return false;
                }

                continue;
            }

            // the typed value is recovered once, to say whether it is Missing and to be what is passed.
            var typed = TypedValue(argument);
            if (parameter.IsOptional && typed is VBMissingValue)
            {
                if (!TryOmit(parameter, out arguments[index]))
                {
                    return false;
                }

                continue;
            }

            if (!TryMarshal(argument, typed, parameter, out arguments[index]))
            {
                return false;
            }
        }

        return true;
    }

    // what a direct caller left out or passed as Missing, filled the way the interpreter fills it, short of what only the interpreter can reduce. A ParamArray is
    // the empty array a call that passes it nothing collects (MS-VBAL 5.3.1.11's bounds 0 to -1). A required parameter is refused, as a call
    // from source that omits it is (error 5 here, where a call from source is 449 before it gets this far), and the member is not
    // called. An optional one takes the default its symbol states as a literal - an enumeration constant, or a StdLibDefaultAttribute's
    // value, so that Split told nothing is told Limit -1, as it would be from source - and otherwise the implementation's own default:
    // null, the "Missing" its signature declares.
    private static bool TryOmit(StdLibParameter parameter, out object? omitted)
    {
        omitted = null;
        if (parameter.IsParamArray)
        {
            omitted = parameter.EmptyParamArray;
            return true;
        }

        if (!parameter.IsOptional)
        {
            return false;
        }

        if (parameter.Info.DefaultValue is null && parameter.Symbol?.DefaultValue is LiteralExpressionNode { StaticValue: { } stated })
        {
            return TryMarshal(stated.RuntimeValue, TypedValue(stated.RuntimeValue), parameter.Type, out omitted);
        }

        omitted = parameter.Info.HasDefaultValue ? parameter.Info.DefaultValue : null;
        return true;
    }

    // a ParamArray arrives as the one array the caller collected the rest of its arguments into, and is declared as `params`:
    // what the implementation takes is an array of the element type, each element a Variant.
    private static bool TryMarshal(IRuntimeValue argument, VBTypedValue? typed, StdLibParameter parameter, out object? marshalled)
    {
        if (!parameter.IsParamArray)
        {
            return TryMarshal(argument, typed, parameter.Type, out marshalled);
        }

        marshalled = null;
        if (typed is not VBArrayValue collected)
        {
            return false;
        }

        var elementType = parameter.Type.GetElementType()!;
        var elements = System.Array.CreateInstance(elementType, collected.Length);
        for (var index = 0; index < collected.Length; index++)
        {
            if (collected.ElementAt(index) is not { } element
                || (elementType == typeof(VBVariantValue) ? element as VBVariantValue ?? new VBVariantValue(element) : element) is not { } converted
                || !elementType.IsInstanceOfType(converted))
            {
                return false;
            }

            elements.SetValue(converted, index);
        }

        marshalled = elements;
        return true;
    }

    // the reader gives an instance member an implicit Me at parameter 0, exactly as a workspace class's members have.
    private static bool HasReceiver(VBTypeMemberSymbol member)
        => RuntimeProcedureInvoker.GetParameters(member) is [{ Name: "Me", ParameterKind: ParameterKind.ImplicitByRef }, ..];

    // typed: the typed value argument is the storage of (TypedValue).
    private static bool TryMarshal(IRuntimeValue argument, VBTypedValue? typed, Type parameterType, out object? marshalled)
    {
        marshalled = null;

        if (parameterType.IsEnum)
        {
            marshalled = Enum.ToObject(parameterType, Convert.ToInt64(argument.BoxedValue));
            return true;
        }

        // the caller Let-coerced the argument to the parameter's declared type, so what arrives is that type's
        // storage - a boxed double for a Double, a boxed short for an Integer - and the typed value it is the
        // storage of is all there is left to recover. One that is still the wrong type is a coercion the
        // caller was supposed to have done, not something to do quietly here.
        // A Variant parameter takes anything, that being what a Variant is - MS-VBAL 5.5.1.2.2's Let-coercion to
        // Variant has no failing case. Most of the library declares its parameters that way, so a Variant that
        // rejected an argument would leave most of the library uncallable.
        marshalled = parameterType == typeof(VBVariantValue) ? typed is null ? null : new VBVariantValue(typed) : typed;
        return marshalled is not null && parameterType.IsInstanceOfType(marshalled);
    }

    /// <summary>
    /// The typed value a runtime value is the storage of.
    /// </summary>
    /// <remarks>
    /// An external call carries <see cref="IRuntimeValue"/> arguments rather than typed ones, so the declared
    /// type a member was called with has to be recovered here. It survives: each intrinsic stores its own exact
    /// managed type — <c>short</c> for <c>Integer</c> and <c>int</c> for <c>Long</c>, not one integer type for
    /// both — which is what lets <c>Len</c> answer "the number of bytes required to store a variable" instead
    /// of guessing. <c>Date</c> and <c>Double</c> both store a <c>double</c> and are indistinguishable here.
    /// <para>
    /// 🚧 TODO with the first member that declares a <c>Date</c>, <c>Decimal</c>, <c>LongPtr</c> or <c>Object</c>
    /// parameter. The first three store what another type stores — a <c>double</c>, a <c>decimal</c>, a
    /// <c>long</c> or an <c>int</c> — so the argument recovers as a <c>Double</c>, a <c>Currency</c>, a
    /// <c>LongLong</c> or a <c>Long</c>, which such a parameter refuses, and a <c>Decimal</c> beyond
    /// <c>Currency</c>'s range throws instead; and an object reference has no case here at all, so it is refused
    /// too. A <c>Variant</c> argument is unaffected, carrying its typed value whole. The declared type is what has
    /// to decide the rest.
    /// </para>
    /// </remarks>
    private static VBTypedValue? TypedValue(IRuntimeValue argument) => argument switch
    {
        // a Variant carries the whole typed value it wraps, and this is the only way to reach it: its own
        // BoxedValue unwraps all the way down to the managed value, which is exactly what must not happen here
        // - and every library member declaring a Variant parameter gets its argument coerced to one on the way
        // in, so this is the case nearly every call arrives as.
        VBRuntimeVariantValue variant => variant.WrappedValue,
        // the runtime values that are a kind rather than a managed value, and could not be told apart by one:
        // Null's and Empty's own boxed values say nothing, and a Boolean's is an integer.
        VBRuntimeNullValue => VBNullValue.Null,
        VBRuntimeEmptyValue => VBEmptyValue.Empty,
        VBRuntimeBooleanValue boolean => new VBBooleanValue(Convert.ToInt32(boolean.BoxedValue) != 0),
        VBRuntimeCurrencyValue currency => new VBCurrencyValue(Convert.ToDecimal(currency.BoxedValue)),
        VBRuntimeDecimalValue @decimal => new VBDecimalValue(Convert.ToDecimal(@decimal.BoxedValue)),
        VBRuntimeHResult error => new VBErrorValue(Convert.ToInt32(error.BoxedValue)),
        // an object is the identity of one: what the argument is the storage of, as with the rest.
        VBRuntimeValue<VBRuntimeObjectId> identity => new VBObjectValue(identity.StoredValue),
        // a String is the runtime value that holds its data, and the typed value is bound to that same one rather
        // than rebuilt from its managed value: vbNullString's is null and boxes nothing - it is the default the
        // interpreter fills an omitted Optional String with - and an odd string's holds a byte beside it.
        IRuntimeValue<string> text => new VBStringValue(new ValueBindingHandle(text)),
        _ => argument.BoxedValue switch
        {
            // a Variant stores the whole typed value it wraps, and an array and a UDT are each boxed around
            // their own storage - so all three already are the value that was passed.
            VBTypedValue typed => typed,
            VBRuntimeArrayValue array => array.Array,
            VBRuntimeUserDefinedTypeValue udt => udt.UserDefinedType,
            byte value => new VBByteValue(value),
            short value => new VBIntegerValue(value),
            int value => new VBLongValue(value),
            long value => new VBLongLongValue(value),
            float value => new VBSingleValue(value),
            double value => new VBDoubleValue(value),
            decimal value => new VBCurrencyValue(value),
            _ => null,
        },
    };
}
