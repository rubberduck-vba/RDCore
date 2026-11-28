using CommandLine;
using Microsoft.Extensions.Logging;
using RDCore.CLI.App.Bench;
using RDCore.CLI.App.Build;
using RDCore.CLI.ConsoleIO;
using RDCore.CLI.ConsoleIO.Model;
using RDCore.Runtime.Composer;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using System.Globalization;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Commands;

internal sealed class ComposeCommandOptions
{
    [Value(0, MetaName = "path", Required = false,
        HelpText = "A workspace folder (with or without a .rdproj), or a module file. The current folder when omitted.")]
    public string? Path { get; set; }

    [Option('o', "output", Required = false, HelpText = "The composition file. bin/<configuration>/<project>.rdcomposition in the workspace when omitted.")]
    public string? Output { get; set; }

    [Option('c', "configuration", Required = false, Default = VBBuildConfiguration.Debug, HelpText = "Debug or Release.")]
    public VBBuildConfiguration Configuration { get; set; }
}

// rdc compose [path]: the workspace composed as a host composes it from its files, written for a host that has no parser (RDCore.Runtime.Hosting), with
// the descriptions of the libraries its project references in the Symbols folder beside it, where the host reads them.
internal sealed class ComposeCommand(
    IConsoleMessageWriter writer, IFileSystem fileSystem, BenchRunner runner, ILibrarySource libraries, ILoggerFactory loggers, ProgramOutput? output = null)
    : ICliCommand
{
    private ProgramOutput Output => output ?? new ProgramOutput(fileSystem, AppContext.BaseDirectory);

    public string Name => CommandNames.ComposeCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.ComposeCommand.Alias];
    public string Summary => Resources.Compose_Summary;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var parsed = Parser.Default.ParseArguments<ComposeCommandOptions>(args);
        if (parsed.Errors.Any())
        {
            // CommandLine already wrote the usage/errors to stderr.
            return 2;
        }

        var options = parsed.Value;
        var composition = new ProgramCompositionOptions
        {
            Environment = RuntimeEnvironmentProfile.Of(runner.Environment) with { Configuration = options.Configuration },
            Workspace = runner.Workspace,
            Loggers = loggers,
        };
        ProgramCompositionImage image;
        IReadOnlyList<LibraryDescription> referenced;
        try
        {
            var composed = await ProgramComposer.ComposeImageAsync(
                options.Path ?? fileSystem.Directory.GetCurrentDirectory(), composition, token, fileSystem, libraries);
            composed.Loaded.Dispose();
            (image, referenced) = (composed.Image, composed.Libraries);
        }
        catch (ProgramCompositionException exception)
        {
            // nothing of the project runs while a module of it does not load (RD-VBAL 2.6.1): no composition is written of it.
            foreach (var problem in exception.Problems)
            {
                _ = Error(problem);
            }

            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            return Error(exception.Message);
        }

        var root = image.Root.IsFile ? image.Root.LocalPath : fileSystem.Directory.GetCurrentDirectory();
        var file = fileSystem.Path.GetFullPath(options.Output
            ?? fileSystem.Path.Combine(root, "bin", options.Configuration.ToString(), image.Project.Name + ProgramCompositionImage.Extension));
        var folder = fileSystem.Path.GetDirectoryName(file)!;
        _ = fileSystem.Directory.CreateDirectory(folder);
        await fileSystem.File.WriteAllTextAsync(file, image.ToJson(), token);
        Output.WriteLibraries(fileSystem.Path.Combine(folder, PlatformEnvironment.SymbolsDirectory), referenced);

        writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Success)
            .WithMessageBody(string.Format(CultureInfo.CurrentCulture, Resources.Compose_Written, image.Project.Name, file)));
        return 0;
    }

    private int Error(string message)
    {
        writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Error).WithMessageBody(message));
        return 1;
    }
}
