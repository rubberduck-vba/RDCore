using RDCore.SDK.Workspace;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// The file a <c>LOAD</c> or a <c>SAVE</c> was told to act on.
/// </summary>
internal static class ReplFilePathService
{
    /// <summary>
    /// The absolute path of the file that was typed, which may be quoted and may leave out the extension of a program (<see cref="BasicProgramText.Extension"/>).
    /// </summary>
    /// <param name="fileSystem">The file system the path is of.</param>
    /// <param name="argument">What was typed after the command.</param>
    /// <returns><see langword="null"/> when nothing was, or when what was is not a path.</returns>
    public static string? Resolve(IFileSystem fileSystem, string argument)
    {
        var name = argument.Trim().Trim('"');
        if (name.Length == 0 || name.IndexOfAny(fileSystem.Path.GetInvalidPathChars()) >= 0)
        {
            return null;
        }

        if (!fileSystem.Path.HasExtension(name))
        {
            name += BasicProgramText.Extension;
        }

        return fileSystem.Path.GetFullPath(name);
    }
}
