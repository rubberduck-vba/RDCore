using RDCore.CLI.Host;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;
using System.Globalization;
using System.Text.Json;

namespace RDCore.CLI.App.Build;

/// <summary>
/// What a project's <c>.rdproj</c> says of the program it is, in its <c>build</c> section, which the project file reads as
/// members it does not know, kept as they were read.
/// </summary>
/// <param name="EntryPoint">The entry point it names, <c>Module.Procedure</c> or <c>Procedure</c>; <see langword="null"/> for the implicit <c>Sub Main</c>.</param>
/// <param name="IsLibrary">Whether the project is a library, which has no entry point.</param>
internal sealed record class ProjectBuildSettings(string? EntryPoint, bool IsLibrary)
{
    /// <summary>
    /// The settings of <paramref name="project"/>; none of them when it has no <c>build</c> section.
    /// </summary>
    public static ProjectBuildSettings Of(RDCoreProject project)
    {
        if (project.ExtensionData?.TryGetValue("build", out var build) is not true || build.ValueKind is not JsonValueKind.Object)
        {
            return new ProjectBuildSettings(null, false);
        }

        var entryPoint = build.TryGetProperty("entryPoint", out var named) && named.ValueKind is JsonValueKind.String ? named.GetString() : null;
        var library = build.TryGetProperty("outputType", out var type) && type.ValueKind is JsonValueKind.String
            && string.Equals(type.GetString(), "Library", StringComparison.OrdinalIgnoreCase);
        return new ProjectBuildSettings(string.IsNullOrWhiteSpace(entryPoint) ? null : entryPoint, library);
    }
}

/// <summary>
/// Runs a program's entry point in a session that is loaded: what <c>rdc run</c> runs a workspace with, and a built program its image, so that the two run
/// the same way and end the same way.
/// </summary>
internal static class ProgramRunner
{
    /// <summary>
    /// The exit code of a program that ran to its end, or to an <c>End</c>.
    /// </summary>
    public const int Completed = 0;

    /// <summary>
    /// The exit code of a program a run-time error stopped, whatever the error: its number is in the message on standard error.
    /// </summary>
    public const int Failed = 1;

    /// <summary>
    /// The exit code of a program that could not be started: no entry point, or code that does not load.
    /// </summary>
    public const int Refused = 2;

    /// <summary>
    /// The entry point of the session's program: the procedure the project names (<see cref="ProjectBuildSettings.EntryPoint"/>), or else the
    /// <c>Sub Main</c> of the one standard module that declares one (<see cref="ProgramEntryPoint"/>).
    /// </summary>
    /// <param name="session">The session the program is loaded in.</param>
    /// <param name="modules">The names of the modules that loaded.</param>
    /// <param name="settings">What the project says of the program.</param>
    /// <param name="refusal">Why there is none, worded for the user.</param>
    /// <returns>The module that declares it and the procedure; <see langword="null"/> when there is none, or more than one.</returns>
    public static (string Module, VBTypeMemberSymbol Procedure)? EntryPointOf(IRuntimeSession session, IEnumerable<string> modules, ProjectBuildSettings settings,
        out string? refusal)
    {
        refusal = null;
        if (settings.IsLibrary)
        {
            refusal = Resources.Build_NoEntryPoint;
            return null;
        }

        var (named, inModule) = settings.EntryPoint?.Split('.', 2) switch
        {
            [var module, var procedure] => (procedure, module),
            [var procedure] => (procedure, (string?)null),
            _ => (ProgramEntryPoint.Name, null),
        };

        var found = new List<(string Module, VBTypeMemberSymbol Procedure)>();
        foreach (var name in modules)
        {
            if ((inModule is not null && !string.Equals(name, inModule, StringComparison.OrdinalIgnoreCase))
                || !session.Symbols.TryGetModule(name, out var module) || module is not VBStandardModuleSymbol)
            {
                continue;
            }

            if (session.Symbols.MembersOf(module.Uri).OfType<VBTypeMemberSymbol>().FirstOrDefault(member => ProgramEntryPoint.IsEntryPoint(member, named)) is { } procedure)
            {
                found.Add((name, procedure));
            }
        }

        switch (found)
        {
            case [var one]:
                return one;
            case []:
                refusal = Resources.Build_NoEntryPoint;
                return null;
            default:
                refusal = string.Format(CultureInfo.CurrentCulture, Resources.Build_AmbiguousEntryPoint, string.Join(", ", found.Select(entry => entry.Module)));
                return null;
        }
    }

    /// <summary>
    /// The command line a program started with <paramref name="arguments"/> sees (<c>Command$</c>): the arguments, separated by a space, and in double quotes
    /// when they are empty or hold a space or a tab, so that each is still one argument.
    /// </summary>
    /// <param name="arguments">What the program was started with.</param>
    public static string CommandLineOf(IReadOnlyList<string> arguments)
        => string.Join(' ', arguments.Select(argument => argument.Length == 0 || argument.AsSpan().IndexOfAny(' ', '\t') >= 0 ? $"\"{argument}\"" : argument));

    /// <summary>
    /// Runs <paramref name="entryPoint"/> in the session of <paramref name="provider"/>, and says how the program ended.
    /// </summary>
    /// <param name="provider">The session the program is loaded in.</param>
    /// <param name="entryPoint">Its entry point.</param>
    /// <param name="arguments">
    /// What the program was started with: the entry point's <c>ParamArray</c>, when it declares one, and <c>Command$</c> (<see cref="CommandLineOf"/>).
    /// </param>
    /// <param name="messages">Builds the verbose half of a run-time error's message.</param>
    /// <param name="output">Where what the program prints is written, as it prints it: standard output.</param>
    /// <param name="error">Where the error that stopped it is written: standard error.</param>
    /// <param name="unattended">Whether nobody answers a dialog, which then fails the run.</param>
    /// <param name="token">Stops the program.</param>
    /// <returns>The exit code: <see cref="Completed"/>, or <see cref="Failed"/>.</returns>
    public static async Task<int> RunAsync(EnvironmentSessionProvider provider, VBTypeMemberSymbol entryPoint, IReadOnlyList<string> arguments,
        IVerboseMessageBuilder messages, TextWriter output, TextWriter error, bool unattended, CancellationToken token)
    {
        var outside = provider.Outside with { CommandLine = CommandLineOf(arguments) };
        var pipeline = RuntimeExecutionPipeline.Create(provider.Session, provider.Image, messages, token, outside: outside);
        var result = await provider.Execution.RunAsync(pipeline, entryPoint, arguments, output.WriteLine, token, unattended);

        // what it printed last and left open, with no line break after it.
        foreach (var line in result.Output)
        {
            await output.WriteLineAsync(line);
        }

        if (result.Outcome is ExecutionOutcome.Completed)
        {
            return Completed;
        }

        // the error's VBA number is in the message and never in the exit code, which is 1 whatever the error: a process's exit code is a byte on Linux,
        // where 1004 or vbObjectError + n would wrap.
        await error.WriteLineAsync(result.Outcome is ExecutionOutcome.RuntimeError
            ? string.Format(CultureInfo.CurrentCulture, Resources.Run_RuntimeError, result.ErrorTitle, result.ErrorNumber, result.ErrorMessage, result.ErrorCode)
            : $"{result.Outcome} {result.ErrorMessage}".Trim());
        foreach (var frame in result.StackTrace)
        {
            await error.WriteLineAsync(frame.Line < 0 ? $"    {frame.Procedure}" : $"    {frame.Procedure}:{frame.Line + 1}");
        }

        return Failed;
    }
}
