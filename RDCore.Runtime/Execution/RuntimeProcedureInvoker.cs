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
        => Activate(procedure, arguments, (frame, body) => Executor.Run(Session, frame, body, new RuntimeEvaluationContext(procedure.Uri)));

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
        => Activate(procedure, [.. GetParameters(procedure).Select(AssumedArgument(procedure))],
            (frame, body) => Executor.Sweep(Session, frame, body, new RuntimeEvaluationContext(procedure.Uri))
                ? RuntimeExecutionOutcome.ExitProcedure
                : RuntimeExecutionOutcome.InternalError).IsSuccess;

    private Func<VBParameterSymbol, IRuntimeValue> AssumedArgument(VBTypeMemberSymbol procedure) => parameter =>
    {
        if (parameter.Name == "Me"
            && Session.Symbols.Resolver.ResolveType(procedure.ParentUri.Fragment.TrimStart('#'), ScopeKind.Global, StaticSymbol.GlobalUri).Symbol is VBClassModuleSymbol classModule)
        {
            var objectId = Session.Objects.CreateObject();
            Session.Symbols.CreateInstance(objectId, classModule);
            return new VBObjectValue(objectId).RuntimeValue;
        }

        return SymbolAddressTable.BoxedValue(parameter.ResolvedType.DefaultValue);
    };

    private RuntimeSemanticsEvaluationResult Activate(
        VBTypeMemberSymbol procedure, IRuntimeValue[] arguments, Func<CallStackFrame, InstructionList, RuntimeExecutionOutcome> run)
    {
        if (!Bodies.TryGetValue(procedure.SemanticId, out var body))
        {
            // a member with no body is a real gap in the platform. A member whose code is not the workspace's
            // never reaches here at all: ICallableBindingFactory binds it to an IExternalDispatcher instead,
            // which is where that decision belongs - at the binding, not inside the thing that runs one kind.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var parameters = GetParameters(procedure);
        if (parameters.Length != arguments.Length)
        {
            // arity mismatch: a real static-semantics gap (no call-site argument-count check exists yet
            // either), not something this slice invents error handling for.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var activation = _activations.GetOrAdd(procedure, ActivationOf);
        var frame = (CallStackFrame)Session.Symbols.CreateFrame(activation.NodeId, activation.Symbol, activation.Directives);

        // the first parameter of a member of a class is its Me, and the object the member is a call on is what its
        // fields are the storage of.
        if (GetParameters(procedure) is [{ Name: "Me" }, ..] && arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var target }, ..])
        {
            frame.Target = target;
        }

        (frame.Procedure, frame.Body) = (procedure, body);
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
            frame.ReturnValue = returning.ResolvedType.DefaultValue;
        }

        List<VBParameterSymbol>? copied = null;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (IsByRef(parameter.ParameterKind) && arguments[i] is VBRuntimeReference reference)
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
                frame.Push(parameter, parameter.ResolvedType.CreateValue(new ValueBindingHandle(arguments[i])));

                // the local holds what it was given for as long as the activation does, whatever the caller does with its own variable meanwhile -
                // Me as much as any: a member that lets go of the last variable of its own object still runs on a live object. An object New is
                // initializing is held by the New expression's temporary (ObjectReferences.HoldTemporarily) until the statement is over, so letting go
                // of Me when Initialize returns does not destroy it. A parameter of a type that holds no object (a number, a String) has nothing to count.
                if (ObjectReferences.CanHoldAnObject(parameter.ResolvedType) && frame.TryResolve(parameter, out var cell))
                {
                    ObjectReferences.Rebind(Session, cell, ObjectReferences.Held.None);
                    (copied ??= []).Add(parameter);
                }
            }
        }

        HoistLocals(Session, frame, GetLocals(procedure));

        var outcome = run(frame, body);

        // a program that was stopped does not unwind: the objects its locals held are not let go of, because that is where a Terminate would run, and an End
        // runs none. What it leaves is the session's to clear (ISessionSymbols.ResetStorage) when it is over, or to keep when it is only broken.
        if (outcome.Kind is not (RuntimeExecutionOutcomeKind.Halt or RuntimeExecutionOutcomeKind.Break))
        {
            ReleaseLocals(frame, GetLocals(procedure), copied);
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

    // MS-VBAL §5.3.1.10: the procedure extent variables cease to exist with the activation, and an object that was
    // held by one of them only is destroyed with it - which is where its Terminate runs. What the function returns
    // is taken by whoever called it: it is held as a temporary of the caller's statement first (ObjectReferences.HoldTemporarily),
    // and the function result variable, the locals, the ByVal parameters and the blocks still open let go of what they held
    // after it. A Static local outlives the activation and is not in the frame.
    private void ReleaseLocals(CallStackFrame frame, ImmutableArray<BoundTypedSymbol> locals, List<VBParameterSymbol>? copiedParameters)
    {
        ObjectReferences.HoldTemporarily(Session, frame.ReturnValue);
        if (frame.HasResultCell)
        {
            ObjectReferences.Release(Session, frame.ResultCell);
        }

        foreach (var local in locals)
        {
            if (local is not VBLocalVariableSymbol { IsStatic: false } variable || !frame.TryResolve(variable, out var handle))
            {
                continue;
            }

            ObjectReferences.Release(Session, handle);
        }

        // a ByVal parameter is a local of the activation as much as a Dim is (MS-VBAL §5.3.1.11): what it holds it lets go of with it.
        foreach (var parameter in copiedParameters ?? [])
        {
            if (frame.TryResolve(parameter, out var handle))
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

    internal static void HoistLocals(IRuntimeSession session, CallStackFrame frame, ImmutableArray<BoundTypedSymbol> locals)
    {
        foreach (var local in locals)
        {
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
                if (!session.Symbols.Resolver.TryGetAddress(variable, out _))
                {
                    session.Symbols.Resolver.TryAllocate(variable, DefaultValueOf(session, variable), out _);
                }
            }
            else
            {
                frame.Push(variable, DefaultValueOf(session, variable));
            }
        }
    }

    // what an activation of a procedure is made of that the procedure alone decides, made once for each procedure the invoker activates (IR lowering S0.9):
    // the invoker lives as long as one execution, and a procedure that is defined again is another symbol. Its body is not among them: a module loaded again
    // while a program is broken replaces it for the calls made after.
    private sealed record class Activation(StaticSymbol Symbol, SyntaxNodeId NodeId, ModuleDirectives Directives);

    private readonly ConcurrentDictionary<VBTypeMemberSymbol, Activation> _activations = new(ReferenceEqualityComparer.Instance);

    // MS-VBAL 5.2.1: a procedure's code runs under the Option directives of the module declaring it, whichever module called it. A StaticSymbol has no link
    // back to its module, so the frame is what carries them.
    private Activation ActivationOf(VBTypeMemberSymbol procedure) => new(
        new StaticSymbol(procedure.Name, procedure.Kind, procedure.ResolvedType),
        new SyntaxNodeId(procedure.Uri.AbsolutePath, []),
        DeclaringModuleDirectives(procedure));

    // the module a procedure is declared in is its symbol's parent, a standard module or a class module,
    // whose name binds only as a type. A procedure whose parent is not a module symbol — one the session
    // has no module for — runs under the defaults, which is what every activation used to do.
    private ModuleDirectives DeclaringModuleDirectives(VBTypeMemberSymbol procedure)
        => Session.Symbols.TryResolveModule(procedure.ParentUri.Fragment.TrimStart('#'), out var module)
            ? module.Directives
            : ModuleDirectives.None;

    internal static bool IsByRef(ParameterKind kind) => kind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef;

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
