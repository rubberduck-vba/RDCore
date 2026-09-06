namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Encapsulates the execution environment for a bound node evaluation.
/// </summary>
public interface IVBExecutionContext
{
    /// <summary>
    /// The execution session this evaluation runs in.
    /// </summary>
    IRuntimeSession Session { get; }
}
