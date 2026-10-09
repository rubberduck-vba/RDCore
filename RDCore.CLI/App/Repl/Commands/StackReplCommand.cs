using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>STACK</c>: shows the procedures the program that was stopped is in, innermost first, and the line each is at.
/// </summary>
/// <remarks>
/// The line is the program's own line number where the procedure is the program, and the line of its module where it is a procedure the program called.
/// </remarks>
internal sealed class StackReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Stack;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Stack_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (context.Debugger.State is not ReplDebugState.Suspended || !context.Platform.Provides<ProgramDebugging>())
        {
            context.Console.WriteMessage(MessageKind.Warning, Resources.Repl_NotStopped);
            return ReplCommandResult.Continue;
        }

        var stack = await context.Platform.GetStackAsync(token);
        foreach (var frame in stack.Frames)
        {
            context.Console.WriteLine($"#{frame.Id} {frame.Procedure} {Where(context.Program, frame.Module, frame.Line)}".TrimEnd());
        }

        return ReplCommandResult.Continue;
    }

    // a line of the shell's program is a line it was typed in; a line of anything else is a line of a file.
    private static string Where(ReplProgram program, string module, int line)
        => line < 0
            ? string.Empty
            : module == ReplProgram.ModuleName && program.LineNumberAt(line) is { } number ? number.ToString() : $"({module} line {line + 1})";
}
