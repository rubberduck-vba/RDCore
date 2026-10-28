namespace RDCore.CLI.App.Repl;

/// <summary>
/// The mark of a breakpoint, as <c>LIST</c> draws it in its margin and <c>BREAK</c> lists it: whether the program waits at it, and whether it waits on a condition.
/// </summary>
internal static class ReplBreakpointMarks
{
    private const string Enabled = "●";
    private const string Disabled = "○";
    private const string EnabledWithCondition = "◆";
    private const string DisabledWithCondition = "◈";

    /// <summary>The mark of a breakpoint.</summary>
    /// <param name="breakpoint">The breakpoint.</param>
    public static string GlyphOf(Breakpoint breakpoint)
        => breakpoint.ConditionExpression != null
            ? breakpoint.Enabled ? EnabledWithCondition : DisabledWithCondition
            : breakpoint.Enabled ? Enabled : Disabled;
}
