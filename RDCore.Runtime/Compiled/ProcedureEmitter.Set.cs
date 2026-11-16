using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a Set statement.
internal sealed partial class ProcedureEmitter
{
    // CompiledActivation.TrySet of the cells the activation holds the objects in, or TrySetResult: a Set it declines is the interpreter's alone, and compiled
    // code goes on after it. A Set given what a call returns makes the call first (EmitObjectCall), and one given a New makes the object first (EmitNew):
    // one it declines after is the interpreter's with the object known (Fail), which does not make the call, or the object, again.
    private void EmitSet(SetStep set)
    {
        var returned = set.SourceCall is { } call ? EmitObjectCall(call, FailureAt(set.Offset))
            : set.SourceNew is { } made ? EmitNew(made)
            : null;
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, set.Offset);
        if (set.Target != SetStep.Result)
        {
            _il.Emit(OpCodes.Ldc_I4, set.Target);
            _il.Emit(OpCodes.Ldloc, _objects[set.Target]);
        }

        if (returned is not null || set.Source is not null)
        {
            _il.Emit(OpCodes.Ldloc, returned ?? _objects[set.Source!.Value]);
            _il.Emit(OpCodes.Ldc_I4_0);
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldc_I4_1);
        }

        _il.Emit(OpCodes.Call, Activation.GetMethod(set.Target == SetStep.Result ? nameof(CompiledActivation.TrySetResult) : nameof(CompiledActivation.TrySet))!);
        _il.Emit(OpCodes.Brfalse, Fail(RecoverAt(set.Offset)));
    }

    // the object made as the interpreter makes it, its Initialize run (CompiledActivation.New), and taken into a cell of its own, which the interpreter is
    // given known from then on, as what a call returned is (EmitObjectCall). An error making it raises, or one its Initialize leaves unhandled, is the
    // instruction's, which the interpreter is given at the New as it is given a call's (CallFailedAt).
    private LocalBuilder EmitNew(NewNode made)
    {
        var offset = _knowingAt;
        var returned = _il.DeclareLocal(typeof(object));
        var cell = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, offset);
        _il.Emit(OpCodes.Ldc_I4, made.Made);
        _il.Emit(OpCodes.Ldloca, cell);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.New))!);
        _il.Emit(OpCodes.Stloc, returned);
        _il.Emit(OpCodes.Ldloc, returned);
        _il.Emit(OpCodes.Brfalse, Fail(CallFailedAt(offset)));
        _called = true;
        _known.Add(new KnownLocal(made.Syntax, default, returned, IsObject: true));
        EmitAfterNestedCall(offset);
        return cell;
    }
}
