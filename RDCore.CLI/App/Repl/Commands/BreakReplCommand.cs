using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>BREAK</c>: sets a breakpoint at a line of the program, or clears the one it has.
/// </summary>
/// <remarks>
/// <c>BREAK 40</c> toggles the breakpoint of line 40; <c>BREAK</c> alone lists them, and <c>BREAK CLEAR</c> clears them all. A program that is run
/// waits before a line that has one, and <c>CONT</c> or <c>STEP</c> goes on. <c>LIST</c> shows them in its margin.
/// </remarks>
internal sealed class BreakReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Break;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Break_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        var debugger = context.Debugger;
        var text = arguments.Trim();

        if (text.Length == 0)
        {
            context.Console.WriteLine(debugger.Breakpoints.Count == 0
                ? Resources.Repl_Breakpoint_None
                : string.Format(Resources.Repl_Breakpoint_List, string.Join(", ", debugger.Breakpoints)));
            return ReplCommandResult.Continue;
        }

        if (string.Equals(text, "CLEAR", StringComparison.OrdinalIgnoreCase))
        {
            debugger.ClearBreakpoints();
            await TellAsync(context, token);
            context.Console.WriteLine(Resources.Repl_Breakpoint_None);
            return ReplCommandResult.Continue;
        }

        if (!int.TryParse(text, out var number))
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
            return ReplCommandResult.Continue;
        }

        // a breakpoint is on a line of the program, and a line that is not there is not one: it is as undefined as it is for a GOTO.
        if (context.Program.IndexOf(number) < 0)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_UndefinedLine, number.ToString());
            return ReplCommandResult.Continue;
        }

        var set = debugger.Toggle(number);
        await TellAsync(context, token);
        context.Console.WriteLine(string.Format(set ? Resources.Repl_Breakpoint_Set : Resources.Repl_Breakpoint_Cleared, number));
        return ReplCommandResult.Continue;
    }

    // a program that waits goes on with the lines the platform has, and the next one that is run is told them anyway; telling it now makes a
    // breakpoint set while a program waits one that is waited at when it goes on.
    private static Task TellAsync(ReplCommandContext context, CancellationToken token)
        => context.Debugger.PushBreakpointsAsync(context, token);
}
