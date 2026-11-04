using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK;
using System.Collections.Concurrent;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Semantics.Static;
using System.Collections.Immutable;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The <strong>RDCore.Runtime</strong> implementation of <see cref="IProcedureInvoker"/>: a <c>Sub</c>,
/// <c>Function</c> or <c>Property Get</c>, same session, no <c>Me</c>. Parameters are bound <c>ByVal</c>
/// (a fresh copy) or <c>ByRef</c> (a real alias onto the caller's own storage, per
/// <strong>MS-VBAL §5.3.1.11</strong>) depending on <see cref="VBParameterSymbol.ParameterKind"/> and
/// whether <see cref="RuntimeExpressionEvaluator"/> could resolve the argument's own address; a
/// <c>Function</c>/<c>Property Get</c> returns the data value of its own function result variable
/// (<strong>MS-VBAL §5.3.1</strong>) rather than <c>Void</c>.
/// </summary>
/// <remarks>
/// One instance per session, matching <see cref="IProcedureInvoker"/>'s own xmldoc ("what a runtime
/// provides... once, for the procedures of a session") — <paramref name="Session"/> is held directly
/// rather than derived from <see cref="ISymbolResolver"/> (<c>Invoke</c>'s own parameter), since
/// <see cref="ISymbolResolver"/> has no path back to <see cref="IRuntimeSession.CallStack"/> or
/// <see cref="ISessionSymbols.CreateFrame"/>. <paramref name="Bodies"/> is the symbol-to-body bridge
/// nothing else in the platform builds yet: production workspace loading has no step that lowers every
/// procedure body and remembers the result keyed by its own symbol, so today this is populated by
/// whoever composes the session (a test, for now) — a real host's own equivalent step is future work,
/// not this slice's.
/// </remarks>
/// <param name="Session">The session this invoker runs every call against.</param>
/// <param name="Bodies">Every procedure's own lowered body, keyed by its <see cref="Symbol.SemanticId"/>.</param>
/// <param name="Executor">The interpreter that steps through a called procedure's own instructions.</param>
public sealed class RuntimeProcedureInvoker(IRuntimeSession Session, IReadOnlyDictionary<SemanticId, InstructionList> Bodies, ProcedureExecutor Executor) : IProcedureInvoker
{
    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Invoke(VBTypeMemberSymbol procedure, ISymbolResolver resolver, IRuntimeValue[] arguments)
        => Activate(procedure, arguments, [], sweep: false);

    /// <summary>
    /// Invokes <paramref name="procedure"/>, every parameter of which is a scalar passed by value, with <paramref name="arguments"/> - payloads of the
    /// parameters' types, in order: each is what the parameter's cell is made of (IR lowering S1.3), and no value is made of it.
    /// </summary>
    internal RuntimeSemanticsEvaluationResult InvokeScalars(VBTypeMemberSymbol procedure, ReadOnlySpan<ScalarPayload> arguments)
        => Activate(procedure, null, arguments, sweep: false);

    /// <summary>
    /// Evaluates every instruction of <paramref name="procedure"/> once, as the entry point of an analysis (<see cref="ProcedureExecutor.Sweep"/>).
    /// </summary>
    /// <param name="procedure">The <c>Sub</c>, <c>Function</c> or property accessor to evaluate.</param>
    /// <returns>Whether every instruction was evaluated.</returns>
    /// <remarks>
    /// The procedure is entered as any procedure is, with a parameter for each of its own, and no caller to say what they hold: each is
    /// its type's default, which an analysis does not trust (<see cref="RuntimeExpressionEvaluator.AssumesVariables"/>). A member of a class is
    /// entered with a <c>Me</c> that is an instance of its class, made for the occasion and not initialized.
    /// </remarks>
    public bool Sweep(VBTypeMemberSymbol procedure)
        => Activate(procedure, [.. GetParameters(procedure).Select(AssumedArgument(procedure))], [], sweep: true).IsSuccess;

    private Func<VBParameterSymbol, IRuntimeValue> AssumedArgument(VBTypeMemberSymbol procedure) => parameter =>
    {
        if (parameter.Name == "Me"
            && Session.Symbols.Resolver.ResolveType(procedure.ParentUri.Fragment.TrimStart('#'), ScopeKind.Global, StaticSymbol.GlobalUri).Symbol is VBClassModuleSymbol classModule)
        {
            var objectId = Session.Objects.CreateObject();
            Session.Symbols.CreateInstance(objectId, classModule);
            return new VBObjectValue(objectId).RuntimeValue;
        }

        // the argument is the caller's, which there is none of: its type's default is what the parameter assumes, and a Variant's says nothing of its subtype.
        return SymbolAddressTable.BoxedValue(parameter.ResolvedType.DefaultValue.AsAssumed());
    };

    // an activation that runs the body (Invoke), or that evaluates every instruction of it once (Sweep). Its arguments are runtime values, or - when
    // there are none of those - payloads, for a procedure whose parameters are all scalars passed by value (InvokeScalars).
    private RuntimeSemanticsEvaluationResult Activate(VBTypeMemberSymbol procedure, IRuntimeValue[]? arguments, ReadOnlySpan<ScalarPayload> payloads, bool sweep)
    {
        if (!_activations.TryGetValue(procedure, out var activation))
        {
            activation = _activations.GetOrAdd(procedure, ActivationOf);
        }

        if (BodyOf(procedure, activation) is not { } body)
        {
            // a member with no body is a real gap in the platform. A member whose code is not the workspace's
            // never reaches here at all: ICallableBindingFactory binds it to an IExternalDispatcher instead,
            // which is where that decision belongs - at the binding, not inside the thing that runs one kind.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var parameters = GetParameters(procedure);
        if (parameters.Length != (arguments?.Length ?? payloads.Length))
        {
            // arity mismatch: a real static-semantics gap (no call-site argument-count check exists yet
            // either), not something this slice invents error handling for.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var frame = (CallStackFrame)Session.Symbols.CreateFrame(activation.NodeId, activation.Symbol, activation.Directives);

        // the first parameter of a member of a class is its Me, and the object the member is a call on is what its
        // fields are the storage of.
        if (parameters is [{ Name: "Me" }, ..] && arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var target }, ..])
        {
            frame.Target = target;
        }

        (frame.Procedure, frame.Body) = (procedure, body);
        frame.LayOut(activation.Layout);

        // a program that starts is not stopped by what a Class_Terminate left unhandled in the last one (ISessionErrorState.Untrappable).
        if (!Session.CallStack.Frames.Any())
        {
            Session.Errors.ClearUntrappable();
        }

        if (!Session.CallStack.TryPush(frame))
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.OutOfStackSpace,
                default, Exceptions.VBProcedureCall_OutOfStackSpace_Verbose));
        }

        if (procedure is VBReturningMemberSymbol returning)
        {
            // MS-VBAL §5.3.1: "each invocation of a function declaration has a distinct function result
            // variable" - seeded to the declared return type's own default, exactly like a fresh Dim,
            // before the body ever runs (a Function that never assigns its own name still returns
            // something well-defined, not an uninitialized slot).
            // A record is seeded field by field, as a declared variable of it is: a fixed-size array field has its dimensions, and the fields are the type's as
            // the session has it now (CurrentTypes).
            frame.ReturnValue = returning.ResolvedType is VBUserDefinedType record && Session.Symbols.Defaults is { } defaults
                ? new VBUserDefinedTypeValue(CurrentTypes.Of(Session.Symbols.Resolver, record), defaults)
                : returning.ResolvedType.DefaultValue;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (arguments is null)
            {
                frame.PushPayload(parameter, payloads[i], activation.Layout.ParameterSlots[i]);
            }
            else if (IsAliased(parameter, arguments[i]) && arguments[i] is CellReference shared)
            {
                // a variable of the caller held in a cell, which the parameter is a second name for: the parameter's slot holds the very cell (S1.3), so
                // what one writes the other reads, as through an address - with no address made, and none looked up.
                frame.PushAlias(parameter, shared.Cell, activation.Layout.ParameterSlots[i]);
            }
            else if (IsAliased(parameter, arguments[i]) && arguments[i] is VBRuntimeReference reference)
            {
                // MS-VBAL §5.3.1.11: "a reference parameter binding is defined... referring to the
                // variable referenced by the argument's expression" - true aliasing onto the caller's own
                // storage, set up by RuntimeExpressionEvaluator when it could resolve the argument's own
                // address. A write inside this activation is visible to the caller instantly; no
                // copy-back step is needed because nothing was ever copied.
                frame.PushByRef(parameter, reference.Value, reference.DeclaredType);
            }
            else
            {
                // ByVal, or ByRef with a non-addressable argument (a literal, an expression result) -
                // MS-VBAL §5.3.1.11's own "otherwise" case: a fresh local, Let-assigned from the
                // argument's value, never aliasing anything the caller can see again.
                var held = frame.HoldArgument(parameter, parameter.ResolvedType, arguments[i], activation.Layout.ParameterSlots[i]);

                // the local holds what it was given for as long as the activation does, whatever the caller does with its own variable meanwhile -
                // Me as much as any: a member that lets go of the last variable of its own object still runs on a live object. An object New is
                // initializing is held by the New expression's temporary (ObjectReferences.HoldTemporarily) until the statement is over, so letting go
                // of Me when Initialize returns does not destroy it. A parameter of a type that holds no object (a number, a String) has nothing to count.
                if (ObjectReferences.CanHoldAnObject(parameter.ResolvedType) && (held ?? (frame.TryResolve(parameter, out var resolved) ? resolved : null)) is { } cell)
                {
                    ObjectReferences.Rebind(Session, cell, ObjectReferences.Held.None);
                }
            }
        }

        HoistLocals(Session, frame, GetLocals(procedure));

        var context = new RuntimeEvaluationContext(procedure.Uri);
        var outcome = !sweep
            ? Run(procedure, frame, body, context)
            : Executor.Sweep(Session, frame, body, context) ? RuntimeExecutionOutcome.ExitProcedure : RuntimeExecutionOutcome.InternalError;

        // a program that was stopped does not unwind: the objects its locals held are not let go of, because that is where a Terminate would run, and an End
        // runs none. What it leaves is the session's to clear (ISessionSymbols.ResetStorage) when it is over, or to keep when it is only broken.
        if (outcome.Kind is not (RuntimeExecutionOutcomeKind.Halt or RuntimeExecutionOutcomeKind.Break))
        {
            ReleaseLocals(frame, GetLocals(procedure), parameters, arguments);

            // a Class_Terminate that letting go of them ran, and that left an error unhandled, fails the call with it - which no handler of the caller
            // handles (ISessionErrorState.Untrappable).
            if (Session.Errors.Untrappable is { } untrappable && outcome.Kind is not RuntimeExecutionOutcomeKind.Error)
            {
                outcome = RuntimeExecutionOutcome.Error(untrappable);
            }
        }

        // A program that was stopped by a Stop, or by a break, is suspended and not over (MS-VBAL 5.4.2.11): "all variables maintain their state if execution resumes". Its
        // activations stay on the stack, innermost on top, each with its locals and the place it stopped at, for as long as the session has not been told to let the program
        // go (SessionWipe.Abandon). An End is over, and the wipe that follows it pops what is left of it.
        if (outcome.Kind is not RuntimeExecutionOutcomeKind.Break)
        {
            Session.CallStack.TryPop(out _);
        }

        return outcome.Kind switch
        {
            RuntimeExecutionOutcomeKind.ExitProcedure => RuntimeSemanticsEvaluationResult.Success(
                procedure is VBReturningMemberSymbol ? frame.ReturnValue! : VBVoidValue.Void),
            RuntimeExecutionOutcomeKind.Error => RuntimeSemanticsEvaluationResult.Error(outcome.ErrorInfo!),
            // Halt (End) and Break (Stop) have no answer to give: this return type is a value or an error, and a call that was
            // stopped is neither. It fails as a call fails that has no answer, and the session says why (ISessionHalt) - which is
            // what the Run of the procedure that made the call, and the host that started the program, ask when they see it.
            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };
    }

    // the body run compiled when the session runs compiled code and the procedure is compiled, and in the interpreter otherwise: an
    // analysis, a debugger attached and an error a Class_Terminate left unhandled are the interpreter's. A procedure is not compiled when payloads are not
    // used, and the compiler says so.
    private RuntimeExecutionOutcome Run(VBTypeMemberSymbol procedure, CallStackFrame frame, InstructionList body, RuntimeEvaluationContext context)
    {
        if (CompiledExecution.EngineFor(Session.Environment.ExecutionEngine) is not VBExecutionEngine.Compiled
            || Executor.Observation is not null || Session.Halt.WantsInterpreter || Session.Errors.Untrappable is not null || Bodies is not ProgramImage image)
        {
            return Executor.Run(Session, frame, body, context);
        }

        var compiled = image.Compiler.Compile(Session, Executor.Expressions, procedure, body);
        if (compiled.Body is { } code)
        {
            return code.Run(new CompiledActivation(Session, frame, body, Executor, context, code, this));
        }

        // a test that expects every procedure it runs to be compiled fails the call of one that is not, rather than run it interpreted unnoticed.
        return CompiledExecution.IsStrict ? RuntimeExecutionOutcome.InternalError : Executor.Run(Session, frame, body, context);
    }

    // MS-VBAL §5.3.1.10: the procedure extent variables cease to exist with the activation, and an object that was
    // held by one of them only is destroyed with it - which is where its Terminate runs. What the function returns
    // is taken by whoever called it: it is held as a temporary of the caller's statement first (ObjectReferences.HoldTemporarily),
    // and the function result variable, the locals, the ByVal parameters and the blocks still open let go of what they held
    // after it. A Static local outlives the activation and is not in the frame.
    private void ReleaseLocals(CallStackFrame frame, ImmutableArray<BoundTypedSymbol> locals, ImmutableArray<VBParameterSymbol> parameters, IRuntimeValue[]? arguments)
    {
        ObjectReferences.HoldTemporarily(Session, frame.ReturnValue);
        if (frame.HasResultCell)
        {
            ObjectReferences.Release(Session, frame.ResultCell);
        }

        // the cells are read by the number of their slot when they have one (S1.4c), and looked up by their symbol otherwise. An activation an edit relaid holds
        // the locals the procedure declares now, in its order, and those the edit removed, each looked up by its symbol.
        var layout = frame.Layout;
        if (frame.EditedLocals is { } edited)
        {
            (locals, layout) = ([.. edited.Concat(frame.RetiredLocals).OfType<BoundTypedSymbol>()], null);
        }

        for (var i = 0; i < locals.Length; i++)
        {
            // a variable of a type that holds no object (a number, a String) has nothing to let go of, and is not looked up (S1.3).
            if (locals[i] is not VBLocalVariableSymbol { IsStatic: false } variable || !ObjectReferences.CanHoldAnObject(variable.ResolvedType)
                || CellOf(frame, variable, layout is { LocalSlots.Length: var count } && count == locals.Length ? layout.LocalSlots[i] : -1) is not { } handle)
            {
                continue;
            }

            ObjectReferences.Release(Session, handle);
        }

        // a local whose name an edit gave another variable of the activation: what it held goes with the activation.
        foreach (var retired in frame.RetiredCells)
        {
            ObjectReferences.Release(Session, retired);
        }

        // a ByVal parameter is a local of the activation as much as a Dim is (MS-VBAL §5.3.1.11): what it holds it lets go of with it. Which parameters
        // were given a copy is read again from the arguments, rather than remembered in a list made for each call (IR lowering S1.3). Parameters given
        // payloads are all ByVal (InvokeScalars): a Variant one may have been Set to an object since (S1.7).
        layout = frame.Layout;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if ((arguments is null || !IsAliased(parameter, arguments[i])) && ObjectReferences.CanHoldAnObject(parameter.ResolvedType)
                && CellOf(frame, parameter, layout is { ParameterSlots.Length: var count } && count == parameters.Length ? layout.ParameterSlots[i] : -1) is { } handle)
            {
                ObjectReferences.Release(Session, handle);
            }
        }

        // a block the activation left by returning from inside it: an Exit Function in a With block.
        foreach (var holder in frame.TakeBlockHoldersOutside(null))
        {
            ObjectReferences.Release(Session, holder);
        }
    }

    // MS-VBAL §5.4.3: "Create the function result variable and any procedure extent local variables
    // declared within the procedure" is step 4 of procedure invocation, after parameters (steps 1-2)
    // and the error-handling policy reset (step 3, already free - CallStackFrame.ErrorHandler defaults
    // to Disabled on every freshly-constructed frame) and the return-value seed (also already done).
    // A Dim (procedure extent) gets a fresh Push every call, seeded to its declared type's own default,
    // freed with the rest of the frame when this call returns. A Static (module extent) instead needs
    // storage that OUTLIVES this call - ISymbolResolver.TryAllocate reserves it in the session's own
    // module-level heap, the same tier a module field uses, deliberately NOT via ISessionSymbols.TryDefine:
    // TryDefine's own bucket-add would register the SAME symbol a second time, since a procedure's own
    // Locals (ScopeTreeBuilder.LocalsOf, mirroring how Parameters already "ride on" their own procedure
    // symbol) already makes it resolvable by name on its own - a real ambiguous-name risk, not just
    // redundant work. CallStackAwareSymbolResolver already falls through to this same session-level
    // storage for any Local-scoped symbol the current frame doesn't itself declare, so a Static local
    // never needs a frame binding at all. TryAllocate itself is safe to call every time (a symbol
    // already allocated just gets a fresh, independent copy of the same default value - see its own
    // xmldoc), but only the FIRST call should actually happen: every later call must see whatever the
    // previous call's own body last wrote, not get reset back to the default.
    // what a variable's storage starts as: its type's default, except for an array, which is as big as it was declared.
    private static VBTypedValue DefaultValueOf(IRuntimeSession session, VBLocalVariableSymbol variable)
        => session.Symbols.Defaults?.DefaultValueOf(variable) ?? variable.ResolvedType.DefaultValue;

    /// <summary>
    /// Lays an activation out as its procedure, edited while the program waited, now declares its locals, each declared as the activation's own were when it
    /// began: a <c>Dim</c> at its default, on the activation; a <c>Static</c> at its default, where the procedure's statics are, when it has no storage yet.
    /// The <c>Dim</c>s it lets go of when it returns, in the order the procedure declares them (<see cref="CallStackFrame.EditedLocals"/>).
    /// </summary>
    /// <param name="session">The session the program is in.</param>
    /// <param name="frame">The activation.</param>
    /// <param name="procedure">The procedure the activation is of, as it is defined now.</param>
    /// <param name="goesOn">
    /// Whether the program goes on with the edit: a local the edit removed is retired then - held until the activation returns, with nothing naming it - and
    /// a local declared again under the name of one the activation holds that is not it (one retired, or one a statement typed at a stop declared as
    /// another type) is a variable of its own, at its default. A statement typed at the stop meanwhile (<see langword="false"/>) is only given the locals
    /// the activation holds nothing of yet, so that an edit taken back before the program goes on loses nothing.
    /// </param>
    /// <returns>How many locals were declared.</returns>
    public static int DeclareAddedLocals(IRuntimeSession session, CallStackFrame frame, VBTypeMemberSymbol procedure, bool goesOn)
    {
        // what the activation holds: the procedure's locals it began with and those declared at a stop, or, once an edit relaid it, those it was relaid with.
        var held = (frame.EditedLocals ?? [.. GetLocals(frame.Procedure ?? procedure).OfType<VBLocalVariableSymbol>().Where(local => !local.IsStatic)])
            .Concat(frame.DeclaredAtAStop).GroupBy(local => local.SemanticId).ToDictionary(group => group.Key, group => group.Last());
        var retired = frame.RetiredLocals.ToDictionary(local => local.SemanticId);
        var declared = GetLocals(procedure).OfType<VBLocalVariableSymbol>().ToList();

        var fresh = new List<BoundTypedSymbol>();
        var locals = new List<Symbol>();
        foreach (var local in declared)
        {
            if (local.IsStatic)
            {
                // a Static declared under the name of a local the activation holds (one the edit removed, or the Dim it made a Static) is not that cell: the cell is
                // held apart and let go of when the activation returns, and the Static is where the procedure's statics are.
                if (goesOn && frame.TryResolve(local, out _))
                {
                    frame.Retire(local);
                    _ = retired.Remove(local.SemanticId);
                }

                fresh.Add(local);
                continue;
            }

            locals.Add(local);
            var isRetired = retired.ContainsKey(local.SemanticId);
            var stopDeclared = frame.DeclaredAtAStop.Any(other => other.SemanticId == local.SemanticId);
            if (held.TryGetValue(local.SemanticId, out var was) && !isRetired
                && !(goesOn && stopDeclared && !Equals(((ITypedSymbol)was).ResolvedType, local.ResolvedType)))
            {
                continue;
            }

            // a cell the activation has of the name that is no longer this local's is held apart, and let go of when the activation returns: the local is a
            // variable of its own. One a statement typed at the stop declared stays the activation's until the program goes on with the edit, which may yet be
            // taken back.
            if (frame.TryResolve(local, out _))
            {
                if (!goesOn && !isRetired)
                {
                    continue;
                }

                frame.Retire(local);
            }

            _ = retired.Remove(local.SemanticId);
            fresh.Add(local);
        }

        // a local the edit removed is held until the activation returns.
        if (goesOn)
        {
            foreach (var (id, was) in held)
            {
                if (!declared.Any(local => local.SemanticId == id))
                {
                    retired[id] = was;
                }
            }
        }
        else
        {
            locals = [.. held.Values.Where(was => !frame.DeclaredAtAStop.Contains(was)), .. fresh.Where(local => local is VBLocalVariableSymbol { IsStatic: false })];
        }

        HoistLocals(session, frame, [.. fresh], numbered: false);
        frame.Relaid(locals, [.. retired.Values]);
        return fresh.Count;
    }

    // numbered: whether the locals are the procedure's own, which the frame's layout numbers; locals added by an edit are no part of its layout.
    internal static void HoistLocals(IRuntimeSession session, CallStackFrame frame, ImmutableArray<BoundTypedSymbol> locals, bool numbered = true)
    {
        // the locals of the procedure the frame is laid out as are in its slots by number (S1.3).
        var slots = numbered && frame.Layout is { LocalSlots: var slotted } && slotted.Length == locals.Length ? slotted : default;
        for (var i = 0; i < locals.Length; i++)
        {
            var local = locals[i];
            if (local is not VBLocalVariableSymbol variable)
            {
                // VBLocalConstantSymbol (a local Const): MS-VBAL's own value is a compile-time
                // substitution, never a runtime address - the symbol carries its declaration's own
                // expression, and RuntimeExpressionEvaluator folds that at each use site, so there is
                // nothing here to hoist. One that carries no expression has no value to read at all,
                // and reading it resolves to InternalError.
                continue;
            }

            if (variable.IsStatic)
            {
                // a member of a class called on an object: MS-VBA gives each object its own Static local (Excel-verified, oracle\s19 N37), which the
                // object holds beside its fields, where MS-VBAL §5.4.3.1 gives it module extent.
                if (frame.Target is { } target && session.Symbols.TryGetInstance(target, out var instance))
                {
                    if (!instance.TryResolve(variable, out _))
                    {
                        instance.Push(variable, DefaultValueOf(session, variable));
                    }
                }
                else if (!session.Symbols.Resolver.TryGetAddress(variable, out _))
                {
                    session.Symbols.Resolver.TryAllocate(variable, DefaultValueOf(session, variable), out _);
                }
            }
            else
            {
                frame.Push(variable, DefaultValueOf(session, variable), slots.IsDefault ? -1 : slots[i]);
            }
        }
    }

    // what an activation of a procedure is made of that the procedure alone decides, made once for each procedure the invoker activates (IR lowering S0.9):
    // the invoker lives as long as one execution, and a procedure that is defined again is another symbol. Its layout too (S1.3), which is the symbol's.
    // Its body is not among them: a module loaded again while a program is broken replaces it for the calls made after - the activation remembers the body
    // it found, with the program image it found it in (Body), which is the body as long as the image is the same.
    private sealed record class Activation(StaticSymbol Symbol, SyntaxNodeId NodeId, ModuleDirectives Directives, SDK.Semantics.Binding.FrameLayout Layout)
    {
        public FoundBody? Body;
    }

    private sealed record class FoundBody(object Image, InstructionList List);

    // the body of the procedure in the program image as it is now: looked up by the procedure's identity, a string hashed and a tree walked, once for
    // each image a load leaves rather than once for each call.
    private InstructionList? BodyOf(VBTypeMemberSymbol procedure, Activation activation)
    {
        object image = Bodies is ProgramImage program ? program.Procedures : Bodies;
        if (activation.Body is { } found && ReferenceEquals(found.Image, image) && Bodies is ProgramImage)
        {
            return found.List;
        }

        if (!Bodies.TryGetValue(procedure.SemanticId, out var body))
        {
            return null;
        }

        activation.Body = new FoundBody(image, body);
        return body;
    }

    private readonly ConcurrentDictionary<VBTypeMemberSymbol, Activation> _activations = new(ReferenceEqualityComparer.Instance);

    /// <summary>The body of <paramref name="procedure"/> as this invoker runs it; <see langword="null"/> when it has none.</summary>
    internal InstructionList? BodyOf(VBTypeMemberSymbol procedure)
        => BodyOf(procedure, _activations.TryGetValue(procedure, out var activation) ? activation : _activations.GetOrAdd(procedure, ActivationOf));

    /// <summary>
    /// The activation of <paramref name="procedure"/> a call compiled code makes of it runs in, when it is compiled too and every parameter of it is a scalar
    /// passed by value (IR lowering S2.3b): laid out, and on the call stack, as <see cref="InvokeScalars"/> would make it - but holding nothing. The callee's
    /// compiled code is given its arguments, and holds its parameters and its variables; it gives the frame their cells only when the activation is handed to
    /// the interpreter, or a debugger is to see them (<c>CompiledActivation.Spill</c>).
    /// </summary>
    /// <returns><see langword="null"/> when the call stack is full: the call is then made as any other, which says so (<see cref="VBRuntimeErrorId.OutOfStackSpace"/>).</returns>
    internal CallStackFrame? PushCompiledActivation(VBTypeMemberSymbol procedure, InstructionList body)
    {
        if (!_activations.TryGetValue(procedure, out var activation))
        {
            activation = _activations.GetOrAdd(procedure, ActivationOf);
        }

        var frame = (CallStackFrame)Session.Symbols.CreateFrame(activation.NodeId, activation.Symbol, activation.Directives);
        (frame.Procedure, frame.Body) = (procedure, body);
        frame.LayOut(activation.Layout);
        return Session.CallStack.TryPush(frame) ? frame : null;
    }

    /// <summary>
    /// The end of an activation <see cref="PushCompiledActivation"/> made, as <see cref="InvokeScalars"/> ends one: what an error a <c>Class_Terminate</c> left
    /// unhandled makes of its outcome, and the frame off the stack - unless the program was stopped in it, which leaves it there. Its parameters and variables
    /// are scalars, which hold no object to let go of.
    /// </summary>
    /// <returns>The activation's outcome, as its caller is to see it.</returns>
    internal RuntimeExecutionOutcome PopCompiledActivation(RuntimeExecutionOutcome outcome)
    {
        if (outcome.Kind is not (RuntimeExecutionOutcomeKind.Halt or RuntimeExecutionOutcomeKind.Break or RuntimeExecutionOutcomeKind.Error)
            && Session.Errors.Untrappable is { } untrappable)
        {
            outcome = RuntimeExecutionOutcome.Error(untrappable);
        }

        if (outcome.Kind is not RuntimeExecutionOutcomeKind.Break)
        {
            Session.CallStack.TryPop(out _);
        }

        return outcome;
    }

    // MS-VBAL 5.2.1: a procedure's code runs under the Option directives of the module declaring it, whichever module called it. A StaticSymbol has no link
    // back to its module, so the frame is what carries them.
    private Activation ActivationOf(VBTypeMemberSymbol procedure) => new(
        new StaticSymbol(procedure.Name, procedure.Kind, procedure.ResolvedType),
        new SyntaxNodeId(procedure.Uri.AbsolutePath, []),
        DeclaringModuleDirectives(procedure),
        SDK.Semantics.Binding.FrameLayout.For(procedure));

    // the module a procedure is declared in is its symbol's parent, a standard module or a class module. A procedure whose parent is not a module symbol
    // — one the session has no module for — runs under the defaults, which is what every activation used to do.
    private ModuleDirectives DeclaringModuleDirectives(VBTypeMemberSymbol procedure)
        => Session.Symbols.TryGetModule(procedure.ParentUri.Fragment.TrimStart('#'), out var module)
            ? module.Directives
            : ModuleDirectives.None;

    internal static bool IsByRef(ParameterKind kind) => kind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef;

    // the cell of a variable of the activation: the one in its slot, or the one its symbol resolves to.
    private static IBindingHandle? CellOf(CallStackFrame frame, Symbol variable, int slot)
        => frame.TryGetSlotCell(slot, out var cell) || frame.TryResolve(variable, out cell) ? cell : null;

    // a ByRef parameter whose argument is a variable is an alias of it (MS-VBAL §5.3.1.11); any other is a fresh local, given a copy of its argument.
    private static bool IsAliased(VBParameterSymbol parameter, IRuntimeValue argument)
        => IsByRef(parameter.ParameterKind) && argument is VBRuntimeReference or CellReference;

    /// <summary>
    /// The value an optional parameter that declares no default takes when the call supplies no argument for it.
    /// </summary>
    /// <remarks>
    /// A <c>Variant</c> one is Missing (<strong>MS-VBAL §6.1.2.7.1.6</strong>) - the value <c>IsMissing</c> is there to detect, which
    /// any other value would leave it unable to. Any other declared type cannot hold Missing, and takes its type's default.
    /// </remarks>
    internal static VBTypedValue OmittedArgumentValue(VBParameterSymbol parameter)
        => parameter.ResolvedType is VBVariantType
            ? new VBVariantValue(VBMissingValue.Missing)
            : parameter.ResolvedType.DefaultValue;

    internal static ImmutableArray<VBParameterSymbol> GetParameters(VBTypeMemberSymbol procedure) => procedure switch
    {
        VBProcedureMemberSymbol sub => sub.Parameters,
        VBReturningMemberSymbol returning => returning.Parameters,
        _ => [],
    };

    internal static ImmutableArray<BoundTypedSymbol> GetLocals(VBTypeMemberSymbol procedure) => procedure switch
    {
        VBProcedureMemberSymbol sub => sub.Locals,
        VBReturningMemberSymbol returning => returning.Locals,
        _ => [],
    };
}
