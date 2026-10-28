using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Commands;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>BREAK</c>: sets a breakpoint at a line of the program, or clears the one it has.
/// </summary>
/// <remarks>
/// <c>BREAK 40</c> toggles the breakpoint of line 40; <c>BREAK 40 I &gt; 5</c> sets one that the program waits at only when the condition is <c>True</c>, replacing
/// the one the line had; <c>BREAK TOGGLE 40</c> disables the breakpoint of line 40 or enables it again, and sets one when the line has none. <c>BREAK</c> alone
/// lists them, and <c>BREAK CLEAR</c> clears them all. A program that is run waits before a line that has one, and <c>CONT</c> or <c>STEP</c> goes on. <c>LIST</c>
/// shows them in its margin.
/// <para>
/// A condition is parsed when it is set, and one that is not an expression is refused there. It is evaluated when the program reaches the line, in the procedure
/// that reaches it - not before, since the names it has are that procedure's, and its locals are not there until it runs.
/// </para>
/// </remarks>
internal sealed class BreakReplCommand : IReplCommand, IReplCommandDescriptor
{
    private const string Toggle = "TOGGLE";
    private const string Clear = "CLEAR";

    public string Name => ReplCommandNames.Break;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Break_Summary;

    // BREAK CLEAR, BREAK TOGGLE <line>, and BREAK <line> [condition] - the condition an expression of the program, completed as one.
    public VerbDescriptor Descriptor => new()
    {
        Name = Name,
        Summary = Summary,
        Forms =
        [
            [new VerbArgument { Name = "clear", Kind = VerbArgumentKind.Keyword, Values = [new() { Value = Clear, Description = Resources.Repl_Break_Clear_Detail }] }],
            [
                new VerbArgument { Name = "toggle", Kind = VerbArgumentKind.Keyword, Values = [new() { Value = Toggle, Description = Resources.Repl_Break_Toggle_Detail }] },
                new VerbArgument { Name = "line", Kind = VerbArgumentKind.LineNumber },
            ],
            [
                new VerbArgument { Name = "line", Kind = VerbArgumentKind.LineNumber },
                // the program waits before the line, and the condition is evaluated there: in its procedure, with its locals.
                new VerbArgument { Name = "condition", Kind = VerbArgumentKind.Expression, EvaluatedAt = "line" },
            ],
        ],
    };

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        var debugger = context.Debugger;
        var text = arguments.Trim();

        if (text.Length == 0)
        {
            if (debugger.Breakpoints.Count == 0)
            {
                context.Console.WriteLine(Resources.Repl_Breakpoint_None);
                return ReplCommandResult.Continue;
            }

            // one breakpoint a line, marked as LIST marks it: its glyph, its line number, and the condition it waits on.
            foreach (var breakpoint in debugger.Breakpoints)
            {
                context.Console.WriteLine(Describe(breakpoint));
            }

            return ReplCommandResult.Continue;
        }

        if (string.Equals(text, Clear, StringComparison.OrdinalIgnoreCase))
        {
            debugger.ClearBreakpoints();
            _ = await TellAsync(context, token);
            context.Console.WriteLine(Resources.Repl_Breakpoint_None);
            return ReplCommandResult.Continue;
        }

        // the line number is the first word, and whatever follows it is the condition, as it was typed: a string literal in it keeps its spaces.
        var (first, rest) = SplitFirstWord(text);
        if (first.Equals(Toggle, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(rest, out var line))
            {
                context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
                return ReplCommandResult.Continue;
            }

            if (!IsALine(context, line))
            {
                return ReplCommandResult.Continue;
            }

            var existed = debugger.TryGetBreakpoint(line, out _);
            var enabled = debugger.ToggleEnabled(line);
            _ = await TellAsync(context, token);
            context.Console.WriteLine(string.Format(!existed ? Resources.Repl_Breakpoint_Set : enabled ? Resources.Repl_Breakpoint_Enabled : Resources.Repl_Breakpoint_Disabled, line));
            return ReplCommandResult.Continue;
        }

        if (!int.TryParse(first, out var number))
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
            return ReplCommandResult.Continue;
        }

        if (!IsALine(context, number))
        {
            return ReplCommandResult.Continue;
        }

        if (rest.Length == 0)
        {
            var set = debugger.ToggleBreakpoint(number);
            _ = await TellAsync(context, token);
            context.Console.WriteLine(string.Format(set ? Resources.Repl_Breakpoint_Set : Resources.Repl_Breakpoint_Cleared, number));
            return ReplCommandResult.Continue;
        }

        // a condition replaces the breakpoint the line had; one the platform refuses leaves the line with the breakpoint it had before.
        var hadOne = debugger.TryGetBreakpoint(number, out var previous);
        debugger.SetBreakpoint(new Breakpoint(number, Enabled: true, rest));
        var refused = await TellAsync(context, token);
        var refusal = refused.FirstOrDefault(entry => entry.Breakpoint.LineNumber == number);
        if (refusal.Reason is { } reason)
        {
            if (hadOne)
            {
                debugger.SetBreakpoint(previous);
                _ = await TellAsync(context, token);
            }

            context.Console.WriteMessage(MessageKind.Error, string.Format(Resources.Repl_Breakpoint_ConditionRefused, number, reason));
            return ReplCommandResult.Continue;
        }

        context.Console.WriteLine(string.Format(Resources.Repl_Breakpoint_Set, number));
        return ReplCommandResult.Continue;
    }

    // a breakpoint is on a line of the program, and a line that is not there is not one: it is as undefined as it is for a GOTO.
    private static bool IsALine(ReplCommandContext context, int number)
    {
        if (context.Program.IndexOf(number) >= 0)
        {
            return true;
        }

        context.Console.WriteMessage(MessageKind.Error, Resources.Repl_UndefinedLine, number.ToString());
        return false;
    }

    private static (string Word, string Remainder) SplitFirstWord(string text)
    {
        var space = text.IndexOf(' ');
        return space < 0 ? (text, string.Empty) : (text[..space], text[(space + 1)..].Trim());
    }

    // how BREAK lists a breakpoint: the glyph LIST marks it with - which says whether the program waits at it, and on a condition - the number of its line, and the
    // condition.
    private static IReadOnlyList<ReplTextRun> Describe(Breakpoint breakpoint)
        =>
        [
            new ReplTextRun(ReplBreakpointMarks.GlyphOf(breakpoint), ReplTextStyle.BreakpointGlyph),
            new ReplTextRun(" ", ReplTextStyle.Plain),
            new ReplTextRun(breakpoint.LineNumber.ToString(), ReplTextStyle.Number),
            .. breakpoint.ConditionExpression is { } condition ? new[] { new ReplTextRun($" {condition}", ReplTextStyle.Plain) } : [],
        ];

    // a program that waits goes on with the lines the platform has, and the next one that is run is told them anyway; telling it now makes a
    // breakpoint set while a program waits one that is waited at when it goes on.
    private static Task<IReadOnlyList<(Breakpoint Breakpoint, string Reason)>> TellAsync(ReplCommandContext context, CancellationToken token)
        => context.Debugger.PushBreakpointsAsync(context, token);
}
