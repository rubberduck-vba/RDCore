using RDCore.SDK.ConsoleIO.Model;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>LOAD</c>: replaces the program with the one in a <c>.rdc</c> file, which is from then on a document of the language server for as long as it is the program.
/// </summary>
/// <remarks>
/// Says nothing on success, as its BASIC namesake does not. The file is opened as a document (<c>textDocument/didOpen</c>) with the version the buffer has, so what
/// the language server knows of the program is the text that was loaded, and every line typed after it is a change of that document.
/// </remarks>
internal sealed class LoadReplCommand(IFileSystem fileSystem) : IReplCommand
{
    public string Name => ReplCommandNames.Load;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Load_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        var path = ReplFilePathService.Resolve(fileSystem, arguments);
        if (path is null)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_BadFileName);
            return ReplCommandResult.Continue;
        }

        if (!fileSystem.File.Exists(path))
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_FileNotFound, path);
            return ReplCommandResult.Continue;
        }

        var rejected = context.Program.Load(await fileSystem.File.ReadAllTextAsync(path, token));
        if (rejected.Count > 0)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Load_NotAProgram,
                string.Format(Resources.Repl_Load_NotAProgram_Verbose, path, string.Join(", ", rejected)));
            return ReplCommandResult.Continue;
        }

        // the program that was there is gone, and so are the variables it made: the one that replaces it starts from nothing.
        await ReplExecution.DiscardProgramAsync(context, token);
        await context.Document.OpenAsync(path, token);
        return ReplCommandResult.Continue;
    }
}
