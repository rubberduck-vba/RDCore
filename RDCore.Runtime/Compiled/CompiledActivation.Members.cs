using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a member of an object it assigns, or calls on the object of a With block.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Assigns the payload <paramref name="value"/>, of its own type, to the member of an object the <c>Let</c> at <paramref name="pc"/> assigns to
    /// (MS-VBAL §5.4.3.8): as the interpreter assigns the value of a statement's expression (<c>StatementRuntimeSemanticsProvider.AssignValue</c>), the
    /// object evaluated after the value, and the member found on it (a field, which holds the value let-coerced to its declared type, or a
    /// <c>Property Let</c>, which is called with it) in the context the interpreter evaluates the statement in, the object of its <c>With</c> block included.
    /// It is a support call: a <c>Property Let</c> runs user code, and what the statement held while it ran is let go of when it is over, as the interpreter
    /// lets go of it before the next instruction. The frame says the instruction is the one being run, as it does of a call.
    /// </summary>
    /// <param name="pc">The offset of the statement.</param>
    /// <param name="value">The payload assigned.</param>
    /// <returns>
    /// Whether the assignment completed. When not, the error it raised is the interpreter's to give the activation's handler, as a call's is
    /// (<see cref="AfterFailedCall"/>): the statement is not run again.
    /// </returns>
    public bool LetMember(int pc, VariantPayload value)
    {
        var frame = Frame;
        frame.Pc = pc;
        _failed = null;
        _suspensionsBeforeCall = Suspensions;
        var temporaries = _session.Objects.TemporaryCount;
        var payload = value.Value;
        var outcome = _executor.Statements is StatementRuntimeSemanticsProvider statements && Body.Items[pc].Node is AssignmentStatementNode assignment
            ? statements.AssignValue(_session, ContextAt(pc), assignment, payload.Typed())
            : RuntimeExecutionOutcome.InternalError;

        // a program that was stopped does not unwind (ProcedureExecutor.Run).
        if (outcome.Kind is not (RuntimeExecutionOutcomeKind.Break or RuntimeExecutionOutcomeKind.Halt))
        {
            ObjectReferences.ReleaseTemporaries(_session, temporaries);
        }

        if (outcome.Kind is RuntimeExecutionOutcomeKind.Next)
        {
            return true;
        }

        // the program stopped in a Property Let is the interpreter's to say, from the halt it was asked for (ProcedureExecutor.RunAfter).
        _failed = outcome.Kind is RuntimeExecutionOutcomeKind.Error ? outcome : RuntimeExecutionOutcome.InternalError;
        return false;
    }

    /// <summary>
    /// Whether compiled code makes the call numbered <paramref name="callee"/> of the body, of a member of the object of the <c>With</c> block opened at
    /// <paramref name="opener"/> (MS-VBAL §5.4.2.21), as <see cref="CanCallMember"/> says of a member of the object a variable refers to. The object is the
    /// one the interpreter holds for the block, where it holds it: a block whose <c>With</c> statement set none (jumped into, or its object in error and
    /// resumed past) has <c>Nothing</c>, whose member is error 91, which the interpreter raises; so does a block whose object is <c>Nothing</c>.
    /// </summary>
    /// <param name="callee">The number of the call among the body's callees.</param>
    /// <param name="opener">The offset of the <c>With</c> statement of the block.</param>
    public bool CanCallMemberOfWith(int callee, int opener)
    {
        (_member, _receiver, _memberInstance) = (null, null, null);
        var called = _body.Callees[callee];
        if (_executor.Observation is not null || !Frame.TryGetBlockState(opener, out var held)
            || (held is VBVariantValue { TypedValue: var inner } ? inner : held) is not VBObjectValue target || target.IsNothing()
            || !_session.Symbols.TryGetInstance(target.Value, out var instance)
            || MemberOf(called, instance.ClassModule) is not { } member || !Takes(called, member))
        {
            return false;
        }

        (_member, _receiver, _memberInstance) = (member, target.RuntimeValue, instance);
        return true;
    }

    // the context the interpreter evaluates the statement at pc in (ProcedureExecutor.ResolveContext): inside a With block, the object the block holds, or
    // Nothing when it holds none, which its frame says - a With statement is the interpreter's, which an activation is given its frame for.
    private RuntimeEvaluationContext ContextAt(int pc)
        => Body.Items[pc] is { EnclosingWith: not null } instruction ? ProcedureExecutor.ResolveContext(_context, Frame, Body, instruction) : _context;
}
