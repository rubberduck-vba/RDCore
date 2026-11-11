using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a function result of the Variant type, or of the String type: its own, assigned (and a Variant read) where its frame holds
// it; and what a call returned, which the statement that made the call assigns, then lets go of.
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
    /// Assigns the function result variable, of the <c>String</c> type, the text <paramref name="text"/> as it is (MS-VBAL §5.3.1, §5.4.3.8): as the
    /// interpreter assigns it, where the frame holds it, until the function returns it.
    /// </summary>
    /// <param name="text">The text assigned; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns><see langword="false"/> when the interpreter runs the statement: the activation has no frame to hold the result.</returns>
    public bool TryAssignResultText(string? text)
    {
        if (FailsAccessAtRandom() || Frame is not { } frame)
        {
            return false;
        }

        frame.ReturnValue = ObjectReferences.AssignResult(_session, frame.ResultCell, new VBStringValue(new ValueBindingHandle(new VBRuntimeValue<string>(text!))));
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

    // the temporaries of the session when the statement compiled code is running began (BeginStatement): what its calls return is held from then until
    // the statement is over (ObjectReferences.HoldTemporarily), as the interpreter holds it.
    private int _statementTemporaries;

    /// <summary>
    /// Begins a statement whose calls return what the session holds as its temporaries (<see cref="CompiledCallee.HoldsWhatItReturns"/>), before its
    /// first call: what they return is held until <see cref="EndStatement"/>, as the interpreter holds a statement's temporaries.
    /// </summary>
    public void BeginStatement() => (_statementTemporaries, _inStatement) = (_session.Objects.TemporaryCount, true);

    /// <summary>
    /// Ends the statement <see cref="BeginStatement"/> began, once it assigned what it assigns: what its calls returned is let go of, as the interpreter
    /// lets go of a statement's temporaries before the next instruction, which is where an object only they held is destroyed.
    /// </summary>
    public void EndStatement()
    {
        _inStatement = false;
        ObjectReferences.ReleaseTemporaries(_session, _statementTemporaries);
    }

    // whether a statement compiled code began is not over: one handed to the interpreter is the interpreter's to end.
    private bool _inStatement;

    // the temporaries the statement compiled code hands to the interpreter holds (ProcedureExecutor.Run), which the interpreter lets go of when it is over;
    // none of a statement that was not begun, or is over.
    private int? HandStatement()
    {
        var held = _inStatement ? _statementTemporaries : (int?)null;
        _inStatement = false;
        return held;
    }

    // the statement of a call that raised an error, or whose result's assignment did, ends as the interpreter ends it (ProcedureExecutor.RunInstructions):
    // what it held is let go of before the error is handled - unless the program stopped, which lets go of nothing.
    private void EndStatementAfter(CompiledCallee called, RuntimeExecutionOutcome failed)
    {
        if (called.HoldsWhatItReturns && failed.Kind is not (RuntimeExecutionOutcomeKind.Break or RuntimeExecutionOutcomeKind.Halt))
        {
            EndStatement();
        }
    }

    // what a call returned, assigned when its statement assigns it (CompiledCallee.AssignsResult), as the interpreter assigns the value of a statement's
    // expression (StatementRuntimeSemanticsProvider.AssignValue) - to a Variant, whatever it holds, or to a member of an object - before the statement
    // ends, in the context the interpreter evaluates the statement in (ContextAt). An error the assignment raises is the statement's, which the interpreter
    // is given at the call (AfterFailedCall): the call is not made again.
    private bool AssignReturned(CompiledCallee called, VBTypedValue? returned)
    {
        if (!called.AssignsResult)
        {
            return true;
        }

        var pc = _frame?.Pc ?? _pc;
        var outcome = returned is not null && _executor.Statements is StatementRuntimeSemanticsProvider statements
            && _list.Items[pc].Node is AssignmentStatementNode assignment
                ? statements.AssignValue(_session, ContextAt(pc), assignment, returned)
                : RuntimeExecutionOutcome.InternalError;
        if (outcome.Kind is RuntimeExecutionOutcomeKind.Next)
        {
            return true;
        }

        EndStatementAfter(called, outcome);
        _failed = outcome;
        return false;
    }

    // what an activation a call entered directly returned (ReturnedDirectly): the frame's function result, or the payload the activation holds it as while it
    // has no frame.
    private static VBTypedValue? ReturnedBy(CompiledActivation callee)
        => callee._frame is { } frame ? frame.ReturnValue
            : callee.TryReadResult(out var payload) ? payload.Typed()
            : null;
}
