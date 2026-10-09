using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>VARS</c>: shows the variables of the program that was stopped.
/// </summary>
/// <remarks>
/// In the order they are declared in: the variables of the module come first, since a module declares them before any procedure declares its own, and then those of the
/// procedure the program is in - which for a program typed at the prompt are none, since its variables are declared by use at module level. <c>VARS 1</c> is the same for
/// the procedure that called, <c>VARS 2</c> for the one that called it. The names are padded to the longest, so that the values line up, and an array or a
/// user-defined type is followed by its first parts, indented.
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

        // the module declares its variables before a procedure declares its own.
        var variables = new List<HostVariable>();
        foreach (var scope in new[] { HostVariableScope.Module, HostVariableScope.Locals })
        {
            variables.AddRange((await context.Platform.GetVariablesAsync(frame, scope, 0, token)).Variables);
        }

        await WriteAsync(context, variables, indent: 0, token);
        return ReplCommandResult.Continue;
    }

    private static async Task WriteAsync(ReplCommandContext context, IReadOnlyList<HostVariable> variables, int indent, CancellationToken token)
    {
        var margin = new string(' ', indent * 2);
        var width = variables.Count == 0 ? 0 : variables.Max(variable => variable.Name.Length);

        foreach (var variable in variables)
        {
            // the name, padded to the longest so that the values line up; then the value, if it has one; then the type.
            var value = variable.Value.Length > 0 ? $" = {variable.Value}" : string.Empty;
            context.Console.WriteLine($"{margin}{variable.Name.PadRight(width)}{value}  ({variable.Type})");

            if (variable.Reference == 0 || indent > 0)
            {
                continue;
            }

            var parts = await context.Platform.GetVariablesAsync(0, HostVariableScope.Locals, variable.Reference, token);
            await WriteAsync(context, [.. parts.Variables.Take(PartsShown)], indent + 1, token);
            if (parts.Variables.Count > PartsShown)
            {
                context.Console.WriteLine($"{margin}  ...");
            }
        }
    }
}
