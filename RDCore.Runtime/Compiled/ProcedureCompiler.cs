using RDCore.Runtime.Execution;
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
/// Compiles the procedures of a program image to IL (IR lowering §9.2): each the first time it is called, into one collectible assembly for the image,
/// which goes with it.
/// </summary>
/// <remarks>
/// A body is compiled for the scopes the session binds its names in: a module defined again binds them anew, and the body is compiled again on its next
/// call. A procedure that is not compiled - <see cref="ProcedureAnalysis"/> says why - runs in the interpreter, and so does one whose compiled code the
/// runtime refuses to run.
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

        lock (_compiling)
        {
            if (_bodies.TryGetValue(list, out known) && ReferenceEquals(known.Scopes, scopes))
            {
                return known.Result;
            }

            var stopwatch = Stopwatch.StartNew();
            var analysis = ProcedureAnalysis.Of(session, evaluator, procedure, list);
            Ineligibility? refusal = null;
            var body = analysis.Reasons.Count == 0 ? Emit(analysis, procedure, out refusal) : null;
            IReadOnlyList<Ineligibility> reasons = analysis.Reasons.Count > 0 ? analysis.Reasons : body is null ? [refusal!] : [];
            stopwatch.Stop();

            var result = new CompilationResult(body, reasons, stopwatch.Elapsed);
            _ = Interlocked.Add(ref _elapsedTicks, stopwatch.Elapsed.Ticks);
            _bodies.AddOrUpdate(list, new Entry(scopes, result));
            _results[procedure.SemanticId] = (procedure, result);
            return result;
        }
    }

    // a type of its own for each procedure, created as soon as its one method is written: what is created is what can be called.
    private CompiledBody? Emit(ProcedureAnalysis analysis, VBTypeMemberSymbol procedure, out Ineligibility? refusal)
    {
        refusal = null;
        var type = _module.Value.DefineType($"P{++_types}.{procedure.Name}", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);
        var method = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(RuntimeExecutionOutcome), [typeof(CompiledActivation)]);
        ProcedureEmitter.Emit(analysis, method);

        var run = type.CreateType().GetMethod("Run")!;
        try
        {
            // the IL is compiled now, rather than at the first call: code the runtime refuses is the interpreter's, and the call made as if nothing was compiled.
            RuntimeHelpers.PrepareMethod(run.MethodHandle);
        }
        catch (InvalidProgramException exception)
        {
            refusal = new Ineligibility(IneligibilityKind.Instruction, $"the runtime refuses the compiled code: {exception.Message}");
            return null;
        }

        return new CompiledBody(run.CreateDelegate<Func<CompiledActivation, RuntimeExecutionOutcome>>(), analysis.ResultKind, analysis.Loops);
    }
}

/// <summary>
/// What runs the procedures of a session when it is asked to run compiled code, whatever its environment says: the test suite runs every procedure
/// compiled this way (IR lowering §9.2, the differential runs), and holds the procedures it expects to be compiled to it.
/// </summary>
internal static class CompiledExecution
{
    /// <summary>The engine every session runs its procedures with; <see langword="null"/> for the one its environment says.</summary>
    public static VBExecutionEngine? Engine { get; set; } = Environment.GetEnvironmentVariable("RDCORE_ENGINE") is { } engine
        && Enum.TryParse<VBExecutionEngine>(engine, ignoreCase: true, out var parsed) ? parsed : null;

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
