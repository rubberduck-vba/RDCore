using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a Let of a member of an object.
internal sealed partial class ProcedureEmitter
{
    // the value evaluated - one the evaluation on payloads declines is the interpreter's statement alone, from its start, nothing of it done - then the member
    // assigned by the interpreter's assignment, as a support call (CompiledActivation.LetMember), every variable written into the frame before it: a
    // Property Let it calls runs user code, which a debugger may stop in. The error it raises is given to the activation's handler as a call's is
    // (CallFailedAt), the statement not run again. Every variable is read back after it, as after a call the program waited in; one that is not read back
    // leaves the activation to the interpreter from after the statement, and so does the interpreter wanted then, as after a call.
    private void EmitMemberLet(MemberLetStep let)
    {
        var value = _il.DeclareLocal(typeof(VariantPayload));
        EmitVariantInto(let.Value, value, RecoverAt(let.Offset));
        EmitSpill();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, let.Offset);
        _il.Emit(OpCodes.Ldloc, value);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.LetMember))!);
        _il.Emit(OpCodes.Brfalse, CallFailedAt(let.Offset));

        // asked as after a call, which the random failed accesses fail the first read-back after a wait by, whatever the seed.
        var read = _il.DefineLabel();
        var unread = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.ReadsBackAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Pop);
        EmitReload(unread);
        _il.Emit(OpCodes.Br, read);
        _il.MarkLabel(unread);
        _il.Emit(OpCodes.Ldc_I4, let.Offset + 1);
        _il.Emit(OpCodes.Stloc, _pc);
        _il.Emit(OpCodes.Br, _unread);
        _il.MarkLabel(read);

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.WantsInterpreterAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Brtrue, WalkBackAt(let.Offset + 1));
    }
}
