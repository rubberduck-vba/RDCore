using CommandLine;
using RDCore.CLI.App.Bench;
using RDCore.CLI.App.Build;
using RDCore.CLI.ConsoleIO;
using RDCore.CLI.ConsoleIO.Model;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Support.Build;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.Globalization;
using System.IO.Abstractions;
using System.Reflection;
using System.Text;

namespace RDCore.CLI.App.Commands;

internal sealed class BuildCommandOptions
{
    [Value(0, MetaName = "path", Required = false, HelpText = "A workspace folder (with or without a .rdproj), or a module file. The current folder when omitted.")]
    public string? Path { get; set; }

    [Option('o', "output", Required = false, HelpText = "The folder the program is written into. bin/<configuration>/<runtime>/ in the workspace when omitted.")]
    public string? Output { get; set; }

    [Option('n', "name", Required = false, HelpText = "The program's name. The project's when omitted.")]
    public string? Name { get; set; }

    [Option('c', "configuration", Required = false, Default = VBBuildConfiguration.Debug, HelpText = "Debug or Release.")]
    public VBBuildConfiguration Configuration { get; set; }

    [Option('r', "runtime", Required = false, HelpText = "The runtime identifier of the platform the program is built for (win-x64, linux-x64). rdc's when omitted.")]
    public string? Runtime { get; set; }
}

// rdc build [path]: the workspace loaded as rdc bench loads it, each procedure compiled as its first call would compile it, and the load itself kept in the
// assembly, which the program replays when it runs: a built program runs with no parser.
internal sealed class BuildCommand(IConsoleMessageWriter writer, IFileSystem fileSystem, IProjectFileLoader projects, BenchRunner runner,
    ProgramOutput? output = null, Func<string, string?>? hosts = null) : ICliCommand
{
    private ProgramOutput Output => output ?? new ProgramOutput(fileSystem, AppContext.BaseDirectory);

    public string Name => CommandNames.BuildCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.BuildCommand.Alias];
    public string Summary => Resources.Build_Summary;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var parsed = Parser.Default.ParseArguments<BuildCommandOptions>(args);
        if (parsed.Errors.Any())
        {
            // CommandLine already wrote the usage/errors to stderr.
            return 2;
        }

        var options = parsed.Value;
        BenchWorkspace workspace;
        try
        {
            workspace = await BenchWorkspace.LoadAsync(fileSystem, projects, options.Path ?? fileSystem.Directory.GetCurrentDirectory(), token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            return Error(exception.Message);
        }

        var name = options.Name ?? workspace.Project.Name;
        if (string.IsNullOrWhiteSpace(name) || Output.IsTaken(name))
        {
            return Error(string.Format(CultureInfo.CurrentCulture, Resources.Build_NameTaken, name));
        }

        var runtime = options.Runtime ?? AppHost.CurrentRuntime;
        var profile = RuntimeEnvironmentProfile.Of(runner.Environment) with { Configuration = options.Configuration };
        var builder = new ProgramAssemblyBuilder(new AssemblyName(name));
        foreach (var module in workspace.Modules)
        {
            // the address each module is parsed with: what its instructions say they are written in.
            builder.AddSource(workspace.DocumentOf(module), module.Source);
        }

        var built = await runner.BuildAsync(workspace, builder, profile, token);
        using var session = built.Provider;

        // nothing of the project runs while a module of it does not load (RD-VBAL 2.6.1): neither does a program built of it.
        if (built.Problems.Count > 0)
        {
            foreach (var problem in built.Problems)
            {
                _ = Error(problem);
            }

            return 1;
        }

        if (ProgramRunner.EntryPointOf(built.Provider.Session, built.Modules, ProjectBuildSettings.Of(workspace.Project), out var refusal) is not { } entryPoint)
        {
            return Error(refusal!);
        }

        builder.DefineEntryPoint(typeof(BuiltProgram).GetMethod(nameof(BuiltProgram.Run))!);
        var image = new BuiltProgramImage(workspace.Project, workspace.RootUri, BuiltEnvironment.Of(built.Environment), entryPoint.Module,
            entryPoint.Procedure.Name, built.Definitions, [.. workspace.Modules.Select(module => new BuiltModule(module.RelativePath, module.Name, module.Type))]);
        builder.AddResource(BuiltProgramImage.ResourceName, Encoding.UTF8.GetBytes(PlatformJson.Serialize(image)));

        using var assembly = new MemoryStream();
        using var pdb = new MemoryStream();
        builder.Save(assembly, pdb, $"{name}.pdb");
        var folder = options.Output ?? fileSystem.Path.Combine(workspace.Root, "bin", options.Configuration.ToString(), runtime);
        var (executable, lacking) = Output.Write(folder, name, assembly.ToArray(), pdb.ToArray(), (hosts ?? (rid => AppHost.Find(rid)))(runtime), runtime,
            built.Libraries);

        if (executable is null)
        {
            writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Warning)
                .WithMessageBody(string.Format(CultureInfo.CurrentCulture, Resources.Build_NoAppHost, runtime, $"{name}.dll")));
        }

        if (lacking.Count > 0)
        {
            writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Warning)
                .WithMessageBody(string.Format(CultureInfo.CurrentCulture, Resources.Build_NoticesLacking, string.Join(", ", lacking))));
        }

        writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Success)
            .WithMessageBody(string.Format(CultureInfo.CurrentCulture, Resources.Build_Built, name, folder)));
        writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Information)
            .WithMessageBody(string.Format(CultureInfo.CurrentCulture, Resources.Compilation_Count,
                built.Procedures.Count(procedure => procedure.Compilation.IsCompiled), built.Procedures.Count)));
        return 0;
    }

    private int Error(string message)
    {
        writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Error).WithMessageBody(message));
        return 1;
    }
}
