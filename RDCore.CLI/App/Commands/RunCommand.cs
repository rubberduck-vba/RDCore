using CommandLine;
using RDCore.CLI.App.Bench;
using RDCore.CLI.App.Build;
using RDCore.CLI.App.Repl;
using RDCore.External.Interaction;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Build;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Commands;

internal sealed class RunCommandOptions
{
    [Value(0, MetaName = "path", Required = false, HelpText = "A workspace folder (with or without a .rdproj), or a module file. The current folder when omitted.")]
    public string? Path { get; set; }

    [Option('c', "configuration", Required = false, Default = VBBuildConfiguration.Debug, HelpText = "Debug or Release.")]
    public VBBuildConfiguration Configuration { get; set; }

    [Option("unattended", Required = false, HelpText = "Nobody answers a dialog: a MsgBox or an InputBox fails the run.")]
    public bool Unattended { get; set; }
}

// rdc run [path] [-- args…]: the workspace loaded as rdc bench loads it, and run as the program rdc build builds of it runs (ProgramRunner). Nothing is written.
internal sealed class RunCommand(IFileSystem fileSystem, IProjectFileLoader projects, BenchRunner runner, IErrorDetailsRecorder errorDetails,
    TextWriter? output = null, TextWriter? error = null, IConsoleKeys? keys = null) : ICliCommand
{
    private TextWriter Output => output ?? global::System.Console.Out;
    private TextWriter Error => error ?? global::System.Console.Error;

    // the dialogs of the program: Windows shows its own, and elsewhere they are asked at the console, as the shell asks them.
    private IUserInteraction? Dialogs => OperatingSystem.IsWindows() ? null
        : output is null ? ConsoleUserInteraction.OfTheConsole(keys ?? new SystemConsoleKeys(), Spectre.Console.AnsiConsole.Profile.Capabilities.Ansi)
        : new ConsoleUserInteraction(keys ?? new SystemConsoleKeys(), output);

    public string Name => CommandNames.RunCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.RunCommand.Alias];
    public string Summary => Resources.Run_Summary;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        // what follows -- is the program's, and not rdc's.
        var separator = args.ToList().IndexOf("--");
        var own = separator < 0 ? args : args.Take(separator).ToList();
        var parsed = Parser.Default.ParseArguments<RunCommandOptions>(own);
        if (parsed.Errors.Any())
        {
            // CommandLine already wrote the usage/errors to stderr.
            return ProgramRunner.Refused;
        }

        var options = parsed.Value;
        BenchWorkspace workspace;
        try
        {
            workspace = await BenchWorkspace.LoadAsync(fileSystem, projects, options.Path ?? fileSystem.Directory.GetCurrentDirectory(), token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            await Error.WriteLineAsync(exception.Message);
            return ProgramRunner.Refused;
        }

        var profile = RuntimeEnvironmentProfile.Of(runner.Environment) with { Configuration = options.Configuration };
        var (provider, modules, problems) = await runner.LoadProgramAsync(workspace, profile, Dialogs, token);
        using var session = provider;

        // nothing of the project runs while a module of it does not load (RD-VBAL 2.6.1).
        if (problems.Count > 0)
        {
            foreach (var problem in problems)
            {
                await Error.WriteLineAsync(problem);
            }

            return ProgramRunner.Refused;
        }

        if (ProgramRunner.EntryPointOf(provider.Session, modules, ProjectBuildSettings.Of(workspace.Project), out var refusal) is not { } entryPoint)
        {
            await Error.WriteLineAsync(refusal);
            return ProgramRunner.Refused;
        }

        IReadOnlyList<string> arguments = separator < 0 ? [] : [.. args.Skip(separator + 1)];
        return await ProgramRunner.RunAsync(provider, entryPoint.Procedure, arguments, errorDetails, Output, Error, options.Unattended, token);
    }
}
