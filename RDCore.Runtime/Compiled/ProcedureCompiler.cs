using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Instructions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// Compiles the procedures of a program image to IL: each the first time it is called, into one collectible assembly for the image,
/// which goes with it.
/// </summary>
/// <remarks>
/// A body is compiled for the scopes the session binds its names in: a module defined again binds them anew, and the body is compiled again on its next
/// call. A procedure that is not compiled - <see cref="ProcedureAnalysis"/> says why - runs in the interpreter, and so does one whose compiled code the
/// runtime refuses to run, and every one where values are held boxed (<see cref="IneligibilityKind.BoxedEvaluation"/>).
/// </remarks>
internal sealed class ProcedureCompiler
{
    private readonly object _compiling = new();
    private readonly Lazy<ModuleBuilder> _module = new(() =>
    {
        var name = new AssemblyName($"RDCore.Compiled.{Guid.NewGuid():N}");
        return AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule(name.Name!);
    });

    private readonly ConditionalWeakTable<InstructionList, Entry> _bodies = [];
    private readonly ConcurrentDictionary<SemanticId, (VBTypeMemberSymbol Procedure, CompilationResult Result)> _results = [];
    private int _types;
    private long _elapsedTicks;

    private sealed record class Entry(ScopeTree Scopes, CompilationResult Result);

    // what every procedure is where values are held boxed, which is so for the whole process.
    private static readonly CompilationResult Boxed = new(null, [new Ineligibility(IneligibilityKind.BoxedEvaluation, "RDCORE_UNBOXED=0")], TimeSpan.Zero);

    /// <summary>How long compiling the procedures took, all told.</summary>
    public TimeSpan Elapsed => TimeSpan.FromTicks(Interlocked.Read(ref _elapsedTicks));

    /// <summary>Each procedure that was compiled, or found not to be, by its identity: the last time it was.</summary>
    public IEnumerable<(VBTypeMemberSymbol Procedure, CompilationResult Result)> Results => _results.Values;

    /// <summary>
    /// The compiled body of <paramref name="procedure"/>, whose body is <paramref name="list"/>, compiled now when it was not for the session's scopes.
    /// </summary>
    /// <param name="session">The session the procedure is called in.</param>
    /// <param name="evaluator">What binds the expressions of the body, and folds its constants.</param>
    /// <param name="procedure">The procedure.</param>
    /// <param name="list">Its body.</param>
    public CompilationResult Compile(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, VBTypeMemberSymbol procedure, InstructionList list)
    {
        var scopes = session.Symbols.Scopes;
        if (_bodies.TryGetValue(list, out var known) && ReferenceEquals(known.Scopes, scopes))
        {
            return known.Result;
        }

        CompilationResult result;
        Exception? failure = null;
        lock (_compiling)
        {
            if (_bodies.TryGetValue(list, out known) && ReferenceEquals(known.Scopes, scopes))
            {
                return known.Result;
            }

            // compiled code works on payloads: where the process holds its values boxed, no procedure is compiled, and each says why, once for its body.
            if (!UnboxedEvaluation.IsEnabled)
            {
                _bodies.AddOrUpdate(list, new Entry(scopes, Boxed));
                _results[procedure.SemanticId] = (procedure, Boxed);
                return Boxed;
            }

            var stopwatch = Stopwatch.StartNew();
            CompiledBody? body;
            IReadOnlyList<Ineligibility> reasons;
            try
            {
                var analysis = ProcedureAnalysis.Of(session, evaluator, procedure, list);
                body = analysis.Reasons.Count == 0 ? Emit(analysis, procedure) : null;
                reasons = analysis.Reasons;
            }
            catch (Exception exception)
            {
                // a failure to compile a procedure is the compiler's defect, never the program's - a cancellation too, as compiling is told of none: the
                // procedure runs in the interpreter, as one that is not compiled does, and the failure is reported.
                body = null;
                reasons = [new Ineligibility(IneligibilityKind.Refused, $"{exception.GetType().Name}: {exception.Message}")];
                failure = exception;
            }

            stopwatch.Stop();

            result = new CompilationResult(body, reasons, stopwatch.Elapsed);
            _ = Interlocked.Add(ref _elapsedTicks, stopwatch.Elapsed.Ticks);
            _bodies.AddOrUpdate(list, new Entry(scopes, result));
            _results[procedure.SemanticId] = (procedure, result);
        }

        if (failure is not null)
        {
            Report(procedure, failure);
        }

        return result;
    }

    // what is told of a failure is told once it is the procedure's result, and what is told of it cannot fail the call.
    private void Report(VBTypeMemberSymbol procedure, Exception failure)
    {
        try
        {
            Refused?.Invoke(procedure, failure);
        }
        catch (Exception)
        {
            // a handler that fails has no one to tell: the procedure runs in the interpreter all the same.
        }
    }

    /// <summary>
    /// Raised when a procedure could not be compiled because the compiler failed - its analysis, its IL, its type, or the runtime's compilation of the IL:
    /// the procedure runs in the interpreter, and its reason is <see cref="IneligibilityKind.Refused"/>. Whoever hosts the session logs it.
    /// </summary>
    public event Action<VBTypeMemberSymbol, Exception>? Refused;

    // a type of its own for each procedure, created as soon as its one method is written: what is created is what can be called. The IL is compiled now,
    // rather than at the first call, so that code the runtime refuses is refused here.
    private CompiledBody Emit(ProcedureAnalysis analysis, VBTypeMemberSymbol procedure)
    {
        var type = _module.Value.DefineType($"P{++_types}.{procedure.Name}", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);
        var method = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(RuntimeExecutionOutcome), [typeof(CompiledActivation)]);
        ProcedureEmitter.Emit(analysis, method);

        var run = type.CreateType().GetMethod("Run") ?? throw new MissingMethodException(type.Name, "Run");
        RuntimeHelpers.PrepareMethod(run.MethodHandle);
        return new CompiledBody(run.CreateDelegate<Func<CompiledActivation, RuntimeExecutionOutcome>>(), analysis.ResultKind, analysis.Loops);
    }
}

/// <summary>
/// What runs the procedures of a session when it is asked to run compiled code, whatever its environment says: the test suite runs every procedure
/// compiled this way, to compare each run with the interpreter's, and holds the procedures it expects to be compiled to it.
/// </summary>
internal static class CompiledExecution
{
    /// <summary>
    /// The engine every session runs its procedures with; <see langword="null"/> for the one its environment says. The test assembly sets it from the
    /// <c>RDCORE_ENGINE</c> environment variable; nothing else does.
    /// </summary>
    public static VBExecutionEngine? Engine { get; set; }

    private static readonly AsyncLocal<VBExecutionEngine?> _scoped = new();

    /// <summary>
    /// The engine a session whose environment says <paramref name="configured"/> runs its procedures with: the one a scope says (<see cref="Running"/>), then
    /// the one every session is told (<see cref="Engine"/>), then its own.
    /// </summary>
    public static VBExecutionEngine EngineFor(VBExecutionEngine configured) => _scoped.Value ?? Engine ?? configured;

    /// <summary>Runs what follows, in this asynchronous flow, with <paramref name="engine"/>, until the scope is disposed of: a test that compares the two.</summary>
    public static IDisposable Running(VBExecutionEngine engine)
    {
        var previous = _scoped.Value;
        _scoped.Value = engine;
        return new Scope(() => _scoped.Value = previous);
    }

    /// <summary>
    /// Whether every compiled activation is handed to the interpreter at a safepoint chosen at random - any instruction boundary, a different one for each
    /// activation - whether or not anything asks for it: what the suite runs under to show that what a walk-back writes into the frame is all the
    /// interpreter needs, wherever it happens. The test assembly sets it from the <c>RDCORE_WALKBACK=random</c> environment variable; nothing else does.
    /// </summary>
    public static bool WalksBackAtRandom { get; set; }

    private static readonly AsyncLocal<bool> _strict = new();

    /// <summary>
    /// Whether a procedure that is not compiled fails its call, rather than running in the interpreter: what a test that expects its procedures to be compiled
    /// runs them under, so that what stops one compiling cannot go unnoticed.
    /// </summary>
    public static bool IsStrict => _strict.Value;

    /// <summary>Runs what follows, in this asynchronous flow, strictly (<see cref="IsStrict"/>), until the scope is disposed of.</summary>
    public static IDisposable Strictly()
    {
        var previous = _strict.Value;
        _strict.Value = true;
        return new Scope(() => _strict.Value = previous);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
