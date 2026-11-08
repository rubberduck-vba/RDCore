using System.Diagnostics;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.Runtime.Compiled;

public sealed partial class CompiledActivation
{
    // what compiled code evaluated of the instruction it hands the activation to the interpreter in the middle of, each by where it is in its statement.
    private List<(int Index, ScalarPayload Value)>? _knowns;

    /// <summary>
    /// Keeps what compiled code evaluated of an instruction that calls inside an expression, for the interpreter it hands the activation to after the call.
    /// </summary>
    /// <param name="syntax">Where the call, or the variable read, is in the instruction's statement (<c>KnownOperands.IndexOf</c>).</param>
    /// <param name="kind">The kind of what it evaluated to.</param>
    /// <param name="integral">The payload, of an integral kind.</param>
    /// <param name="floating">The payload, of a floating-point kind.</param>
    public void Know(int syntax, ScalarKind kind, long integral, double floating)
        => (_knowns ??= []).Add((syntax, new ScalarPayload(ScalarKinds.TypeOf(kind), integral, floating)));

    // the frame given what compiled code evaluated of the instruction at pc, which the interpreter runs from the middle (inside); nothing, when it runs the
    // instruction from its start (with the error of a call), which the activation's handler is given. False when what was evaluated names nothing of the
    // instruction: the interpreter would evaluate it again, and make its calls a second time.
    private bool HandKnowns(CallStackFrame frame, InstructionList list, int pc, bool inside)
    {
        if (_knowns is not { Count: > 0 } known)
        {
            return true;
        }

        try
        {
            if (!inside)
            {
                return true;
            }

            if (pc < list.Items.Length && list.Items[pc].Node is { } statement && KnownOperands.Of(statement, known) is { } operands)
            {
                frame.Know(operands);
                return true;
            }

            Debug.Fail($"what compiled code evaluated of the instruction at {pc} names nothing of it");
            return false;
        }
        finally
        {
            known.Clear();
        }
    }

    private bool HasKnowns => _knowns is { Count: > 0 };
}
