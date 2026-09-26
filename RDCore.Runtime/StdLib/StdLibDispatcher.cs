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
    /// 🚧 TODO as each module lands: <c>Conversion</c>, <c>Strings</c>, <c>Math</c>, <c>DateTime</c>,
    /// <c>Interaction</c>, <c>Collection</c>, <c>RegExp</c>, and the constant modules.
    /// </para>
    /// </remarks>
    /// <param name="session">The session the implementations read their state from.</param>
    public static StdLibDispatcher For(IRuntimeSession session)
        => new(new Dictionary<Type, object>
        {
            [typeof(IStdInformationModule)] = new StdInformation(session),
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

        // an argument the caller did not supply is an omitted Optional: the implementation's own default
        // stands in, which for a VBTypedValue parameter is null - the "Missing" its signature declares.
        if (request.Arguments.Length > parameters.Length)
        {
            return false;
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            if (index >= request.Arguments.Length)
            {
                arguments[index] = parameter.HasDefaultValue ? parameter.DefaultValue : null;
                continue;
            }

            if (!TryMarshal(request.Arguments[index], parameter.ParameterType, out arguments[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryMarshal(IRuntimeValue argument, Type parameterType, out object? marshalled)
    {
        marshalled = null;

        if (parameterType.IsEnum)
        {
            marshalled = Enum.ToObject(parameterType, Convert.ToInt64(argument.BoxedValue));
            return true;
        }

        // a VBTypedValue argument is already the shape the signature asks for, unless it is the wrong one -
        // which is a coercion the caller was supposed to have done, not something to do quietly here.
        marshalled = argument.BoxedValue as VBTypedValue ?? WrappedValue(argument, parameterType);
        return marshalled is not null && parameterType.IsInstanceOfType(marshalled);
    }

    // a runtime value that is not itself a VBTypedValue still has to reach a typed parameter, and the type
    // the signature names is the one that knows how to hold it.
    private static VBTypedValue? WrappedValue(IRuntimeValue argument, Type parameterType)
        => parameterType == typeof(VBStringValue) && argument.BoxedValue is string text ? new VBStringValue(text)
            : parameterType == typeof(VBLongValue) && argument.BoxedValue is not null ? new VBLongValue(Convert.ToInt32(argument.BoxedValue))
            : null;
}
