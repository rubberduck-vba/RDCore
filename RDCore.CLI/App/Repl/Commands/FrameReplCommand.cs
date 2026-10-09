using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>FRAME</c>: selects the activation of the program that was stopped that <c>VARS</c> and <c>EVAL</c> are about.
/// </summary>
/// <remarks>
/// <c>FRAME 1</c> selects the procedure that called the one the program is in, as <c>STACK</c> numbers them; <c>FRAME</c> alone says which is selected. The innermost is
/// selected every time the program stops.
/// </remarks>
internal sealed class FrameReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Frame;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Frame_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (context.Debugger.State is not ReplDebugState.Suspended || !context.Platform.Provides<ProgramDebugging>())
        {
            context.Console.WriteMessage(MessageKind.Warning, Resources.Repl_NotStopped);
            return ReplCommandResult.Continue;
        }

        var stack = await context.Platform.GetStackAsync(token);
        var selected = context.Debugger.SelectedFrame;
        if (arguments.Trim().Length > 0)
        {
            if (!int.TryParse(arguments.Trim(), out selected) || selected < 0 || selected >= stack.Frames.Count)
            {
                context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
                return ReplCommandResult.Continue;
            }

            context.Debugger.SelectedFrame = selected;
        }

        if (selected < stack.Frames.Count)
        {
            var frame = stack.Frames[selected];
            context.Console.WriteLine($"#{frame.Id} {frame.Procedure}");
        }

        return ReplCommandResult.Continue;
    }
}
