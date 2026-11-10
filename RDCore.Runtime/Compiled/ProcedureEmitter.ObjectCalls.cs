using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call that returns an object, inside an expression (ObjectCallNode): the call made, the object it returned taken into a cell
// compiled code calls a member of it or reads a variable of it through, and the statement that makes it begun before its first call and ended once it is
// over, as the interpreter begins and ends one: what its calls returned is held by its temporaries until then, and let go of there.
internal sealed partial class ProcedureEmitter
{
    // whether the step emitted is a statement that calls for an object, begun (CompiledActivation.BeginStatement) and not yet ended.
    private bool _inObjectStatement;

    // the step, begun and ended around what it does when it calls for an object: ended where it is over - after its assignment, before the jump its
    // condition decides, before a For's first test - and not where it is handed to the interpreter, which ends it.
    private void EmitObjectStatement(CompiledStep step)
    {
        if (!ProcedureAnalysis.CallsForAnObject(step))
        {
            EmitStep(step);
            return;
        }

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.BeginStatement))!);
        _inObjectStatement = true;
        try
        {
            switch (step)
            {
                case BranchStep branch:
                {
                    var otherwise = _il.DefineLabel();
                    EmitBranch(branch.Condition, branch.Negate, whenFalse: otherwise, FailureOf(branch.Condition, branch.Offset));
                    var then = _il.DefineLabel();
                    EmitEndStatement();
                    _il.Emit(OpCodes.Br, then);
                    _il.MarkLabel(otherwise);
                    EmitEndStatement();
                    _il.Emit(OpCodes.Br, _instructions[branch.Else]);
                    _il.MarkLabel(then);
                    break;
                }

                case LoopBackStep loopBack:
                {
                    EmitPoll(loopBack.Offset);
                    var past = _il.DefineLabel();
                    EmitBranch(loopBack.Condition, loopBack.Negate, whenFalse: past, FailureOf(loopBack.Condition, loopBack.Offset));
                    EmitEndStatement();
                    _il.Emit(OpCodes.Br, _instructions[loopBack.Target]);
                    _il.MarkLabel(past);
                    EmitEndStatement();
                    break;
                }

                // a For opener ends before its first test (EmitForOpen).
                case ForOpenStep:
                    EmitStep(step);
                    break;

                default:
                    EmitStep(step);
                    EmitEndStatement();
                    break;
            }
        }
        finally
        {
            _inObjectStatement = false;
        }
    }

    private void EmitEndStatement()
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.EndStatement))!);
    }

    // the call made as a nested call is (EmitNestedCall), then the object it returned taken into a cell of its own, which the interpreter is given known
    // from then on: the call is not made again.
    private LocalBuilder EmitObjectCall(ObjectCallNode node, Label failure)
    {
        var offset = _knowingAt;
        _ = EmitCallOf(new CallStep(offset, node.Callee, node.Arguments, CallStep.NoResult, node.Receiver)
        {
            WithAt = node.WithAt, ReceiverField = node.ReceiverField, ReceiverCall = node.ReceiverCall,
        }, failure);

        var returned = _il.DeclareLocal(typeof(object));
        var cell = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloca, cell);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TakeReturnedObject))!);
        _il.Emit(OpCodes.Stloc, returned);
        _called = true;
        _known.Add(new KnownLocal(node.Syntax, default, returned, IsObject: true));
        EmitAfterNestedCall(offset);
        return cell;
    }
}
