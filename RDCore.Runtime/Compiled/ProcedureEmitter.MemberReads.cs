using RDCore.SDK.Model.Symbols;
using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a public variable of an object it reads, and of Err.Number.
internal sealed partial class ProcedureEmitter
{
    // Err.Number, where the session holds it (CompiledActivation.TryReadErrNumber).
    private Operand EmitErrNumber(Label failure)
    {
        var number = _il.DeclareLocal(typeof(long));
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloca, number);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryReadErrNumber))!);
        _il.Emit(OpCodes.Brfalse, Fail(failure));
        return new Operand(number, 0, 0);
    }

    // the variable read of the object a variable or an object field of a record refers to, or of the object of a With block
    // (CompiledActivation.TryReadMemberField); one compiled code cannot read fails the instruction, which the interpreter makes from its start, or from where
    // what was evaluated of it is known.
    private Operand EmitMemberField(MemberFieldNode field, Label failure)
    {
        var integral = _il.DeclareLocal(typeof(long));
        var floating = _il.DeclareLocal(typeof(double));
        var held = field.ReceiverField is { } path ? EmitObjectFieldCell(new ObjectFieldNode(path)) : null;
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, field.Field);
        if (field.WithAt is { } with)
        {
            _il.Emit(OpCodes.Ldc_I4, with);
        }
        else
        {
            _il.Emit(OpCodes.Ldloc, held ?? _objects[field.Receiver!.Value]);
        }

        _il.Emit(OpCodes.Ldloca, integral);
        _il.Emit(OpCodes.Ldloca, floating);
        _il.Emit(OpCodes.Call, Activation.GetMethod(field.WithAt is null
            ? nameof(CompiledActivation.TryReadMemberField)
            : nameof(CompiledActivation.TryReadMemberFieldOfWith))!);
        _il.Emit(OpCodes.Brfalse, Fail(field.Receiver is { } receiver ? ReceiverFailure(receiver, failure) : failure));
        return new Operand(ScalarKinds.IsIntegral(field.Kind) ? integral : floating, 0, 0);
    }

    // the text of a String public variable of an object, into text, read as EmitMemberField reads a scalar one (CompiledActivation.TryReadMemberText).
    private void EmitMemberText(MemberTextNode member, LocalBuilder text, Label failure)
    {
        var held = member.ReceiverField is { } path ? EmitObjectFieldCell(new ObjectFieldNode(path)) : null;
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, member.Field);
        if (member.WithAt is { } with)
        {
            _il.Emit(OpCodes.Ldc_I4, with);
        }
        else
        {
            _il.Emit(OpCodes.Ldloc, held ?? _objects[member.Receiver!.Value]);
        }

        _il.Emit(OpCodes.Ldloca, text);
        _il.Emit(OpCodes.Call, Activation.GetMethod(member.WithAt is null
            ? nameof(CompiledActivation.TryReadMemberText)
            : nameof(CompiledActivation.TryReadMemberTextOfWith))!);
        _il.Emit(OpCodes.Brfalse, member.Receiver is { } receiver ? ReceiverFailure(receiver, failure) : failure);
    }

    // where an instruction goes when what it reads or calls a member of is the default instance of a class and no member compiled code calls or reads: one
    // that holds no object yet, or none since a Set of it to Nothing, is no error, but an object the interpreter makes (MS-VBAL §5.2.3.1.4) - it runs the
    // instruction alone, and compiled code goes on after it. Of any other object, failure.
    private Label ReceiverFailure(int receiver, Label failure)
        => _analysis.SlotSymbols.TryGetValue(receiver, out var symbol) && symbol is VBPredeclaredInstanceSymbol ? RecoverAt(_knowingAt) : failure;
}
