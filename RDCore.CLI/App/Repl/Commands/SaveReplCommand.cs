using RDCore.SDK.ConsoleIO.Model;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>SAVE</c>: writes the program to a <c>.rdc</c> file, and tells the language server around the write the way an editor does: about to save
/// (<c>willSave</c>, <c>willSaveWaitUntil</c>), then saved (<c>didSave</c>).
/// </summary>
/// <remarks>
/// With no file name, the program is saved to the file it was loaded from or last saved to. Saving to another file is saving a document as another: the
/// language server is told the first is closed and the second is open.
/// </remarks>
internal sealed class SaveReplCommand(IFileSystem fileSystem) : IReplCommand
{
    public string Name => "SAVE";
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Save_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        var path = arguments.Length == 0 && context.Document.Uri is { IsFile: true } open
            ? open.LocalPath
            : ReplFilePath.Resolve(fileSystem, arguments);
        if (path is null)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_BadFileName);
            return ReplCommandResult.Continue;
        }

        if (context.Document.Uri?.LocalPath != path)
        {
            await context.Document.OpenAsync(path, token);
        }

        var text = await context.Document.WillSaveAsync(token);
        await fileSystem.File.WriteAllTextAsync(path, text, token);
        await context.Document.SavedAsync(text, token);
        return ReplCommandResult.Continue;
    }
}
