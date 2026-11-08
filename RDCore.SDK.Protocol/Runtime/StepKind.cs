namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// How far a program that is resumed goes before it stops again. The program only ever stops between two instructions, never inside one: a statement is not
/// half-done at a step, and a call that is stepped over is one that is made.
/// </summary>
public enum StepKind
{
    /// <summary>At the next instruction, in whichever procedure it is: a call is stepped into.</summary>
    Into,

    /// <summary>At the next instruction of the procedure the program is in, or of the one that called it if this one is over: a call is made, and stepped over.</summary>
    Over,

    /// <summary>At the next instruction of the procedure that called the one the program is in.</summary>
    Out,
}
