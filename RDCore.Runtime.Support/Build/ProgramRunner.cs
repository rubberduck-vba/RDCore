using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.Globalization;

namespace RDCore.Runtime.Support.Build;

// what rdc run runs a loaded workspace with, and a built program its replayed image, so that the two run, print and exit alike.
internal static class ProgramRunner
{
    public const int Completed = 0;

    public const int Failed = 1;

    // no entry point, or code that does not load.
    public const int Refused = 2;

    // the builder's codes (RDCoreDiagnosticId, in RDCore.Diagnostics, which a program does not ship): each refusal ends with its code, which names the page
    // that documents it.
    public static readonly RDCoreDiagnosticCode NoEntryPoint = new(RDCoreDiagnosticCode.CorePrefix, 30001);

    public static readonly RDCoreDiagnosticCode AmbiguousEntryPoint = new(RDCoreDiagnosticCode.CorePrefix, 30002);

    // the entry point of the project's modules, as the session defined them (ProgramEntryPoint.Of); null, with what rdc says of it, when it has none. A run
    // of one module's file (startModule) starts where that module would start as a project of its own: its script, else its Sub Main. The file names the
    // module, so no other module's procedure answers to it, and a library is still not run.
    public static (string Module, VBTypeMemberSymbol Procedure)? EntryPointOf(IRuntimeSession session, IEnumerable<string> modules, ProjectBuild? build,
        out string? refusal, string? startModule = null)
    {
        if (startModule is not null)
        {
            (modules, build) = ([startModule], (build ?? new ProjectBuild()) with { EntryPoint = null });
        }

        var resolved = ProgramEntryPoint.Of(build, modules
            .Select(name => session.Symbols.TryGetModule(name, out var module) ? module : null)
            .OfType<VBModuleSymbol>()
            .Select(module => (module, session.Symbols.MembersOf(module.Uri).OfType<VBTypeMemberSymbol>())));

        refusal = resolved.Refusal switch
        {
            null => null,
            ProgramEntryPointRefusal.Ambiguous => Refusal(AmbiguousEntryPoint, string.Format(CultureInfo.CurrentCulture, Resources.Build_AmbiguousEntryPoint,
                string.Join(", ", resolved.Candidates.Select(module => module.Name)))),
            ProgramEntryPointRefusal.Library => Refusal(NoEntryPoint, Resources.Build_LibraryNotRun),
            _ when build?.EntryPoint is { Length: > 0 } named => Refusal(NoEntryPoint,
                string.Format(CultureInfo.CurrentCulture, Resources.Build_NamedEntryPointNotFound, named)),
            _ => Refusal(NoEntryPoint, Resources.Build_NoEntryPoint),
        };
        return resolved.IsFound ? (resolved.Module.Name, resolved.Procedure) : null;
    }

    private static string Refusal(RDCoreDiagnosticCode code, string message) => string.Format(CultureInfo.CurrentCulture, Resources.Build_Refusal, message, code);

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

        // an End ends the program, and so does a Stop in a release build (RD-VBAL §5.4.2.11): it ended, and no error ended it. A program that said the code
        // its process ends with as it ended (WScript.Quit) ends with that one.
        if (result.Outcome is ExecutionOutcome.Completed or ExecutionOutcome.Halted)
        {
            return result.Outcome is ExecutionOutcome.Halted ? result.ExitCode : Completed;
        }

        // the error's VBA number is in the message and never in the exit code, which is 1 whatever the error: a process's exit code is a byte on Linux,
        // where 1004 or vbObjectError + n would wrap.
        await error.WriteLineAsync(result.Outcome is ExecutionOutcome.RuntimeError
            ? string.Format(CultureInfo.CurrentCulture, Resources.Run_RuntimeError, result.ErrorTitle, result.ErrorNumber, result.ErrorMessage, result.ErrorCode)
            : $"{result.Outcome} {result.ErrorMessage}".Trim());

        // what the error says beyond its description, under it: which form was still loaded, for a run nobody attends.
        if (result.ErrorVerbose is { } verbose)
        {
            await error.WriteLineAsync($"  {verbose}");
        }

        foreach (var frame in result.StackTrace)
        {
            await error.WriteLineAsync(frame.Line < 0 ? $"    {frame.Procedure}" : $"    {frame.Procedure}:{frame.Line + 1}");
        }

        return Failed;
    }
}
