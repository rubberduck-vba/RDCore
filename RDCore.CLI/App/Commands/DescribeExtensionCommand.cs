using CommandLine;
using Microsoft.Extensions.Options;
using RDCore.CLI.App.Messages;
using RDCore.CLI.App.Messages.Model;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions;
using System.Text.Json;

namespace RDCore.CLI.App.Commands;

/// <summary>
/// The parsed <c>describe-ext</c> verb arguments.
/// </summary>
internal sealed class DescribeExtensionOptions
{
    [Value(0, MetaName = "extension", Required = true, HelpText = "The extension executable file name (e.g. RDCore.Diagnostics.exe).")]
    public string Name { get; set; } = string.Empty;

    [Option("description", HelpText = "A short description of the extension. Falls back to the assembly description.")]
    public string? Description { get; set; }

    [Option("overwrite", HelpText = "Overwrite an existing manifest.")]
    public bool Overwrite { get; set; }

    [Option("unsafe-dev-mode", Hidden = true, HelpText = "Allow describing an unsigned local build.")]
    public bool UnsafeDevMode { get; set; }
}

/// <summary>
/// Generates an <c>extension.manifest.json</c> for the extension in the current directory by
/// reflecting the advertised capabilities off its executable. Runs only under
/// <c>--unsafe-dev-mode</c>; invoked per extension by <c>PlatformPublish.ps1</c>.
/// </summary>
internal sealed class DescribeExtensionCommand(
    IConsoleMessageWriter writer,
    IFileSystem fileSystem,
    IOptions<SdkAppOptions> options,
    IExtensionsProvider provider) : ICliCommand
{
    public string Name => CommandNames.DescribeExtensionCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.DescribeExtensionCommand.Alias];
    public string Summary => Resources.DescribeExtension_Summary;

    public Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var parsed = Parser.Default.ParseArguments<DescribeExtensionOptions>(args);
        if (parsed.Errors.Any())
        {
            // CommandLine already wrote the usage/errors to stderr.
            return Task.FromResult(2);
        }

        return Task.FromResult(Execute(parsed.Value));
    }

    private int Execute(DescribeExtensionOptions args)
    {
        if (!args.UnsafeDevMode && !options.Value.Server.UnsafeDevMode)
        {
            Write(MessageKind.Error, Resources.DescribeExtension_DevModeRequired, args.Name);
            return 1;
        }

        if (fileSystem.Path.GetFileName(args.Name) != args.Name)
        {
            Write(MessageKind.Error, Resources.Command_InvalidArgs, args.Name);
            return 1;
        }

        var manifestFileName = options.Value.Platform.Extensions.Manifest;
        if (!args.Overwrite && fileSystem.File.Exists(manifestFileName))
        {
            Write(MessageKind.Warning, Resources.DescribeExtension_AlreadyExists, args.Name);
            return 0;
        }

        var description = args.Description ?? Resources.Extension_DefaultDescription;
        if (provider.Describe(args.Name, description) is not ExtensionInfo info)
        {
            Write(MessageKind.Error, Resources.DescribeExtension_Failed, args.Name);
            return 1;
        }

        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        fileSystem.File.WriteAllText(manifestFileName, json);

        Write(MessageKind.Success,
            string.Format(Resources.DescribeExtension_Written, manifestFileName),
            string.Join(", ", info.Capabilities.Select(capability => capability.Name)));
        return 0;
    }

    private void Write(MessageKind kind, string body, string verbose)
        => writer.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(kind)
            .WithTitle(Resources.DescribeExtension_Title)
            .WithMessageBody(body)
            .WithVerbose(verbose));
}
