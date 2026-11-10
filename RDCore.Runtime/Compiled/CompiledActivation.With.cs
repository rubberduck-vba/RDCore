using RDCore.Runtime.Execution;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a With block.
public sealed partial class CompiledActivation
{
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
