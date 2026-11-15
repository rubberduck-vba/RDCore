namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// An activation on the call stack that has no frame yet: one compiled code made of a call it made directly, which is given its frame
/// only when something is to see it (<see cref="RuntimeCallStack.MaterializeLights"/>).
/// </summary>
internal interface ILightActivation
{
    /// <summary>
    /// The frame of the activation, made as a call through the invoker would have made it, saying what the activation is at: the instruction of the call it is
    /// making, and its function result variable as compiled code assigned it. Making it changes nothing: the activation is held in it once the stack has it
    /// (<see cref="HeldIn"/>).
    /// </summary>
    CallStackFrame MakeFrame();

    /// <summary>The activation is held in <paramref name="frame"/>, the one <see cref="MakeFrame"/> made, which the stack now has, from now on.</summary>
    void HeldIn(CallStackFrame frame);
}
