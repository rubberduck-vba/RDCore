using RDCore.SDK.Model.Values.Runtime;
using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call of a function of the standard library inside an expression: the call made as the interpreter makes it
// (CompiledActivation.CallLibrary), and known once it is made, as a call of the workspace is (EmitNestedCall).
internal sealed partial class ProcedureEmitter
{
    // the arguments evaluated into locals before anything is called (EmitLibraryArguments); then the call, of which an error, or what is no payload of the
    // kind it returns, gives the interpreter the call (CallFailedAt). Inside an instruction that calls inside an expression, the call is known once it is
    // made: when something after it fails, the interpreter is given what it returned, as it is given what a call of the workspace returned.
    private Operand EmitLibraryCall(LibraryCallNode node, Label failure)
    {
        var offset = _knowingAt;
        var (payloads, values) = EmitLibraryArguments(node.Arguments, failure);
        var integral = _il.DeclareLocal(typeof(long));
        var floating = _il.DeclareLocal(typeof(double));
        LoadLibraryArguments(offset, node.Library, payloads, values);
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

    // the arguments evaluated as EmitLibraryCall evaluates them, then the call, into result: what is no payload of a scalar subtype (Empty, a String) gives
    // the interpreter the call, as an error does. The call is not known: the interpreter makes it again, given what was known of its arguments.
    private void EmitLibraryVariantCall(LibraryVariantNode node, LocalBuilder result, Label failure)
    {
        var offset = _knowingAt;
        var (payloads, values) = EmitLibraryArguments(node.Arguments, failure);
        LoadLibraryArguments(offset, node.Library, payloads, values);
        _il.Emit(OpCodes.Ldloca, result);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.CallLibraryVariant))!);
        _il.Emit(OpCodes.Brfalse, Fail(CallFailedAt(offset)));
    }

    // the arguments of a call, in order (MS-VBAL §5.3.1.11), each into a local: a payload, held in a Variant of its own subtype; or, of a Variant variable
    // (VariantValueNode), what it holds when it is read, which is read once, where it is in the order.
    private (LocalBuilder[] Payloads, LocalBuilder?[] Values) EmitLibraryArguments(IReadOnlyList<VariantNode> arguments, Label failure)
    {
        var payloads = new LocalBuilder[arguments.Count];
        var values = new LocalBuilder?[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] is VariantValueNode held)
            {
                payloads[i] = _il.DeclareLocal(typeof(VariantPayload));
                values[i] = _il.DeclareLocal(typeof(IRuntimeValue));
                _il.Emit(OpCodes.Ldloc, _variants[held.Slot]);
                _il.Emit(OpCodes.Call, Variants.GetMethod(nameof(VariantOperations.ValueOf))!);

                // a Variant the frame holds in no Variant's cell (a parameter passed a String, an error, an object or nothing) is read from the frame.
                var read = _il.DefineLabel();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Brtrue, read);
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldc_I4, held.Slot);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.VariantValueOf))!);
                _il.MarkLabel(read);
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Stloc, values[i]!);
                _il.Emit(OpCodes.Brfalse, failure);
                continue;
            }

            payloads[i] = EmitVariant(arguments[i], failure);
        }

        return (payloads, values);
    }

    // what CallLibrary and CallLibraryVariant are given before what they return: the call, the payloads of the arguments, and the values of those that are
    // given what a Variant variable holds.
    private void LoadLibraryArguments(int offset, int library, LocalBuilder[] payloads, LocalBuilder?[] values)
    {
        var none = payloads.Length < 3 ? _il.DeclareLocal(typeof(VariantPayload)) : null;
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, offset);
        _il.Emit(OpCodes.Ldc_I4, library);
        _il.Emit(OpCodes.Ldc_I4, payloads.Length);
        for (var i = 0; i < 3; i++)
        {
            _il.Emit(OpCodes.Ldloc, i < payloads.Length ? payloads[i] : none!);
        }

        for (var i = 0; i < 3; i++)
        {
            if (i < values.Length && values[i] is { } value)
            {
                _il.Emit(OpCodes.Ldloc, value);
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
            }
        }
    }
}
