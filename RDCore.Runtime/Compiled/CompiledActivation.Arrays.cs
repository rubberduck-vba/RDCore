using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.Runtime.Compiled;

// the arrays of an activation, held in the cells its frame holds them in.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// The cell the frame holds the array parameter or variable in <paramref name="slot"/> in: what compiled code finds an element in
    /// (<see cref="ArrayOperations"/>), for as long as the interpreter runs nothing of the activation. A parameter is in its caller's variable's cell, as a
    /// <c>Variant</c>'s is (<see cref="VariantCellOf"/>). A <c>ReDim</c> stores the array it makes in the same cell.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the frame holds it in no cell of a value: compiled code finds no element in it, and the interpreter runs each statement that
    /// reads or writes one.
    /// </returns>
    public object? ArrayCellOf(int slot)
    {
        var frame = Frame;
        return (frame.TryGetSlotCell(slot, out var cell) || frame.TryResolve(_body.SlotSymbols[slot], out cell)) && cell is ValueBindingHandle handle ? handle : null;
    }

    /// <summary>
    /// The cell of the array in <paramref name="slot"/> (<see cref="ArrayCellOf"/>), found again after the interpreter ran an instruction (which may have
    /// given the variable another array) or the program waited.
    /// </summary>
    /// <returns><see langword="false"/> when the frame cannot be read: the activation is then the interpreter's, from where it is.</returns>
    public bool TryReloadArrayCell(int slot, out object? cell)
    {
        cell = null;
        if (FailsAccessAtRandom())
        {
            return false;
        }

        cell = ArrayCellOf(slot);
        return true;
    }

    /// <summary>
    /// Runs the <c>ReDim</c> or the <c>Erase</c> at <paramref name="pc"/> as the interpreter runs the statement (MS-VBAL §5.4.3.3-4): a support call, with
    /// every variable written into the frame, where its bounds are read. The frame says the instruction is the one being run, as it does of a call.
    /// </summary>
    /// <returns>
    /// Whether the statement completed. When not, the error it raised is the interpreter's to give the activation's handler, as a call's is
    /// (<see cref="AfterFailedCall"/>): the statement is not run again.
    /// </returns>
    public bool RunArrayStatement(int pc)
    {
        SetPc(pc);
        _failed = null;
        _suspensionsBeforeCall = Suspensions;
        var outcome = _executor.Statements.Execute(_session, _context, Body.Items[pc].Node!);
        if (outcome.Kind is RuntimeExecutionOutcomeKind.Next)
        {
            return true;
        }

        _failed = outcome.Kind is RuntimeExecutionOutcomeKind.Error ? outcome : RuntimeExecutionOutcome.InternalError;
        return false;
    }
}
