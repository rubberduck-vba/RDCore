namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// What a <see cref="ControlFlowGraph"/> could not describe exactly, so that whoever asks it a question knows which answers to trust.
/// </summary>
/// <remarks>
/// A graph with no imprecision has every edge control can take, and no edge it cannot (a condition that is not a literal can go either way). A graph that has some
/// is still an answer: it says <em>what</em> it approximates, and an analysis that states a fact decides whether the approximation can make it a lie.
/// </remarks>
[Flags]
public enum ControlFlowImprecision
{
    /// <summary>
    /// Nothing is approximated.
    /// </summary>
    None = 0,

    /// <summary>
    /// A jump lands nowhere: the label it names is not defined, which is a compile error. The graph has no edge for it.
    /// </summary>
    UnresolvedJump = 1 << 0,

    /// <summary>
    /// The procedure uses <c>GoSub</c> or <c>On…GoSub</c>, and a <c>Return</c> goes back to the instruction after <em>any</em> of them: which one is known only at run time.
    /// The graph has the edges that are possible, and some that are not.
    /// </summary>
    Subroutines = 1 << 1,

    /// <summary>
    /// The procedure uses <c>Resume</c> or <c>Resume Next</c>, which continue at the statement that raised the error: which one is known only at run time. The graph has no edge
    /// for it, so a path that only exists through one is not in it.
    /// </summary>
    /// <remarks>
    /// The data flow analyses do not have this imprecision. <see cref="ErrorHandlingStates"/> finds the instructions that can raise an error into a handler, and the
    /// edges it is built with (<see cref="FlowEdges"/>) go from a <c>Resume</c> back to each of them, and from a <c>Resume Next</c> to the statement after each of them.
    /// <see cref="LiveVariables"/> and <see cref="ReachingDefinitions"/> walk those edges. Only a question asked of the graph's own edges, as
    /// <see cref="ReturnValueAnalysis"/> asks one, misses the paths.
    /// </remarks>
    Resumption = 1 << 2,
}
