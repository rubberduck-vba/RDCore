using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call of a function of the standard library inside an expression: the call made as the interpreter makes it
// (CompiledActivation.CallLibrary), and known once it is made, as a call of the workspace is (EmitNestedCall).
internal sealed partial class ProcedureEmitter
{
    // the arguments, each a Variant of its own subtype evaluated into a local, before anything is called; then the call, of which an error, or what is no
    // payload of the kind it returns, gives the interpreter the call (CallFailedAt). Inside an instruction that calls inside an expression, the call is known
    // once it is made: when something after it fails, the interpreter is given what it returned, as it is given what a call of the workspace returned.
    private Operand EmitLibraryCall(LibraryCallNode node, Label failure)
    {
        var offset = _knowingAt;
        var arguments = new LocalBuilder[node.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = EmitVariant(node.Arguments[i], failure);
        }

        var none = arguments.Length < 3 ? _il.DeclareLocal(typeof(VariantPayload)) : null;
        var integral = _il.DeclareLocal(typeof(long));
        var floating = _il.DeclareLocal(typeof(double));
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, offset);
        _il.Emit(OpCodes.Ldc_I4, node.Library);
        _il.Emit(OpCodes.Ldc_I4, arguments.Length);
        for (var i = 0; i < 3; i++)
        {
            _il.Emit(OpCodes.Ldloc, i < arguments.Length ? arguments[i] : none!);
        }

        _il.Emit(OpCodes.Ldc_I4, (int)node.Kind);
        _il.Emit(OpCodes.Ldloca, integral);
        _il.Emit(OpCodes.Ldloca, floating);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.CallLibrary))!);
        _il.Emit(OpCodes.Brfalse, Fail(CallFailedAt(offset)));

        var returned = ScalarKinds.IsIntegral(node.Kind) ? integral : floating;
        if (node.Syntax < 0)
        {
            return new Operand(returned, 0, 0);
        }

        _called = true;
        _known.Add(new KnownLocal(node.Syntax, node.Kind, returned));

        // under the random walk-back, after the call too: the instruction is the interpreter's from the middle of it, given what the call returned.
        if (CompiledExecution.WalksBackAtRandom)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.ShouldWalkBack))!.GetMethod!);
            _il.Emit(OpCodes.Brtrue, Fail(WalkBackAt(offset)));
        }

        return new Operand(returned, 0, 0);
    }

    // the arguments evaluated as EmitLibraryCall evaluates them, then the call, into result: what is no payload of a scalar subtype - Empty, a String - gives
    // the interpreter the call, as an error does. The call is not known: the interpreter makes it again, given what was known of its arguments.
    private void EmitLibraryVariantCall(LibraryVariantNode node, LocalBuilder result, Label failure)
    {
        var offset = _knowingAt;
        var arguments = new LocalBuilder[node.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = EmitVariant(node.Arguments[i], failure);
        }

        var none = arguments.Length < 3 ? _il.DeclareLocal(typeof(VariantPayload)) : null;
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, offset);
        _il.Emit(OpCodes.Ldc_I4, node.Library);
        _il.Emit(OpCodes.Ldc_I4, arguments.Length);
        for (var i = 0; i < 3; i++)
        {
            _il.Emit(OpCodes.Ldloc, i < arguments.Length ? arguments[i] : none!);
        }

        _il.Emit(OpCodes.Ldloca, result);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.CallLibraryVariant))!);
        _il.Emit(OpCodes.Brfalse, Fail(CallFailedAt(offset)));
    }
}
