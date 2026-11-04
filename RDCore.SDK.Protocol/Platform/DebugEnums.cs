namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// What an edit made while a program waits is to it, when it is not applied.
/// </summary>
public enum DebugEditKind
{
    /// <summary>Applying it is correct, but no build of the host does it to a program that waits yet: it is picked up when the program is run again.</summary>
    NotYet,

    /// <summary>Applying it can't be made correct: it is picked up when the program is run again.</summary>
    Restart,

    /// <summary>The code no longer compiles: there is no program to go on with until it does.</summary>
    DoesNotCompile,
}

/// <summary>
/// Which variables of a frame.
/// </summary>
public enum DebugVariableScope
{
    /// <summary>The parameters and the local variables of the procedure, and the value it returns.</summary>
    Locals,

    /// <summary>The variables of the module the procedure is declared in.</summary>
    Module,
}
