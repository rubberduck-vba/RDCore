using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a function result of the Variant type: its own, assigned and read where its frame holds it; and what a call returned, which
// the statement that made the call assigns, then lets go of.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Assigns the function result variable, of the <c>Variant</c> type, the payload <paramref name="value"/>, of its own subtype (MS-VBAL §5.3.1,
    /// §5.5.1.2.12): as the interpreter assigns it, converted by the plan of its let-coercion, in the frame's result cell
    /// (<c>ObjectReferences.AssignResult</c>), which holds it until the function returns it.
    /// </summary>
    /// <param name="value">The payload assigned.</param>
    /// <returns>
    /// <see langword="false"/> when the interpreter runs the statement: the result held an object that letting go of would destroy, whose <c>Terminate</c>
    /// is the interpreter's to run (MS-VBAL §5.3.1.10), or the value is not one the plan converts.
    /// </returns>
    public bool TryAssignResultVariant(VariantPayload value)
    {
        var payload = value.Value;
        if (FailsAccessAtRandom() || Frame is not { Procedure: ITypedSymbol function } frame
            || !_executor.Expressions.TryConvertUnobserved(payload.Type.CreateValue(new ValueBindingHandle(payload.Box())), function.ResolvedType, out var converted)
            || Destroys(ObjectReferences.HeldIn(_session, frame.ResultCell)))
        {
            return false;
        }

        frame.ReturnValue = ObjectReferences.AssignResult(_session, frame.ResultCell, converted);
        return true;
    }

    /// <summary>
    /// The payload the function result variable, of the <c>Variant</c> type, holds, when it holds one: a known value of a scalar type. When it holds
    /// anything else - <c>Empty</c> before it is first assigned, <c>Null</c>, a <c>String</c>, an object, an array - the interpreter reads it.
    /// </summary>
    /// <param name="value">The payload read.</param>
    /// <returns>Whether the result holds a payload compiled code reads.</returns>
    public bool TryReadResultVariant(out VariantPayload value)
    {
        value = default;
        if (FailsAccessAtRandom() || Frame.ReturnValue is not { IsIndeterminate: false } held)
        {
            return false;
        }

        var inner = held is VBVariantValue { TypedValue: var wrapped } ? wrapped : held;
        if (inner is VBVariantValue || !ScalarPayload.TryOf(inner.TypeInfo, inner.RuntimeValue, out var payload))
        {
            return false;
        }

        value = new VariantPayload(payload);
        return true;
    }

    // the temporaries of the session when the call compiled code is making was made: what the call returned is held from then until the statement that made
    // it is over (ObjectReferences.HoldTemporarily), as the interpreter holds it.
    private int _temporariesBeforeCall;

    // the end of the statement of a call that returned: what the call returned assigned, when the statement assigns it (CompiledCallee.AssignsResult), as the
    // interpreter assigns the value of a statement's expression (StatementRuntimeSemanticsProvider.AssignValue) - to a Variant, whatever it holds; then
    // what the call returned let go of, as the interpreter lets go of a statement's temporaries when it is over, which is where an object only it held is
    // destroyed. An error the assignment raises is the statement's, which the interpreter is given at the call (AfterFailedCall): the call is not made again.
    private bool EndCallStatement(CompiledCallee called, VBTypedValue? returned)
    {
        var outcome = RuntimeExecutionOutcome.Next;
        if (called.AssignsResult)
        {
            outcome = returned is not null && _executor.Statements is StatementRuntimeSemanticsProvider statements
                && _list.Items[_frame?.Pc ?? _pc].Node is AssignmentStatementNode assignment
                    ? statements.AssignValue(_session, _context, assignment, returned)
                    : RuntimeExecutionOutcome.InternalError;
        }

        ObjectReferences.ReleaseTemporaries(_session, _temporariesBeforeCall);
        if (outcome.Kind is RuntimeExecutionOutcomeKind.Next)
        {
            return true;
        }

        _failed = outcome;
        return false;
    }

    // a call that raised an error ends its statement, as the interpreter ends it: what the statement held is let go of before the error is handled.
    private void ReleaseAfterError(RuntimeExecutionOutcome failed)
    {
        if (failed.Kind is RuntimeExecutionOutcomeKind.Error)
        {
            ObjectReferences.ReleaseTemporaries(_session, _temporariesBeforeCall);
        }
    }

    // what an activation a call entered directly returned (ReturnedDirectly): the frame's function result, or the payload the activation holds it as while it
    // has no frame.
    private static VBTypedValue? ReturnedBy(CompiledActivation callee)
        => callee._frame is { } frame ? frame.ReturnValue
            : callee.TryReadResult(out var payload) ? payload.Type.CreateValue(new ValueBindingHandle(payload.Box()))
            : null;
}
