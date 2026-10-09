using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>VARS</c>: shows the variables of the program that was stopped.
/// </summary>
/// <remarks>
/// The variables of the procedure the program is in, then those of its module - which for a program typed at the prompt are all of them, since its variables are
/// declared by use at module level. <c>VARS 1</c> is the same for the procedure that called, <c>VARS 2</c> for the one that called it. An array or a user-defined type is
/// followed by its first parts, indented.
/// </remarks>
internal sealed class VarsReplCommand : IReplCommand
{
    /// <summary>How many parts of a variable are shown under it; the debugger has more, and a screen has not.</summary>
    private const int PartsShown = 20;

    public string Name => ReplCommandNames.Vars;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Vars_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (context.Debugger.State is not ReplDebugState.Suspended || !context.Platform.Provides<ProgramDebugging>())
        {
            context.Console.WriteMessage(MessageKind.Warning, Resources.Repl_NotStopped);
            return ReplCommandResult.Continue;
        }

        var frame = 0;
        if (arguments.Trim().Length > 0 && (!int.TryParse(arguments.Trim(), out frame) || frame < 0))
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
            return ReplCommandResult.Continue;
        }

        foreach (var scope in new[] { HostVariableScope.Locals, HostVariableScope.Module })
        {
            var variables = await context.Platform.GetVariablesAsync(frame, scope, 0, token);
            foreach (var variable in variables.Variables)
            {
                await WriteAsync(context, variable, indent: 0, token);
            }
        }

        return ReplCommandResult.Continue;
    }

    private static async Task WriteAsync(ReplCommandContext context, HostVariable variable, int indent, CancellationToken token)
    {
        var padding = new string(' ', indent * 2);
        var value = variable.Value.Length > 0 ? $" = {variable.Value}" : string.Empty;
        context.Console.WriteLine($"{padding}{variable.Name}{value}  ({variable.Type})".TrimEnd());

        if (variable.Reference == 0 || indent > 0)
        {
            return;
        }

        var parts = await context.Platform.GetVariablesAsync(0, HostVariableScope.Locals, variable.Reference, token);
        foreach (var part in parts.Variables.Take(PartsShown))
        {
            await WriteAsync(context, part, indent + 1, token);
        }

        if (parts.Variables.Count > PartsShown)
        {
            context.Console.WriteLine($"{padding}  ...");
        }
    }
}
