using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// Compiles the procedures of a program image to IL: each the first time it is called, into one collectible assembly for the image,
/// which goes with it.
/// </summary>
/// <remarks>
/// A body is compiled for the scopes the session binds its names in: a module defined again binds them anew, and the body is compiled again on its next
/// call. A procedure that is not compiled - <see cref="ProcedureAnalysis"/> says why - runs in the interpreter, and so does one whose compiled code the
/// runtime refuses to run, and every one where values are held boxed (<see cref="IneligibilityKind.BoxedEvaluation"/>).
/// <para>
/// A compiler given a <see cref="PersistTarget"/> writes the procedures into the assembly a build saves instead, where nothing is run; one given the procedures
/// of an assembly a build saved (<see cref="Use"/>) runs a procedure that was compiled into it as it was, when it is the same procedure, and compiles the
/// others as their first call does.
/// </para>
/// </remarks>
/// <param name="persist">The assembly the procedures are written into, for a build; <see langword="null"/> for a compiler whose procedures are run.</param>
internal sealed class ProcedureCompiler(PersistTarget? persist = null)
{
    private readonly object _compiling = new();
    private readonly Lazy<ModuleBuilder> _module = new(() =>
    {
        if (persist is not null)
        {
            return persist.Module;
        }

        var name = new AssemblyName($"RDCore.Compiled.{Guid.NewGuid():N}");
        return AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule(name.Name!);
    });

    private PrebuiltProcedures? _prebuilt;
    private int _bound;

    /// <summary>
    /// Runs the procedures compiled into an assembly a build saved, each where it is the procedure that was compiled: what a built program's session runs.
    /// </summary>
    public void Use(PrebuiltProcedures prebuilt) => _prebuilt = prebuilt;

    /// <summary>
    /// How many procedures run the code a build compiled them to (<see cref="Use"/>), rather than code compiled at their first call.
    /// </summary>
    public int Bound => Volatile.Read(ref _bound);

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
            (result, failure) = UnboxedEvaluation.IsEnabled ? Make(session, evaluator, procedure, list) : (Boxed, null);
            _bodies.AddOrUpdate(list, new Entry(scopes, result));
            _results[procedure.SemanticId] = (procedure, result);
        }

        if (failure is not null)
        {
            Report(procedure, failure);
        }

        return Told(procedure, result);
    }

    // the procedure compiled, or found not to be: what is made of it, and the compiler's failure if it failed.
    private (CompilationResult Result, Exception? Failure) Make(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, VBTypeMemberSymbol procedure, InstructionList list)
    {
        // a build writes into the assembly it saves what compiles where it runs: a procedure the compiler fails on, or whose code the runtime refuses, would
        // leave a type the assembly cannot be saved with, and runs in the interpreter, as in any session.
        if (persist?.Validate(session, evaluator, procedure, list) is { Body: null } refused)
        {
            return (refused, null);
        }

        var stopwatch = Stopwatch.StartNew();
        CompiledBody? body;
        IReadOnlyList<Ineligibility> reasons;
        IReadOnlyList<Ineligibility> stepped = [];
        Exception? failure = null;
        _ = _inProgress.Add(procedure.SemanticId);
        try
        {
            var analysis = ProcedureAnalysis.Of(session, evaluator, procedure, list);
            body = analysis.Reasons.Count == 0 ? Bind(session, evaluator, analysis, procedure, list) ?? Emit(session, evaluator, analysis, procedure, list) : null;
            reasons = analysis.Reasons;
            stepped = body is null ? [] : analysis.Stepped;
        }
        catch (Exception exception)
        {
            // a failure to compile a procedure is the compiler's defect, never the program's - a cancellation too, as compiling is told of none: the
            // procedure runs in the interpreter, as one that is not compiled does, and the failure is reported.
            body = null;
            reasons = [new Ineligibility(IneligibilityKind.Refused, $"{exception.GetType().Name}: {exception.Message}")];
            failure = exception;
        }
        finally
        {
            _ = _inProgress.Remove(procedure.SemanticId);
        }

        stopwatch.Stop();
        _ = Interlocked.Add(ref _elapsedTicks, stopwatch.Elapsed.Ticks);
        return (new CompilationResult(body, reasons, stopwatch.Elapsed) { Stepped = stepped }, failure);
    }

    // the result made, told to the Compiled handlers: as a failure is told, what is told of a result cannot fail the call.
    private CompilationResult Told(VBTypeMemberSymbol procedure, CompilationResult result)
    {
        try
        {
            Compiled?.Invoke(procedure, result);
        }
        catch (Exception)
        {
        }

        return result;
    }

    /// <summary>
    /// Raised when a procedure was compiled, or found not to be, for the session's scopes: once for each time its <see cref="CompilationResult"/> is made, on
    /// the thread of the call that made it, before the call goes on.
    /// </summary>
    public event Action<VBTypeMemberSymbol, CompilationResult>? Compiled;

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

    // the procedures being compiled, by identity: a callee that is one of them - a procedure that calls the one that calls it - is not compiled for its caller
    // before it, and is called through the invoker (DirectCall).
    private readonly HashSet<SemanticId> _inProgress = [];

    // a type of its own for each procedure, created as soon as its methods are written: what is created is what can be called. Two methods: the typed entry
    // (Body), which a compiled caller calls with the arguments as it holds them, and Run, which reads them from the frame's cells, as an interpreted caller
    // gives them, and calls it. The argument of a parameter passed by reference is a cell, which the IL holds as an object it only hands back to
    // CompiledActivation.
    private CompiledBody Emit(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, ProcedureAnalysis analysis, VBTypeMemberSymbol procedure, InstructionList list)
    {
        // what only a build does, done before the type is defined: a type of the assembly a build saves that is never created fails the save of all of them.
        var lines = persist?.LinesOf(list);
        var type = _module.Value.DefineType($"P{++_types}.{procedure.Name}", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);
        var entry = type.DefineMethod("Body", MethodAttributes.Public | MethodAttributes.Static, typeof(RuntimeExecutionOutcome), ParametersOf(analysis));
        var method = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(RuntimeExecutionOutcome), [typeof(CompiledActivation)]);

        var directs = analysis.Callees.Select(callee => Link(session, evaluator, procedure, entry, list, callee, analysis.NeedsFrame)).ToArray();
        ProcedureEmitter.Emit(analysis, method, entry, directs, lines);

        // a type of the assembly a build saves is created to be saved, and runs only once it is loaded: its typed entry is what its compiled callers call.
        if (persist is not null)
        {
            _ = type.CreateType();
            persist.Add(new PersistedProcedure(type.FullName!, procedure.SemanticId.ToString(), FingerprintOf(persist.FingerprintOf(procedure.SemanticId), analysis, session.Environment.Is64Bit),
                [.. directs.Select(direct => direct is null ? string.Empty : direct.Entry.DeclaringType!.FullName!)]));
            return Finish(NotRun, entry, entry, analysis, procedure, directs);
        }

        var created = type.CreateType();
        var run = created.GetMethod("Run") ?? throw new MissingMethodException(type.Name, "Run");
        var body = created.GetMethod("Body") ?? throw new MissingMethodException(type.Name, "Body");
        return Finish(Prepared(run, body), body, entry, analysis, procedure, directs);
    }

    // the type a build compiled the procedure to, when it is this procedure still and the procedures it calls directly are the ones it called: run as it was
    // compiled. The calls its code makes are those it was compiled with, so a callee it called through the invoker is called that way still; one it called
    // directly must be bound too, or the procedure is compiled again as at its first call.
    private CompiledBody? Bind(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, ProcedureAnalysis analysis, VBTypeMemberSymbol procedure, InstructionList list)
    {
        if (_prebuilt?.Find(procedure) is not { } built || built.Links.Count != analysis.Callees.Count
            || _prebuilt.FingerprintOf(procedure.SemanticId) is not { } bodyFingerprint || FingerprintOf(bodyFingerprint, analysis, session.Environment.Is64Bit) != built.Fingerprint
            || built.Type.GetMethod("Run") is not { } run || built.Type.GetMethod("Body") is not { } body
            || !body.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(ParametersOf(analysis)))
        {
            return null;
        }

        var directs = new DirectCall?[analysis.Callees.Count];
        for (var callee = 0; callee < directs.Length; callee++)
        {
            if (built.Links[callee].Length == 0)
            {
                continue;
            }

            if (Link(session, evaluator, procedure, body, list, analysis.Callees[callee], analysis.NeedsFrame) is not { } direct
                || direct.Entry.Module != built.Type.Module || direct.Entry.DeclaringType?.FullName != built.Links[callee])
            {
                return null;
            }

            directs[callee] = direct;
        }

        var bound = Finish(Prepared(run, body), body, body, analysis, procedure, directs);
        _ = Interlocked.Increment(ref _bound);
        return bound;
    }

    // what the code of a procedure is compiled from: its body (ProgramImage.ProcedureFingerprints), and what the analysis made of it for the session's scopes
    // (the kinds of its slots and its result, its steps, its frame, the procedures it calls), which a library that resolves otherwise, or a #If whose branch
    // differs, changes when the body does not. The size of a pointer in the session's environment is part of it too, since a LongPtr is an
    // integer of that size (MS-VBAL §2.2): code built for one environment never runs in the other, whatever else the analysis made of it.
    private static string FingerprintOf(string body, ProcedureAnalysis analysis, bool is64Bit)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(object? text) => hash.AppendData(Encoding.UTF8.GetBytes($"{text}\n"));

        Append(body);
        Append(is64Bit ? "pointer:64" : "pointer:32");
        Append($"{analysis.ResultKind}|{analysis.ResultIsVariant}|{analysis.ResultIsText}|{analysis.Length}|{analysis.HandlesErrors}|{analysis.HasForEach}|{analysis.NeedsFrame}|{analysis.MeSlot}|{analysis.EntersWithoutFrame}");
        Append(string.Join(',', analysis.Parameters.Select(parameter => $"{parameter.Slot}:{parameter.Kind}:{parameter.ByReference}")));
        Append(string.Join(',', analysis.CellSlots));
        Append(string.Join(',', analysis.SlotSymbols.OrderBy(slot => slot.Key).Select(slot => $"{slot.Key}:{slot.Value.SemanticId}")));
        Append(string.Join(',', analysis.Loops.Select(loop => $"{loop.Opener}:{loop.Kind}")));
        Append(string.Join(',', analysis.Selects.Select(select => $"{select.Opener}:{select.End}:{select.Kind}")));
        foreach (var step in analysis.Steps)
        {
            Append(step);
        }

        foreach (var callee in analysis.Callees)
        {
            Append($"{callee.Procedure.SemanticId}|{string.Join(',', callee.Parameters)}|{callee.Result}|{callee.CallsOwnMember}");
        }

        foreach (var library in analysis.Libraries)
        {
            Append($"{library.Function.SemanticId}|{library.Parameters.Length}");
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // the typed entry's parameters: the compiled activation, then each parameter as compiled code holds it.
    private static Type[] ParametersOf(ProcedureAnalysis analysis)
        => [typeof(CompiledActivation), .. analysis.Parameters.Select(parameter => parameter.ByReference ? typeof(object) : ProcedureEmitter.ClrTypeOf(parameter.Kind))];

    // the IL of both methods compiled now, rather than at the first call, so that code the runtime refuses is refused here.
    private static Func<CompiledActivation, RuntimeExecutionOutcome> Prepared(MethodInfo run, MethodInfo body)
    {
        RuntimeHelpers.PrepareMethod(run.MethodHandle);
        RuntimeHelpers.PrepareMethod(body.MethodHandle);
        return run.CreateDelegate<Func<CompiledActivation, RuntimeExecutionOutcome>>();
    }

    // what a body a build saves is until it is loaded: nothing runs it.
    private static RuntimeExecutionOutcome NotRun(CompiledActivation activation)
        => throw new InvalidOperationException("a procedure compiled into the assembly a build saves runs once the assembly is loaded");

    private static CompiledBody Finish(Func<CompiledActivation, RuntimeExecutionOutcome> run, MethodInfo body, MethodInfo self, ProcedureAnalysis analysis,
        VBTypeMemberSymbol procedure, DirectCall?[] directs)
    {
        var compiled = new CompiledBody(run, analysis.ResultKind, analysis.Loops, procedure.Name, analysis.Callees, directs, analysis.SlotSymbols)
        {
            Entry = body, CellSlots = analysis.CellSlots, Fields = analysis.Fields, NeedsFrame = analysis.NeedsFrame, Selects = analysis.Selects,
            Classes = analysis.Classes, EntersWithoutFrame = analysis.EntersWithoutFrame, MeSlot = analysis.MeSlot ?? -1, MemberFields = analysis.MemberFields,
            Libraries = analysis.Libraries,
        };

        // a call the body makes of itself is of the body just made.
        foreach (var direct in directs)
        {
            if (direct is not null && ReferenceEquals(direct.Entry, self))
            {
                direct.Body = compiled;
            }
        }

        return compiled;
    }

    // how the body calls callee: directly through its typed entry when the callee is compiled - for the same scopes, before its caller - or is the body
    // itself; through the invoker when it is not compiled, is not run by the invoker the body's own calls are, or is being compiled already (a procedure that
    // calls the one that calls it).
    // A callee whose frame holds what compiled code does not (ProcedureAnalysis.NeedsFrame) is called through the invoker, which makes it its frame; so is
    // one given a String, which its frame holds.
    private DirectCall? Link(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, VBTypeMemberSymbol caller, MethodInfo entry, InstructionList list,
        CompiledCallee callee, bool callerNeedsFrame)
    {
        // a member of an object, Me's own included, is called through its binding, the object given as Me (CompiledActivation.CallMember).
        if (callee.Site is not null || callee.CallsOwnMember || callee.Parameters.Contains(null) || evaluator.ScalarInvokerOf(callee.Procedure) is not { } invoker)
        {
            return null;
        }

        if (callee.Procedure.SemanticId == caller.SemanticId)
        {
            return callerNeedsFrame ? null : new DirectCall(entry, callee.Procedure, list);
        }

        if (_inProgress.Contains(callee.Procedure.SemanticId) || invoker.BodyOf(callee.Procedure) is not { } calleeList
            || Compile(session, evaluator, callee.Procedure, calleeList).Body is not { Entry: { } calleeEntry, NeedsFrame: false } calleeBody)
        {
            return null;
        }

        return new DirectCall(calleeEntry, callee.Procedure, calleeList) { Body = calleeBody };
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

    /// <summary>
    /// Whether each compiled activation fails one access of its frame chosen at random - a variable read back after the interpreter ran an instruction, or
    /// written into the frame before it does - as compiled code fails one when the frame holds a variable where it cannot read or write it: the read hands
    /// the rest of the activation to the interpreter, and the write, made all the same, has the interpreter run the activation from the instruction instead
    /// of that instruction alone. The first read-back after a call the program waited in fails too, whatever the seed
    /// (<see cref="CompiledActivation.ReadsBackAfterCall"/>). The frame holds what it always does, so a program runs the same; what the suite runs under to
    /// show that each of those hand-overs leaves the interpreter what it needs. The test assembly sets it from <c>RDCORE_RELOADS=fail-at-random</c>; nothing
    /// else does.
    /// </summary>
    public static bool FailsReloadsAtRandom { get; set; }

    /// <summary>
    /// Whether a compiled activation may be handed to the interpreter where nothing asks for it (<see cref="WalksBackAtRandom"/>,
    /// <see cref="FailsReloadsAtRandom"/>): how many times compiled code recovers or walks back is then not the program's.
    /// </summary>
    public static bool HandsOverAtRandom => WalksBackAtRandom || FailsReloadsAtRandom;

    /// <summary>
    /// What the random walk-back's safepoints, and the random failed accesses, are chosen with (<see cref="CompiledBody.NextRandomSafepoint"/>,
    /// <see cref="CompiledBody.NextRandomFailedAccess"/>): a run that fails is replayed with the same seed. The test assembly sets it from
    /// <c>RDCORE_WALKBACK_SEED</c>, and to 1 when that is not set.
    /// </summary>
    public static int WalkBackSeed { get; set; } = 1;

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
