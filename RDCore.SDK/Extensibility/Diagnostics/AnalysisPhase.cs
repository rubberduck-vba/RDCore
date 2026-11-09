namespace RDCore.SDK.Extensibility.Diagnostics;

/// <summary>
/// How much of what the platform can find out about a module an analysis waits for.
/// </summary>
/// <remarks>
/// <para>
/// What a module's diagnostics are made of does not all arrive at once. The syntax and what the static pass found are there as soon as the module is loaded;
/// what the language core states about the conversions and operations of its code (<see cref="Runtime"/>) takes longer, because every instruction of every
/// procedure is evaluated. A finding that is ready is not held back for one that is not, and so each is asked for in its own phase and the answers are put
/// together by whoever reports them.
/// </para>
/// <para>
/// A phase is a unit of waiting, not of importance: an analyzer belongs to the earliest phase that has everything it reads.
/// </para>
/// </remarks>
[Flags]
public enum AnalysisPhase
{
    /// <summary>
    /// The syntax tree and the facts of the host's static pass (<strong>RD-VBAL §5.0.3</strong>): ready when the module is loaded.
    /// </summary>
    Static = 1,

    /// <summary>
    /// The facts that come of evaluating the code of the module, which are ready some time after the static ones.
    /// </summary>
    Runtime = 2,

    /// <summary>
    /// Every phase: the answer is complete, and is awaited.
    /// </summary>
    All = Static | Runtime,
}
