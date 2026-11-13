using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.Globalization;
using System.Text.Json;

namespace RDCore.Runtime.Support.Build;

// the .rdproj's build section, which the project file reads as members it does not know, kept as they were read. EntryPoint is Module.Procedure or
// Procedure, null for the implicit Sub Main.
internal sealed record class ProjectBuildSettings(string? EntryPoint, bool IsLibrary)
{
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

// what rdc run runs a loaded workspace with, and a built program its replayed image, so that the two run, print and exit alike.
internal static class ProgramRunner
{
    public const int Completed = 0;

    public const int Failed = 1;

    // no entry point, or code that does not load.
    public const int Refused = 2;

    // the procedure the project names, or else the Sub Main of the one standard module that declares one; null, with the reason, otherwise.
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

    // Command$: an argument that is empty or holds whitespace is quoted, so that each is still one argument.
    public static string CommandLineOf(IReadOnlyList<string> arguments)
        => string.Join(' ', arguments.Select(argument => argument.Length == 0 || argument.AsSpan().IndexOfAny(' ', '\t') >= 0 ? $"\"{argument}\"" : argument));

    public static async Task<int> RunAsync(EnvironmentSessionProvider provider, VBTypeMemberSymbol entryPoint, IReadOnlyList<string> arguments,
        IErrorDetailsRecorder errorDetails, TextWriter output, TextWriter error, bool unattended, CancellationToken token)
    {
        var outside = provider.Outside with { CommandLine = CommandLineOf(arguments) };
        var pipeline = RuntimeExecutionPipeline.Create(provider.Session, provider.Image, errorDetails, token, outside: outside);
        var result = await provider.Execution.RunAsync(pipeline, entryPoint, arguments, output.WriteLine, token, unattended);

        // what it printed last and left open, with no line break after it.
        foreach (var line in result.Output)
        {
            await output.WriteLineAsync(line);
        }

        // an End ends the program, and so does a Stop in a release build (RD-VBAL §5.4.2.11): it ended, and no error ended it.
        if (result.Outcome is ExecutionOutcome.Completed or ExecutionOutcome.Halted)
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
