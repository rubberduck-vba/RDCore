namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// What <c>Erl</c> counts as the line a run-time error was raised at.
/// </summary>
/// <remarks>
/// 🎯 An RD-VBA dial, because MS-VBA's own answer is not a useful one. <c>Erl</c> there reports the last
/// <em>line-number label</em> it passed, and hardly any code numbers every line — so a fault in an
/// unnumbered statement is reported at whichever numbered line came before it, however far back that is,
/// and a program that numbers nothing is told every error happened at line <c>0</c>. The number is
/// truthful only for a program in which every single line is numbered, which is to say a BASIC program.
/// </remarks>
public enum VBErlLineNumbering
{
    /// <summary>
    /// The line the faulting statement is really on, counted from <c>1</c> as an editor counts it.
    /// </summary>
    /// <remarks>
    /// The default: it answers the question the caller is actually asking, for any code at all, and it
    /// never names a line the fault was not on.
    /// </remarks>
    DocumentLine = 0,

    /// <summary>
    /// The last line-number label at or before the faulting statement, or <c>0</c> when none precedes it
    /// — bug for bug with MS-VBA.
    /// </summary>
    /// <remarks>
    /// For source that carries a line number on every line, this <em>is</em> the document line, which is
    /// why it is the right answer for a numbered BASIC program and the wrong one for everything else.
    /// Choose it when a workspace's own code depends on MS-VBA's behaviour.
    /// </remarks>
    LineLabel = 1,
}
