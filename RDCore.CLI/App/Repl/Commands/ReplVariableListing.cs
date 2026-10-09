using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// Writes variables as the shell lists them: the names padded to the longest so that the values line up, the type after the value, and the first parts of an array or a
/// user-defined type under it, indented.
/// </summary>
internal static class ReplVariableListing
{
    /// <summary>How many parts of a variable are shown under it; the debugger has more, and a screen has not.</summary>
    private const int PartsShown = 20;

    /// <summary>
    /// Writes <paramref name="variables"/>.
    /// </summary>
    /// <param name="context">The live session.</param>
    /// <param name="variables">The variables, in the order they are written in.</param>
    /// <param name="indent">How many levels they are indented.</param>
    /// <param name="token">A token that cancels the requests for the parts.</param>
    public static async Task WriteAsync(ReplCommandContext context, IReadOnlyList<HostVariable> variables, int indent, CancellationToken token)
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
