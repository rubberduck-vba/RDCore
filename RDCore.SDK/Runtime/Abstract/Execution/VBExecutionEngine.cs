namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// What runs the code of a program: the interpreter, or IL compiled from the same lowered code.
/// </summary>
/// <remarks>
/// 🎯 An RD-VBA dial. Both engines implement the same semantics, from the same instruction list: a program does the same thing in either, only faster in one.
/// </remarks>
public enum VBExecutionEngine
{
    /// <summary>
    /// Every procedure runs in the interpreter, instruction by instruction.
    /// </summary>
    /// <remarks>
    /// The default while the compiled engine is being built.
    /// </remarks>
    Interpreter = 0,

    /// <summary>
    /// A procedure is compiled to IL on its first call when every instruction in it can be compiled, and runs in the interpreter otherwise; a compiled
    /// procedure walks back into the interpreter when a debugger needs it to.
    /// </summary>
    Compiled = 1,
}
