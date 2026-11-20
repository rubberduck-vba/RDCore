using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Collections.Immutable;

namespace RDCore.Runtime.Semantics;

public sealed partial class RuntimeExpressionEvaluator
{
    // the arguments of the accessor call an assignment is, evaluated ahead of its target in the activation that runs it, until the target is evaluated.
    private (ICallStackFrame? Frame, ArgumentCalls? Calls) _accessorArguments;

    // MS-VBA evaluates an assignment whose target is a call of an accessor as the accessor's call statement, the value its last argument (observed in
    // Excel): the calls among the target's arguments are made first, then those inside the arguments of its owner's member calls, then the value is
    // evaluated, then the other arguments, right to left, and the owner last (EvaluateArgumentsFirst). MS-VBAL §5.4.3.8 and §5.4.3.9 do not say. The
    // assignment is made with what they evaluated to known, and failed when one of them raised an error; the temporaries they made are put in their order
    // once the owner is evaluated (SettleAccessorArguments).
    internal TResult WithAccessorArgumentsFirst<TState, TResult>(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode? owner,
        ImmutableArray<ExpressionNode> arguments, ExpressionNode value, TState state, Func<TState, RuntimeSemanticsEvaluationResult?, TResult> assign)
    {
        var calls = EvaluateArgumentsFirst(session, context, owner is null ? null : BoundTreeOf(session, owner, context), default, arguments.Add(value),
            statement: true, out var error);
        if (calls is null)
        {
            return assign(state, error);
        }

        var outer = _accessorArguments;
        _accessorArguments = (session.CallStack.Current, calls);
        try
        {
            return assign(state, null);
        }
        finally
        {
            _accessorArguments = outer;
            calls.End();
        }
    }

    // the owner of an assignment's target is evaluated: the temporaries the arguments of the accessor call it is and the owner made are put in their order,
    // when the activation that evaluated them runs the assignment.
    internal void SettleAccessorArguments(IRuntimeSession session)
    {
        if (_accessorArguments is { Calls: { } calls, Frame: var frame } && ReferenceEquals(frame, session.CallStack.Current))
        {
            calls.Settle();
            _accessorArguments = (frame, null);
        }
    }
}
