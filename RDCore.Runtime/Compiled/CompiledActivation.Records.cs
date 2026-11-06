using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.Runtime.Compiled;

public sealed partial class CompiledActivation
{
    /// <summary>
    /// The cell the frame holds the record parameter or variable in <paramref name="slot"/> in (IR lowering S2.6a): the holder compiled code finds the record
    /// in, and its fields (<see cref="RecordOperations"/>), for as long as the interpreter runs nothing of the activation. A parameter, which a record always
    /// is passed by reference (MS-VBAL §5.3.1.11), is in the cell of its caller's variable, as a <c>Variant</c>'s is (<see cref="VariantCellOf"/>).
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the frame holds it in no cell: compiled code reads nothing of it, and the interpreter runs each statement that does.
    /// </returns>
    public object? RecordCellOf(int slot)
    {
        var frame = Frame;
        return (frame.TryGetSlotCell(slot, out var cell) || frame.TryResolve(_body.SlotSymbols[slot], out cell)) && cell is IBindingHandle handle ? handle : null;
    }

    /// <summary>
    /// The holder of the record in <paramref name="slot"/> (<see cref="RecordCellOf"/>), found again after the interpreter ran an instruction or the program
    /// waited.
    /// </summary>
    /// <returns><see langword="false"/> when the frame cannot be read: the activation is then the interpreter's, from where it is.</returns>
    public bool TryReloadRecordCell(int slot, out object? cell)
    {
        cell = null;
        if (FailsAccessAtRandom())
        {
            return false;
        }

        cell = RecordCellOf(slot);
        return true;
    }
}
