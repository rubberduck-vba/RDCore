namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Whether a program is built to be debugged, or to be run: what the built-in <c>DEBUG</c> conditional-compilation constant is, and whether <c>Stop</c> and the
/// members of the <c>Debug</c> object do anything.
/// </summary>
/// <remarks>
/// 🎯 An RD-VBA dial. MS-VBA has no such thing in the editor, but a compiled VB6 executable is a release build: it has no debugger, so its <c>Stop</c> ends
/// the program as <c>End</c> does. The configuration applies the same way whatever engine runs the code (<see cref="VBExecutionEngine"/>).
/// </remarks>
public enum VBBuildConfiguration
{
    /// <summary>
    /// <c>DEBUG</c> is <c>True</c>; <c>Stop</c> breaks, <c>Debug.Print</c> prints and <c>Debug.Assert</c> asserts.
    /// </summary>
    /// <remarks>
    /// The default: a development tool runs what is being developed.
    /// </remarks>
    Debug = 0,

    /// <summary>
    /// <c>DEBUG</c> is <c>False</c>; a <c>Stop</c> statement ends the program as <c>End</c> does, and every <c>Debug.*</c> call is left out of the program,
    /// its arguments unevaluated.
    /// </summary>
    Release = 1,
}
