namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Encodes the <em>state</em> of an <em>execution session</em>.
/// </summary>
public enum ExecutionSessionState
{
    /// <summary>
    /// Session is currently running uninterrupted.
    /// </summary>
    Running,
    /// <summary>
    /// Session has completed.
    /// </summary>
    Completed,
    /// <summary>
    /// Session is in debug/break mode and must be manually stepped or resumed.
    /// </summary>
    Break,
}

/// <summary>
/// Encapsulates an <em>execution session</em>.
/// </summary>
public interface IExecutionSession
{
    /// <summary>
    /// Gets the current <see cref="ExecutionSessionState"/> value for this session.
    /// </summary>
    ExecutionSessionState State { get; }

    /// <summary>
    /// Gets the current <see cref="IStackFrame"/> from the <em>call stack</em>.
    /// </summary>
    IStackFrame Frame { get; }
    /// <summary>
    /// Retrieves the current stack from the execution context.
    /// </summary>
    /// <returns>Returns an <em>ordered enumerable</em> of <see cref="IStackFrame"/>, with the top-most (current) frame at index 0.</returns>
    IOrderedEnumerable<IStackFrame> GetCurrentStack();

    /// <summary>
    /// Advances execution by a single step.
    /// </summary>
    /// <remarks>
    /// ℹ️ In the classic VBIDE, a debugger command binds this action to the <kbd>F8</kbd> key; in Visual Studio, that's <kbd>F11</kbd>.
    /// </remarks>
    void StepInto();
    /// <summary>
    /// Advances execution into the next <em>statement</em>.
    /// </summary>
    /// <remarks>
    /// ℹ️ In the classic VBIDE, a debugger command binds this action to the <kbd>Shift</kbd>+<kbd>F8</kbd> hotkey; in Visual Studio, that's <kbd>F10</kbd>.
    /// </remarks>
    void StepOver();
    /// <summary>
    /// Advances execution to the next statement in the current scope, stepping <em>over</em> any statements in-between.
    /// </summary>
    /// <remarks>
    /// ℹ️ In the classic VBIDE, a debugger command binds this action to the <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F8</kbd> hotkey.
    /// </remarks>
    void StepOut();
}

