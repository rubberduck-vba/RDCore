using RDCore.CLI.App.Commands;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Mcp;

// rdc mcp --workspace <path>: the platform brought up against the workspace, and its tools served over standard input and output to an assistant's host.
internal sealed class McpCommand(IFileSystem fileSystem, TextWriter? error = null) : ICliCommand
{
    private const string WorkspaceSwitch = "--workspace";

    // what the command exits with when its command line is not one it runs.
    public const int Refused = 2;

    private TextWriter Error => error ?? global::System.Console.Error;

    public string Name => CommandNames.McpCommand;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Mcp_Summary;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        if (WorkspaceOf(args) is not { } path)
        {
            await Error.WriteLineAsync(Resources.Mcp_Usage);
            return Refused;
        }

        var workspace = fileSystem.Path.GetFullPath(path);
        if (!fileSystem.Directory.Exists(workspace))
        {
            await Error.WriteLineAsync(string.Format(Resources.Mcp_NoSuchWorkspace, workspace));
            return Refused;
        }

        using var host = new RDCoreMcpClientHost(workspace, McpStandardStreams.Input, McpStandardStreams.Output);
        return await host.RunAsync([WorkspaceSwitch, workspace]);
    }

    // --workspace <path> or --workspace=<path>, and nothing else.
    internal static string? WorkspaceOf(IReadOnlyList<string> args) => args switch
    {
        [WorkspaceSwitch, var path] when path.Length > 0 && !path.StartsWith('-') => path,
        [var single] when single.StartsWith(WorkspaceSwitch + "=", StringComparison.Ordinal) && single.Length > WorkspaceSwitch.Length + 1
            => single[(WorkspaceSwitch.Length + 1)..],
        _ => null,
    };
}
