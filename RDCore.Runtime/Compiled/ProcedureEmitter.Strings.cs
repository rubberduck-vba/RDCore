using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

internal sealed partial class ProcedureEmitter
{
    // the String slots, each by the local the cell its frame holds it in is held in.
    private readonly Dictionary<int, LocalBuilder> _texts = [];

    // the Strings a Let appends to or a Mid statement changes, each by the local its appender is held in: null until its first append or Mid statement.
    private readonly Dictionary<int, LocalBuilder> _appenders = [];
    private static readonly Type Strings = typeof(StringOperations);

    private void DeclareTextCells()
    {
        foreach (var slot in _analysis.TextSlots)
        {
            _texts[slot] = _il.DeclareLocal(typeof(object));
        }

        foreach (var slot in _analysis.AppendedTextSlots)
        {
            _appenders[slot] = _il.DeclareLocal(typeof(TextAppender));
        }
    }

    // what the appender of a String holds, written into its cell before anything else reads it: compiled code, a callee given it by reference, the
    // interpreter, a debugger (StringOperations.WriteAppended).
    private void EmitWriteAppended(int slot)
    {
        if (_appenders.TryGetValue(slot, out var appender))
        {
            _il.Emit(OpCodes.Ldloc, appender);
            _il.Emit(OpCodes.Ldloc, _texts[slot]);
            _il.Emit(OpCodes.Call, Strings.GetMethod(nameof(StringOperations.WriteAppended))!);
        }
    }

    private void EmitWriteAppended()
    {
        foreach (var slot in _appenders.Keys)
        {
            EmitWriteAppended(slot);
        }
    }

    // the texts appended, each evaluated before any is appended, so that the interpreter, given the statement when one fails, finds the String as it was;
    // then joined, and appended at once (StringOperations.TryAppend).
    private void EmitTextAppend(TextAppendStep append)
    {
        var failure = RecoverAt(append.Offset);
        var texts = append.Appended.Select(appended => EmitTextInto(appended, failure)).ToList();
        var text = texts[0];
        foreach (var next in texts.Skip(1))
        {
            var joined = _il.DeclareLocal(typeof(string));
            _il.Emit(OpCodes.Ldloc, text);
            _il.Emit(OpCodes.Ldloc, next);
            _il.Emit(OpCodes.Call, Strings.GetMethod(nameof(StringOperations.Concat))!);
            _il.Emit(OpCodes.Stloc, joined);
            text = joined;
        }

        _il.Emit(OpCodes.Ldloca, _appenders[append.Slot]);
        _il.Emit(OpCodes.Ldloc, _texts[append.Slot]);
        _il.Emit(OpCodes.Ldloc, text);
        _il.Emit(OpCodes.Call, Strings.GetMethod(nameof(StringOperations.TryAppend))!);
        _il.Emit(OpCodes.Brfalse, failure);
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
        _il.Emit(OpCodes.Brfalse, Fail(failure));
    }

    // the Mid statement of a variable in its appender, made of its cell when it does not hold it (CompiledActivation.TryMid); of a parameter by reference,
    // in its cell (TryMidInCell). The start, the length and the value are evaluated before it is changed; one the statement declines is the
    // interpreter's, given what the appender holds (EmitSpill).
    private void EmitMid(MidStep mid)
    {
        var failure = RecoverAt(mid.Offset);
        var start = EmitPayload(mid.Start, failure);
        var length = mid.Length is { } given ? EmitPayload(given, failure) : default(Operand?);
        var value = EmitTextInto(mid.Value, failure);
        var buffered = _appenders.TryGetValue(mid.Slot, out var appender);
        _il.Emit(OpCodes.Ldarg_0);
        if (buffered)
        {
            _il.Emit(OpCodes.Ldloca, appender!);
        }

        _il.Emit(OpCodes.Ldloc, _texts[mid.Slot]);
        Load(start, mid.Start.Kind);
        if (length is { } counted)
        {
            Load(counted, mid.Length!.Kind);
            _il.Emit(OpCodes.Ldc_I4_1);
        }
        else
        {
            _il.Emit(OpCodes.Ldc_I8, 0L);
            _il.Emit(OpCodes.Ldc_I4_0);
        }

        _il.Emit(OpCodes.Ldloc, value);
        _il.Emit(OpCodes.Call, Activation.GetMethod(buffered ? nameof(CompiledActivation.TryMid) : nameof(CompiledActivation.TryMidInCell))!);
        _il.Emit(OpCodes.Brfalse, Fail(failure));
    }

    // the text assigned to the function result variable where its frame holds it (CompiledActivation.TryAssignResultText).
    private void EmitTextResultLet(TextResultLetStep resultLet)
    {
        var failure = RecoverAt(resultLet.Offset);
        var text = EmitTextInto(resultLet.Value, failure);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, text);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryAssignResultText))!);
        _il.Emit(OpCodes.Brfalse, Fail(failure));
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
    // stack, and given what was evaluated before it when a call inside the statement was made (Fail).
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
                EmitWriteAppended(slot.Slot);
                _il.Emit(OpCodes.Ldloc, _texts[slot.Slot]);
                _il.Emit(OpCodes.Ldloca, text);
                _il.Emit(OpCodes.Call, Strings.GetMethod(nameof(StringOperations.TryRead))!);
                _il.Emit(OpCodes.Brfalse, Fail(failure));
                break;

            case FieldTextNode field:
                EmitFieldText(field, text, failure);
                break;

            case MemberTextNode member:
                EmitMemberText(member, text, failure);
                break;

            case TextSelectorNode selector:
                _il.Emit(OpCodes.Ldloc, _selects[selector.Select].Selector);
                _il.Emit(OpCodes.Stloc, text);
                break;

            case ErrTextNode err:
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(err.Source ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ldloca, text);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryReadErrText))!);
                _il.Emit(OpCodes.Brfalse, Fail(failure));
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
                _il.Emit(OpCodes.Brfalse, Fail(failure));
                break;

            case WholeTextNode whole:
                Load(EmitPayload(whole.Value, failure), whole.Value.Kind);
                _il.Emit(OpCodes.Call, Strings.GetMethod(nameof(StringOperations.OfWhole))!);
                _il.Emit(OpCodes.Stloc, text);
                break;

            case TypeNameTextNode typeName:
                EmitTypeName(typeName, text);
                break;

            case IntrinsicTextNode typed:
                EmitIntrinsicText(typed, text, failure);
                break;

            case LibraryTextNode library:
                EmitLibraryText(library, text, failure);
                break;

            case LibraryVariantTextNode converting:
                EmitLibraryVariantText(converting, text, failure);
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

    // the arguments evaluated as a call of a function that returns a Variant evaluates them (EmitLibraryArguments); then the call
    // (CompiledActivation.TryCallLibraryVariantText). An error, or a result other than a String compiled code holds, gives the interpreter the call.
    private void EmitLibraryVariantText(LibraryVariantTextNode node, LocalBuilder text, Label failure)
    {
        var offset = _knowingAt;
        var (payloads, values) = EmitLibraryArguments(node.Arguments, failure);
        LoadLibraryArguments(offset, node.Library, payloads, values);
        _il.Emit(OpCodes.Ldloca, text);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryCallLibraryVariantText))!);
        _il.Emit(OpCodes.Brfalse, Fail(CallFailedAt(offset)));
    }
}
