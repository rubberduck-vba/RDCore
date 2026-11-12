using RDCore.SDK.Client;
using RDCore.CLI.ConsoleIO.Model;
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
/// <para>
/// A shell attached to a workspace has no numbered program, and a breakpoint is on a line of one of the workspace's modules, by its name and the line's number in
/// it: <c>BREAK Other 9</c>, <c>BREAK Other 9 I &gt; 5</c>, <c>BREAK TOGGLE Other 9</c>. <c>BREAK</c> alone lists them as <c>Other 9</c>, and <c>BREAK CLEAR</c>
/// clears them all. A module the workspace does not have is refused, and so is a line no statement of the module begins on, once the platform holds the module's code.
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
        if (context.Program.AttachedToWorkspace)
        {
            await ExecuteInWorkspaceAsync(context, arguments, token);
            return ReplCommandResult.Continue;
        }

        var debugger = context.Debugger;
        var text = arguments.Trim();

        if (text.Length == 0)
        {
            if (debugger.Breakpoints.Count == 0)
            {
                context.Console.WriteLine(Resources.Repl_Breakpoint_None);
                return ReplCommandResult.Continue;
            }

            // one breakpoint a line, marked as LIST marks it: its glyph, its line number, and the condition it waits on - the conditions of all of them
            // coloured by one request, not one each.
            var conditions = debugger.Breakpoints.Select(breakpoint => breakpoint.ConditionExpression).OfType<string>().Distinct().ToList();
            var highlighted = await HighlightedAsync(context, conditions, token);
            foreach (var breakpoint in debugger.Breakpoints)
            {
                var condition = breakpoint.ConditionExpression is { } written ? highlighted[written] : null;
                context.Console.WriteLine(Describe(breakpoint, condition));
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
        var refusal = refused.FirstOrDefault(entry => entry.Module is null && entry.Breakpoint.LineNumber == number);
        if (refusal.ConditionError is { } reason)
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

    // in a workspace, a breakpoint is on a line of one of its modules: BREAK Other 9 [condition], BREAK TOGGLE Other 9, BREAK alone, BREAK CLEAR.
    private static async Task ExecuteInWorkspaceAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        var breakpoints = context.Debugger.ModuleBreakpoints;
        var text = arguments.Trim();
        if (text.Length == 0)
        {
            if (breakpoints.Count == 0)
            {
                context.Console.WriteLine(Resources.Repl_Breakpoint_None);
                return;
            }

            foreach (var (listed, breakpoint) in breakpoints.All)
            {
                context.Console.WriteLine(Describe(breakpoint, listed, breakpoint.ConditionExpression is { } written ? [new ReplTextRun(written, ReplTextStyle.Plain)] : null));
            }

            return;
        }

        if (string.Equals(text, Clear, StringComparison.OrdinalIgnoreCase))
        {
            context.Debugger.ClearBreakpoints();
            _ = await TellModulesAsync(context, module: null, line: 0, token);
            context.Console.WriteLine(Resources.Repl_Breakpoint_None);
            return;
        }

        // the module is the first word, after TOGGLE when there is one, the line the second, and whatever follows them is the condition, as it was typed.
        var (module, rest) = SplitFirstWord(text);
        var toggle = module.Equals(Toggle, StringComparison.OrdinalIgnoreCase);
        if (toggle)
        {
            (module, rest) = SplitFirstWord(rest);
        }

        var (lineText, condition) = SplitFirstWord(rest);
        if (module.Length == 0 || int.TryParse(module, out _) || !int.TryParse(lineText, out var line) || line < 1 || (toggle && condition.Length > 0))
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
            return;
        }

        if (await context.Platform.FindModuleAsync(module, token) is null)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_UndefinedModule, module);
            return;
        }

        var where = ReplModuleBreakpoints.Where(module, line);
        if (toggle)
        {
            var existed = breakpoints.TryGet(module, line, out _);
            var enabled = breakpoints.ToggleEnabled(module, line);
            if (await TellModulesAsync(context, module, line, token) is null)
            {
                context.Console.WriteLine(string.Format(!existed ? Resources.Repl_Breakpoint_Set : enabled ? Resources.Repl_Breakpoint_Enabled : Resources.Repl_Breakpoint_Disabled, where));
            }

            return;
        }

        if (condition.Length == 0)
        {
            var set = breakpoints.Toggle(module, line);
            if (await TellModulesAsync(context, module, line, token) is null)
            {
                context.Console.WriteLine(string.Format(set ? Resources.Repl_Breakpoint_Set : Resources.Repl_Breakpoint_Cleared, where));
            }

            return;
        }

        // a condition replaces the breakpoint the line had; one the platform refuses leaves the line with the breakpoint it had before.
        var hadOne = breakpoints.TryGet(module, line, out var previous);
        breakpoints.Set(module, new Breakpoint(line, Enabled: true, condition));
        if (await TellModulesAsync(context, module, line, token) is { ConditionError: not null })
        {
            if (hadOne)
            {
                breakpoints.Set(module, previous);
                _ = await TellModulesAsync(context, module: null, line: 0, token);
            }

            return;
        }

        context.Console.WriteLine(string.Format(Resources.Repl_Breakpoint_Set, where));
    }

    // the platform is told every breakpoint, and each it refused is said: the one of the line just set, when it is the one refused, and any other the platform
    // refuses now. Which of them is the line's is answered.
    private static async Task<ReplBreakpointRefusal?> TellModulesAsync(ReplCommandContext context, string? module, int line, CancellationToken token)
    {
        ReplBreakpointRefusal? refusedHere = null;
        foreach (var refusal in await TellAsync(context, token))
        {
            if (module is not null && string.Equals(refusal.Module, module, StringComparison.OrdinalIgnoreCase) && refusal.Breakpoint.LineNumber == line)
            {
                refusedHere = refusal;
            }

            context.Console.WriteMessage(MessageKind.Error, refusal.Message);
        }

        return refusedHere;
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
    private static IReadOnlyList<ReplTextRun> Describe(Breakpoint breakpoint, IReadOnlyList<ReplTextRun>? condition) => Describe(breakpoint, module: null, condition);

    // a breakpoint of a module of a workspace is its module and its line.
    private static IReadOnlyList<ReplTextRun> Describe(Breakpoint breakpoint, string? module, IReadOnlyList<ReplTextRun>? condition)
        =>
        [
            new ReplTextRun(ReplBreakpointMarks.GlyphOf(breakpoint), ReplTextStyle.BreakpointGlyph),
            new ReplTextRun(" ", ReplTextStyle.Plain),
            .. module is null ? [] : new[] { new ReplTextRun(module, ReplTextStyle.Identifier), new ReplTextRun(" ", ReplTextStyle.Plain) },
            new ReplTextRun(breakpoint.LineNumber.ToString(), ReplTextStyle.Number),
            .. condition is null ? [] : new[] { new ReplTextRun(" ", ReplTextStyle.Plain) }.Concat(condition),
        ];

    // the conditions as runs of the way the language server tells their tokens, all in one request; plain where it cannot: a server without the capability,
    // or one that does not answer, leaves a listing as legible as it was.
    private static async Task<Dictionary<string, IReadOnlyList<ReplTextRun>>> HighlightedAsync(ReplCommandContext context, IReadOnlyList<string> conditions, CancellationToken token)
    {
        var plain = conditions.ToDictionary(condition => condition, condition => (IReadOnlyList<ReplTextRun>)[new ReplTextRun(condition, ReplTextStyle.Plain)]);
        if (conditions.Count == 0 || !context.Platform.Provides<SessionSemanticTokens>())
        {
            return plain;
        }

        try
        {
            var tokens = await context.Platform.GetFragmentTokensAsync(context.Program.ToModuleSource(), ReplProgram.ModuleName, conditions, token);
            if (tokens.Count != conditions.Count)
            {
                return plain;
            }

            return conditions.Select((condition, index) => (condition, runs: ReplHighlighter.Runs(condition, tokens[index])))
                .ToDictionary(entry => entry.condition, entry => entry.runs);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return plain;
        }
    }

    // a program that waits goes on with the lines the platform has, and the next one that is run is told them anyway; telling it now makes a
    // breakpoint set while a program waits one that is waited at when it goes on.
    private static Task<IReadOnlyList<ReplBreakpointRefusal>> TellAsync(ReplCommandContext context, CancellationToken token)
        => context.Debugger.PushBreakpointsAsync(context, token);
}
