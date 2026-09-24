using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Bindings;
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
    {
        if (!Bodies.TryGetValue(procedure.SemanticId, out var body))
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var parameters = GetParameters(procedure);
        if (parameters.Length != arguments.Length)
        {
            // arity mismatch: a real static-semantics gap (no call-site argument-count check exists yet
            // either), not something this slice invents error handling for.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var staticSymbol = new StaticSymbol(procedure.Name, procedure.Kind, procedure.ResolvedType);
        var frame = (CallStackFrame)Session.Symbols.CreateFrame(new SyntaxNodeId(procedure.Uri.AbsolutePath, []), staticSymbol);

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
                frame.PushByRef(parameter, reference.Value);
            }
            else
            {
                // ByVal, or ByRef with a non-addressable argument (a literal, an expression result) -
                // MS-VBAL §5.3.1.11's own "otherwise" case: a fresh local, Let-assigned from the
                // argument's value, never aliasing anything the caller can see again.
                frame.Push(parameter, parameter.ResolvedType.CreateValue(new ValueBindingHandle(arguments[i])));
            }
        }

        var outcome = Executor.Run(Session, frame, body, new RuntimeEvaluationContext(procedure.Uri));
        Session.CallStack.TryPop(out _);

        return outcome.Kind switch
        {
            RuntimeExecutionOutcomeKind.ExitProcedure => RuntimeSemanticsEvaluationResult.Success(
                procedure is VBReturningMemberSymbol ? frame.ReturnValue! : VBVoidValue.Void),
            RuntimeExecutionOutcomeKind.Error => RuntimeSemanticsEvaluationResult.Error(outcome.ErrorInfo!),
            // Halt (End) and Break (Stop) inside a called procedure have no way to propagate through
            // this return type yet - RuntimeSemanticsEvaluationResult is Result-or-Error only. End's own
            // "wipe the whole session atomically" semantics need a session-level signal a caller's own
            // ProcedureExecutor.Run can observe, which doesn't exist yet - deferred, not mismodeled.
            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };
    }

    internal static bool IsByRef(ParameterKind kind) => kind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef;

    internal static ImmutableArray<VBParameterSymbol> GetParameters(VBTypeMemberSymbol procedure) => procedure switch
    {
        VBProcedureMemberSymbol sub => sub.Parameters,
        VBReturningMemberSymbol returning => returning.Parameters,
        _ => [],
    };
}
