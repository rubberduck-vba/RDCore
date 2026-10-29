using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.Parsing;
using RDCore.SDK.Formatting;
using RDCore.SDK.Server.Configuration;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The shell's program, its statements indented by the shell's <c>Formatting</c> settings as they are (<see cref="ProgramIndenter"/>), and indented again when
/// those change.
/// </summary>
internal static class ReplIndentation
{
    /// <summary>
    /// A program that stores its statements indented.
    /// </summary>
    /// <param name="options">The shell's settings, read each time the program is indented.</param>
    /// <param name="liveSettings">The shell's live settings: the program is indented again when they change; never, without them.</param>
    /// <param name="logger">Where a program that cannot be indented is logged.</param>
    public static ReplProgram Program(IOptions<SdkAppOptions> options, ILiveSettings? liveSettings, ILogger? logger)
    {
        var parser = new ModuleParser();
        var program = new ReplProgram(
            statements => ProgramIndenter.Indent(statements, text => parser.Parse(ReplProgram.IndentedModuleUri, text), options.Value.Formatting.ToOptions()),
            exception => logger?.LogWarning(exception, "⚠️ The program could not be indented; its lines are kept as they were typed."));
        if (liveSettings is not null)
        {
            liveSettings.Changed += (_, _) => program.IndentationChanged();
        }

        return program;
    }
}
