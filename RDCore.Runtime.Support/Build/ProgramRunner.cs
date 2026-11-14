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

namespace RDCore.Runtime.Support.Build;

// what rdc run runs a loaded workspace with, and a built program its replayed image, so that the two run, print and exit alike.
internal static class ProgramRunner
{
    public const int Completed = 0;

    public const int Failed = 1;

    // no entry point, or code that does not load.
    public const int Refused = 2;

    // the entry point of the project's modules, as the session defined them (ProgramEntryPoint.Of); null, with what rdc says of it, when it has none.
    public static (string Module, VBTypeMemberSymbol Procedure)? EntryPointOf(IRuntimeSession session, IEnumerable<string> modules, ProjectBuild? build,
        out string? refusal)
    {
        var resolved = ProgramEntryPoint.Of(build, modules
            .Select(name => session.Symbols.TryGetModule(name, out var module) ? module : null)
            .OfType<VBModuleSymbol>()
            .Select(module => (module, session.Symbols.MembersOf(module.Uri).OfType<VBTypeMemberSymbol>())));

        refusal = resolved.Refusal switch
        {
            null => null,
            ProgramEntryPointRefusal.Ambiguous => string.Format(CultureInfo.CurrentCulture, Resources.Build_AmbiguousEntryPoint,
                string.Join(", ", resolved.Candidates.Select(module => module.Name))),
            _ => Resources.Build_NoEntryPoint,
        };
        return resolved.IsFound ? (resolved.Module.Name, resolved.Procedure) : null;
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
