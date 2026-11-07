namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// What <c>Err.Clear</c> does to the line <c>Erl</c> reports.
/// </summary>
/// <remarks>
/// An RD-VBA setting, as <see cref="VBErlLineNumbering"/> is (<strong>RD-VBAL §6.1.2.7.1.14</strong>). In MS-VBA, <c>Err.Clear</c> resets the error's number and the
/// rest of the error object, but <c>Erl</c> goes on reporting the line of the error it cleared. A <c>Resume</c> statement, an <c>On Error</c> statement and leaving the procedure
/// from an error handler do reset it, in MS-VBA as in RD-VBA, whatever this says.
/// </remarks>
public enum VBErlAfterErrClear
{
    /// <summary>
    /// <c>Err.Clear</c> resets <c>Erl</c> to <c>0</c>, with the rest of the error it clears.
    /// </summary>
    /// <remarks>
    /// The default: once there is no error, there is no line it was raised at.
    /// </remarks>
    Reset = 0,

    /// <summary>
    /// <c>Err.Clear</c> leaves <c>Erl</c> as it is, as MS-VBA does.
    /// </summary>
    /// <remarks>
    /// Choose it when a workspace's own code depends on MS-VBA's behaviour, such as a handler that clears the error before
    /// it logs the line.
    /// </remarks>
    Kept = 1,
}
