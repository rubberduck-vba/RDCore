using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Hosting.Json;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Hosting;
using RDCore.SDK.Runtime.Shared;
using System.Globalization;
using System.Text.Json.Nodes;

namespace RDCore.Runtime.Support.Host;

// a machine of a program: the session of one provider, which runs one invocation at a time, by name, with arguments and a result as JSON (JsonValues).
// Composing a pipeline makes the session's lifecycle its own (ClassLifecycle), so one is composed only while nothing runs: an invocation or a reset that
// finds another running is answered without one. ownsSession: whether disposing the machine disposes the provider. A machine disposed while it runs an
// invocation or a reset lets go of its session once that ends, not under it.
internal sealed class ProgramMachine(EnvironmentSessionProvider provider, IErrorDetailsRecorder errorDetails, bool ownsSession) : IProgramMachine
{
    private int _busy;
    private int _disposed;

    public async Task<ProgramInvocationResult> InvokeAsync(ProgramInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            return ProgramInvocationResult.Ended(ExecutionOutcome.Refused, Resources.Host_AProgramIsRunning);
        }

        try
        {
            var session = provider.Session;
            if (ProcedureOf(session, invocation, out var explanation) is not { } procedure)
            {
                return ProgramInvocationResult.Ended(ExecutionOutcome.NotFound, explanation);
            }

            var pipeline = RuntimeExecutionPipeline.Create(session, provider.Image, errorDetails, cancellationToken, outside: provider.Outside);
            JsonNode? value = null;
            var result = await provider.Execution.InvokeAsync(
                pipeline, procedure, bound => Bind(bound, pipeline, procedure, invocation.Arguments),
                returned => new JsonValues(session, session.Symbols.Resolver).ToJson(returned, out value), cancellationToken);

            return result.Outcome switch
            {
                ExecutionOutcome.Completed when procedure is VBReturningMemberSymbol => ProgramInvocationResult.Returned(value),
                ExecutionOutcome.Completed => ProgramInvocationResult.Completed(),
                ExecutionOutcome.RuntimeError => ProgramInvocationResult.RuntimeError(result.ErrorNumber, result.ErrorMessage, result.ErrorCode),
                ExecutionOutcome.Halted => ProgramInvocationResult.Ended(ExecutionOutcome.Halted, Resources.Host_TheProgramEnded),
                var other => ProgramInvocationResult.Ended(other, result.ErrorMessage),
            };
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
            ReleaseIfDisposed();
        }
    }

    public async Task<bool> ResetAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            return false;
        }

        try
        {
            var pipeline = RuntimeExecutionPipeline.Create(provider.Session, provider.Image, errorDetails, cancellationToken, outside: provider.Outside);
            var reset = await provider.Execution.ResetAsync(pipeline, cancellationToken);

            // an error a Terminate left unhandled ends that Terminate, and the reset goes on: the machine is as it was made either way.
            return reset.Outcome is ExecutionOutcome.Completed or ExecutionOutcome.RuntimeError;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
            ReleaseIfDisposed();
        }
    }

    public ValueTask DisposeAsync()
    {
        Volatile.Write(ref _disposed, 1);
        ReleaseIfDisposed();
        return ValueTask.CompletedTask;
    }

    // once the machine is disposed, whichever comes last, the dispose or the end of what runs, lets go of the session, only once: it holds the machine as
    // busy for good, so that nothing runs in the session after.
    private void ReleaseIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0 && Interlocked.CompareExchange(ref _busy, 1, 0) == 0 && ownsSession)
        {
            provider.Dispose();
        }
    }

    // a Public (or implicitly public) Sub, Function or Property Get of a standard module: a member of a class needs an object, and a Declare is not code
    // the program wrote.
    private static VBTypeMemberSymbol? ProcedureOf(IRuntimeSession session, ProgramInvocation invocation, out string explanation)
    {
        explanation = string.Empty;
        if (!session.Symbols.TryGetModule(invocation.Module, out var module) || module is not VBStandardModuleSymbol)
        {
            explanation = string.Format(CultureInfo.CurrentCulture, Resources.Host_NoSuchModule, invocation.Module);
            return null;
        }

        var procedure = session.Symbols.MembersOf(module.Uri).FirstOrDefault(member =>
            member.Name.Equals(invocation.Procedure, StringComparison.OrdinalIgnoreCase)
            && member.AccessModifier is AccessModifier.Public or AccessModifier.Implicit
            && member is VBPropertyGetMemberSymbol
                or VBFunctionMemberSymbol and not VBExternalFunctionMemberSymbol
                or VBProcedureMemberSymbol and not (VBExternalSubMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol));
        if (procedure is null)
        {
            explanation = string.Format(CultureInfo.CurrentCulture, Resources.Host_NoSuchProcedure, module.Name, invocation.Procedure);
        }

        return procedure;
    }

    // the arguments by their parameters' names, as RD-VBA compares names: one naming no parameter is 448, a parameter given none that is not Optional is 449,
    // and a value of no parameter's type is the error its conversion is (13, or 94 for null). An Optional one given none is what a call that omits it passes;
    // a ParamArray is given nothing. Two names that differ only by case are one name given twice: 448 as well.
    private static (IRuntimeValue[]? Arguments, IVBRaisableError? Refusal) Bind(
        IRuntimeSession session, RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol procedure, IReadOnlyDictionary<string, JsonNode?> arguments)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(procedure);
        var named = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, argument) in arguments)
        {
            if (!parameters.Any(parameter => parameter is not ParamArrayParameterSymbol && parameter.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return (null, Refused(VBRuntimeErrorId.NamedArgumentNotFound, Verbose(Resources.Host_NamedArgumentNotFound_Verbose, procedure.Name, name)));
            }

            if (!named.TryAdd(name, argument))
            {
                return (null, Refused(VBRuntimeErrorId.NamedArgumentNotFound, Verbose(Resources.Host_ArgumentGivenTwice_Verbose, name)));
            }
        }

        var values = new JsonValues(session, session.Symbols.Resolver);
        var bound = new IRuntimeValue[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            if (parameter is ParamArrayParameterSymbol)
            {
                bound[index] = new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(new VBFixedSizeArrayValue([(0, -1)])));
            }
            else if (named.TryGetValue(parameter.Name, out var argument))
            {
                if (values.FromJson(argument, parameter.ResolvedType, out var value) is { } refusal)
                {
                    return (null, refusal);
                }

                bound[index] = value!.RuntimeValue;
            }
            else if (parameter.IsOptional && pipeline.Expressions.OmittedArgument(session, parameter) is { IsSuccess: true, Result: { } omitted })
            {
                bound[index] = omitted.RuntimeValue;
            }
            else
            {
                return (null,
                    Refused(VBRuntimeErrorId.ArgumentNotOptional, Verbose(Resources.Host_ArgumentNotOptional_Verbose, parameter.Name, procedure.Name)));
            }
        }

        return (bound, null);
    }

    private static VBRuntimeErrorInfo Refused(VBRuntimeErrorId id, string verbose) => VBRuntimeErrorInfo.For(id, default, verbose);

    private static string Verbose(string format, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, format, arguments);
}
