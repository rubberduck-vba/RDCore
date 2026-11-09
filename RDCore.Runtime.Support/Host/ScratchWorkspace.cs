using RDCore.SDK.Workspace;
using System.IO.Abstractions;

namespace RDCore.Runtime.Support.Host;

// the folder the shell makes its scratch workspaces in, one per process: the environment host is given one as any other workspace.
internal static class ScratchWorkspace
{
    public static string Folder(IFileSystem fileSystem)
        => fileSystem.Path.TrimEndingDirectorySeparator(fileSystem.Path.GetFullPath(fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "RDCore", "shell")));

    // whether a folder is the scratch workspace of a shell, of this process or another.
    public static bool Is(IFileSystem fileSystem, string root)
        => fileSystem.Path.GetDirectoryName(fileSystem.Path.TrimEndingDirectorySeparator(fileSystem.Path.GetFullPath(root))) is { } parent
            && string.Equals(parent, Folder(fileSystem), WorkspacePaths.Comparison);
}
