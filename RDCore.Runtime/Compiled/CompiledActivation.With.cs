using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a With block.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// The <c>With</c> statement of the block opened at <paramref name="opener"/> of the object held in <paramref name="cell"/> (MS-VBAL §5.4.2.21), as the
    /// interpreter runs it (<c>ProcedureExecutor.ExecuteWith</c>): the block's object is the reference the cell held, which a <c>Set</c> of the variable
    /// it was read from inside the block does not change, and the block holds it until its <c>End With</c>, wherever the program jumps meanwhile.
    /// </summary>
    /// <param name="opener">The offset of the <c>With</c> statement.</param>
    /// <param name="cell">The cell of the object variable, or of the object field, the call or the <c>New</c> the statement reads.</param>
    /// <param name="makesObject">
    /// Whether the cell is the one of an object variable declared <c>As New</c>, which the interpreter reads while it holds <c>Nothing</c>: reading it
    /// makes the object the variable is given (MS-VBAL §5.2.3.1.4).
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the interpreter runs the statement: under a debugger or an analysis, a cell that holds no reference compiled code reads,
    /// one of a variable declared <c>As New</c> that holds <c>Nothing</c>, or a block opened again before its <c>End With</c> whose letting go of what it
    /// held would destroy an object, whose <c>Terminate</c> is the interpreter's to run (MS-VBAL §5.3.1.10).
    /// </returns>
    public bool TryWith(int opener, object? cell, bool makesObject)
    {
        if (FailsAccessAtRandom() || _executor.Observation is not null || _halt.Gate is not null || !ObjectOperations.TryRead(cell, out var instance)
            || makesObject && instance.Equals(VBObjectValue.Nothing.Value))
        {
            return false;
        }

        var frame = Frame;
        if (frame.BlockHolderOf(opener) is { } held && Destroys(ObjectReferences.HeldIn(_session, held)))
        {
            return false;
        }

        var target = instance.Equals(VBObjectValue.Nothing.Value) ? VBObjectValue.Nothing : new VBObjectValue(instance);
        frame.SetBlockState(opener, target);
        var holder = frame.BlockHolder(opener, end: null);
        var before = ObjectReferences.HeldIn(_session, holder);
        holder.Store(target.RuntimeValue, true);
        ObjectReferences.Rebind(_session, holder, before);
        return true;
    }

    /// <summary>
    /// The <c>End With</c> of the block opened at <paramref name="opener"/> (MS-VBAL §5.4.2.21), as the interpreter runs it: the block has no object from
    /// here on, which a jump back into it finds, and what it held is let go of.
    /// </summary>
    /// <param name="opener">The offset of the <c>With</c> statement of the block.</param>
    /// <returns>
    /// <see langword="false"/> when the interpreter runs the statement: under a debugger, or when letting go of what the block held would destroy an object,
    /// whose <c>Terminate</c> is the interpreter's to run (MS-VBAL §5.3.1.10).
    /// </returns>
    public bool TryEndWith(int opener)
    {
        if (FailsAccessAtRandom() || _executor.Observation is not null)
        {
            return false;
        }

        var frame = Frame;
        if (frame.BlockHolderOf(opener) is { } holder && Destroys(ObjectReferences.HeldIn(_session, holder)))
        {
            return false;
        }

        frame.ClearBlockState(opener);
        if (frame.TakeBlockHolder(opener) is { } held)
        {
            ObjectReferences.Release(_session, held);
        }

        return true;
    }
}
