using System.Diagnostics;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.Runtime.Compiled;

public sealed partial class CompiledActivation
{
    // what compiled code evaluated of the instruction it hands the activation to the interpreter in the middle of, each by where it is in its statement.
    private List<(int Index, ScalarPayload Value)>? _knowns;

    // the objects calls of the instruction returned, each by where the call is in its statement: held by the statement's temporaries, not by these.
    private List<(int Index, VBTypedValue Value)>? _knownObjects;

    /// <summary>
    /// Keeps what compiled code evaluated of an instruction that calls inside an expression, for the interpreter it hands the activation to after the call.
    /// </summary>
    /// <param name="syntax">Where the call, or the variable read, is in the instruction's statement (<c>KnownOperands.IndexOf</c>).</param>
    /// <param name="kind">The kind of what it evaluated to.</param>
    /// <param name="integral">The payload, of an integral kind.</param>
    /// <param name="floating">The payload, of a floating-point kind.</param>
    public void Know(int syntax, ScalarKind kind, long integral, double floating)
        => (_knowns ??= []).Add((syntax, new ScalarPayload(ScalarKinds.TypeOf(kind), integral, floating)));

    /// <summary>
    /// Keeps the object a call of an instruction returned (<see cref="TakeReturnedObject"/>), for the interpreter it hands the activation to after the call,
    /// as <see cref="Know"/> keeps a payload: the interpreter does not make the call again. The statement's temporaries hold the object until the instruction
    /// is over.
    /// </summary>
    /// <param name="syntax">Where the call is in the instruction's statement (<c>KnownOperands.IndexOf</c>).</param>
    /// <param name="returned">The object, as <see cref="TakeReturnedObject"/> took it.</param>
    public void KnowObject(int syntax, object? returned)
    {
        if (returned is VBTypedValue value)
        {
            (_knownObjects ??= []).Add((syntax, value));
        }
    }

    // what the last call of a function or a Property Get that returns an object returned (CompiledCallee.ReturnsObject).
    private VBTypedValue? _returnedObject;

    /// <summary>
    /// Takes what the call just made returned, of a function or a <c>Property Get</c> that returns an object (<see cref="CompiledCallee.ReturnsObject"/>):
    /// the statement's temporaries hold it until the statement is over, as they hold what a call the interpreter makes returns (MS-VBAL §5.3.1.10).
    /// </summary>
    /// <param name="cell">A cell that refers to the object, as an object variable's does, which compiled code reads a member of the object through; the
    /// reference is the temporary's, and not counted again.</param>
    /// <returns>The object, as the call returned it, which the interpreter is given when the statement is handed to it (<see cref="KnowObject"/>).</returns>
    public object? TakeReturnedObject(out object? cell)
    {
        var returned = _returnedObject;
        _returnedObject = null;
        cell = returned is null ? null : new ValueBindingHandle(returned.RuntimeValue);
        return returned;
    }

    // the frame given what compiled code evaluated of the instruction at pc, which the interpreter runs from the middle (inside); nothing, when it runs the
    // instruction from its start (with the error of a call), which the activation's handler is given. False when what was evaluated names nothing of the
    // instruction: the interpreter would evaluate it again, and make its calls a second time.
    private bool HandKnowns(CallStackFrame frame, InstructionList list, int pc, bool inside)
    {
        if (!HasKnowns)
        {
            return true;
        }

        try
        {
            if (!inside)
            {
                return true;
            }

            if (pc < list.Items.Length && list.Items[pc].Node is { } statement
                && KnownOperands.Of(statement, (IReadOnlyList<(int, ScalarPayload)>?)_knowns ?? [], _knownObjects) is { } operands)
            {
                frame.Know(operands);
                return true;
            }

            Debug.Fail($"what compiled code evaluated of the instruction at {pc} names nothing of it");
            return false;
        }
        finally
        {
            _knowns?.Clear();
            _knownObjects?.Clear();
        }
    }

    private bool HasKnowns => _knowns is { Count: > 0 } || _knownObjects is { Count: > 0 };
}
