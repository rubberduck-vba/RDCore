using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

internal sealed partial class ProcedureEmitter
{
    // the String slots, each by the local the cell its frame holds it in is held in.
    private readonly Dictionary<int, LocalBuilder> _texts = [];
    private static readonly Type Strings = typeof(StringOperations);

    private void DeclareTextCells()
    {
        foreach (var slot in _analysis.TextSlots)
        {
            _texts[slot] = _il.DeclareLocal(typeof(object));
        }
    }

    // each String's cell, as the frame holds it: none, for one held otherwise, which compiled code then reads and writes nothing of - each statement that
    // does is the interpreter's.
    private void EmitTextCells()
    {
        foreach (var (slot, cell) in _texts)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.StringCellOf))!);
            _il.Emit(OpCodes.Stloc, cell);
        }
    }

    // each String's cell, found again: what the interpreter ran may have bound the slot to another.
    private void EmitReloadTextCells(Label unread)
    {
        foreach (var (slot, cell) in _texts)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Ldloca, cell);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryReloadStringCell))!);
            _il.Emit(OpCodes.Brfalse, unread);
        }
    }

    // the text written into the String's cell as it is; one compiled code reads or writes nothing of, the interpreter's.
    private void EmitTextLet(TextLetStep textLet)
    {
        var failure = RecoverAt(textLet.Offset);
        var text = EmitTextInto(textLet.Value, failure);
        _il.Emit(OpCodes.Ldloc, _texts[textLet.Slot]);
        _il.Emit(OpCodes.Ldloc, text);
        _il.Emit(OpCodes.Call, Strings.GetMethod(nameof(StringOperations.Write))!);
        _il.Emit(OpCodes.Brfalse, failure);
    }

    // the text assigned to the function result variable where its frame holds it (CompiledActivation.TryAssignResultText).
    private void EmitTextResultLet(TextResultLetStep resultLet)
    {
        var failure = RecoverAt(resultLet.Offset);
        var text = EmitTextInto(resultLet.Value, failure);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, text);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryAssignResultText))!);
        _il.Emit(OpCodes.Brfalse, failure);
    }

    // the bool of two texts compared, on the stack (CompiledActivation.CompareText).
    private void EmitTextCompare(TextCompareNode compared, Label failure)
    {
        var left = EmitTextInto(compared.Left, failure);
        var right = EmitTextInto(compared.Right, failure);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, (int)compared.Operator);
        _il.Emit(OpCodes.Ldloc, left);
        _il.Emit(OpCodes.Ldloc, right);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.CompareText))!);
    }

    // the text an expression of Strings is, in a local of its own; a String whose cell holds no text compiled code reads goes to failure, with nothing on the
    // stack.
    private LocalBuilder EmitTextInto(TextNode node, Label failure)
    {
        var text = _il.DeclareLocal(typeof(string));
        switch (node)
        {
            case TextConstantNode { Text: { } constant }:
                _il.Emit(OpCodes.Ldstr, constant);
                _il.Emit(OpCodes.Stloc, text);
                break;

            case TextConstantNode:
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Stloc, text);
                break;

            case TextSlotNode slot:
                _il.Emit(OpCodes.Ldloc, _texts[slot.Slot]);
                _il.Emit(OpCodes.Ldloca, text);
                _il.Emit(OpCodes.Call, Strings.GetMethod(nameof(StringOperations.TryRead))!);
                _il.Emit(OpCodes.Brfalse, failure);
                break;

            case MemberTextNode member:
                EmitMemberText(member, text, failure);
                break;

            case ErrTextNode err:
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(err.Source ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ldloca, text);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryReadErrText))!);
                _il.Emit(OpCodes.Brfalse, failure);
                break;

            case ConcatNode concat:
                var left = EmitTextInto(concat.Left, failure);
                var right = EmitTextInto(concat.Right, failure);
                _il.Emit(OpCodes.Ldloc, left);
                _il.Emit(OpCodes.Ldloc, right);
                _il.Emit(OpCodes.Call, Strings.GetMethod(nameof(StringOperations.Concat))!);
                _il.Emit(OpCodes.Stloc, text);
                break;

            case ChrTextNode chr:
                var code = EmitPayload(chr.Code, failure);
                _il.Emit(OpCodes.Ldarg_0);
                Load(code, chr.Code.Kind);
                _il.Emit(OpCodes.Ldloca, text);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryChr))!);
                _il.Emit(OpCodes.Brfalse, failure);
                break;

            case WholeTextNode whole:
                Load(EmitPayload(whole.Value, failure), whole.Value.Kind);
                _il.Emit(OpCodes.Call, Strings.GetMethod(nameof(StringOperations.OfWhole))!);
                _il.Emit(OpCodes.Stloc, text);
                break;

            case TypeNameTextNode typeName:
                EmitTypeName(typeName, text);
                break;

            case LibraryTextNode library:
                EmitLibraryText(library, text, failure);
                break;
        }

        return text;
    }

    // the text of every item, evaluated before any is written, so that the interpreter, given the statement when one fails, writes none twice; then each
    // written, and the line ended (CompiledActivation.Print, PrintLine).
    private void EmitPrint(PrintStep print)
    {
        var failure = RecoverAt(print.Offset);
        var texts = new LocalBuilder[print.Items.Count];
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = EmitTextInto(print.Items[i], failure);
        }

        foreach (var text in texts)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, text);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Print))!);
        }

        if (print.EndsLine)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.PrintLine))!);
        }

        // a break asked for while the output was written is answered before the next instruction, as the interpreter answers it.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.WantsInterpreterAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Brtrue, WalkBackAt(print.Offset + 1));
    }

    // TypeName of the object an object slot holds, or of Nothing (CompiledActivation.TryTypeNameOf); a call the function fails, or a cell that holds no
    // object compiled code reads, gives the interpreter the call.
    private void EmitTypeName(TypeNameTextNode node, LocalBuilder text)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, _knowingAt);
        _il.Emit(OpCodes.Ldc_I4, node.Library);
        if (node.Slot is { } slot)
        {
            _il.Emit(OpCodes.Ldloc, _objects[slot]);
            _il.Emit(OpCodes.Ldc_I4_0);
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldc_I4_1);
        }

        _il.Emit(OpCodes.Ldloca, text);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryTypeNameOf))!);
        _il.Emit(OpCodes.Brfalse, Fail(CallFailedAt(_knowingAt)));
    }

    // the texts of the arguments, each evaluated into a local before anything is called; then the call (CompiledActivation.TryCallLibraryText), of which an
    // error, or what is no String compiled code holds, gives the interpreter the call.
    private void EmitLibraryText(LibraryTextNode node, LocalBuilder text, Label failure)
    {
        var arguments = new LocalBuilder[node.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = EmitTextInto(node.Arguments[i], failure);
        }

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, _knowingAt);
        _il.Emit(OpCodes.Ldc_I4, node.Library);
        _il.Emit(OpCodes.Ldc_I4, arguments.Length);
        for (var i = 0; i < 3; i++)
        {
            if (i < arguments.Length)
            {
                _il.Emit(OpCodes.Ldloc, arguments[i]);
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
            }
        }

        _il.Emit(OpCodes.Ldloca, text);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryCallLibraryText),
            [typeof(int), typeof(int), typeof(int), typeof(string), typeof(string), typeof(string), typeof(string).MakeByRefType()])!);
        _il.Emit(OpCodes.Brfalse, Fail(CallFailedAt(_knowingAt)));
    }
}
