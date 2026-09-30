using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.StdLib;
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
    /// 🚧 TODO as each module lands: <c>Math</c>, <c>DateTime</c>,
    /// <c>Interaction</c>, <c>Collection</c>, <c>RegExp</c>, and the constant modules.
    /// </para>
    /// </remarks>
    /// <param name="session">The session the implementations read their state from.</param>
    public static StdLibDispatcher For(IRuntimeSession session)
        => new(new Dictionary<Type, object>
        {
            [typeof(IStdInformationModule)] = new StdInformation(session),
            [typeof(IStdFileSystemModule)] = new StdFileSystem(session),
            [typeof(IStdStringsModule)] = new StdStrings(),
            [typeof(IStdFinancialModule)] = new StdFinancial(),
            [typeof(IStdErrClass)] = new ErrObject(session),
            [typeof(IStdConversionModule)] = new StdConversion(session),
            [typeof(IStdSpecialFormsModule)] = new StdSpecialForms(),
        });

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
    /// A member the standard library declares — which is exactly a member carrying the key the reader stamped
    /// on it. A member of some other external target carries no such key, and is some other provider's.
    /// </remarks>
    public bool CanDispatch(ExternalCallRequest request)
        => request.Member.GetProperty(SymbolProperties.ExternalTarget) is { Length: > 0 };

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, ISymbolResolver resolver)
    {
        var target = request.Member.GetProperty(SymbolProperties.ExternalTarget)!;
        if (!_methods.TryGetValue(target, out var method)
            || !_implementations.TryGetValue(method.DeclaringType!, out var implementation))
        {
            return NotImplemented(request, "nothing implements it yet");
        }

        if (!TryMarshalArguments(method, request, out var arguments))
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.InvalidProcedureCallOrArgument, request.CallSite,
                $"'{request.Member.Name}' was called with arguments its implementation cannot accept."));
        }

        // an implementation returns the outcome rather than throwing, the same as the rest of the semantics
        // layer - so an exception escaping one is a bug in it, not a program error, and must not be dressed
        // up as one.
        var returned = method.Invoke(implementation, arguments);
        return returned is IRuntimeSemanticsEvaluationResult result
            ? new RuntimeSemanticsEvaluationResult(result.Result, result.ErrorInfo)
            : RuntimeSemanticsEvaluationResult.InternalError();
    }

    // MS-VBAL 6.1.3.2.1.2's "Application-defined or object-defined error" is what VBA says about a member it
    // has no answer for, and it is the honest answer here too: the symbol resolves, the call is well-formed,
    // and the platform has not got the code.
    private static RuntimeSemanticsEvaluationResult NotImplemented(ExternalCallRequest request, string because)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
            VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, request.CallSite,
            $"'{request.Member.Name}' could not be called: {because}."));

    // the reader built each parameter's declared type from the method's own, so this runs that backwards: a
    // VBTypedValue parameter takes the argument as it is, and a CLR enum parameter takes its numeric value.
    private static bool TryMarshalArguments(MethodInfo method, ExternalCallRequest request, out object?[] arguments)
    {
        var parameters = method.GetParameters();
        arguments = new object?[parameters.Length];

        // a member of a class is called on an object, which arrives as the implicit Me at parameter 0. The
        // library's classes keep no state in the receiver - the error object is a view of the session's - so the
        // implementation is written without it, and it is the arguments after it that marshal.
        var supplied = HasReceiver(request.Member) ? request.Arguments[1..] : request.Arguments;

        // an argument the caller did not supply is an omitted Optional: the implementation's own default
        // stands in, which for a VBTypedValue parameter is null - the "Missing" its signature declares. The
        // interpreter itself never leaves one out: it fills an omitted Optional with the parameter's default
        // value - Empty for a Variant, and a typed one's own type's default, such as False, never null - so an
        // implementation reads null and Empty alike as omitted, and cannot tell a typed one from its default.
        if (supplied.Length > parameters.Length)
        {
            return false;
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            if (index >= supplied.Length)
            {
                arguments[index] = parameter.HasDefaultValue ? parameter.DefaultValue : null;
                continue;
            }

            if (!TryMarshal(supplied[index], parameter.ParameterType, out arguments[index]))
            {
                return false;
            }
        }

        return true;
    }

    // the reader gives an instance member an implicit Me at parameter 0, exactly as a workspace class's members have.
    private static bool HasReceiver(VBTypeMemberSymbol member)
        => RuntimeProcedureInvoker.GetParameters(member) is [{ Name: "Me", ParameterKind: ParameterKind.ImplicitByRef }, ..];

    private static bool TryMarshal(IRuntimeValue argument, Type parameterType, out object? marshalled)
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
        marshalled = parameterType == typeof(VBVariantValue) ? Variant(argument) : TypedValue(argument);
        return marshalled is not null && parameterType.IsInstanceOfType(marshalled);
    }

    // a Variant parameter takes anything, that being what a Variant is - MS-VBAL 5.5.1.2.2's Let-coercion to
    // Variant has no failing case. Most of the library declares its parameters that way, so a Variant that
    // rejected an argument would leave most of the library uncallable.
    private static VBVariantValue? Variant(IRuntimeValue argument)
        => TypedValue(argument) is { } typed ? new VBVariantValue(typed) : null;

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
            string value => new VBStringValue(value),
            _ => null,
        },
    };
}
