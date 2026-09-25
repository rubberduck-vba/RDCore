using RDCore.SDK.Workspace;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The scratch workspace the interactive shell runs in when it was not given one.
/// </summary>
/// <remarks>
/// The shell is an ordinary LSP client and the platform is an ordinary platform: both want a
/// workspace root with a <c>.rdproj</c> in it, and the language server and the environment host are
/// separate processes that each read it from disk. So the shell scaffolds a real, private one rather
/// than inventing an in-memory shape only some of the platform could see — the program buffer becomes
/// a real module in a real project, which is also what makes it visible to the language server as an
/// ordinary document.
/// <para>
/// It is deleted on the way out. A shell started against a real workspace
/// (<c>rdc.exe --workspace …</c>) has nothing to scaffold and nothing to delete.
/// </para>
/// </remarks>
public sealed class ReplWorkspace : IDisposable
{
    private readonly IFileSystem _fileSystem;
    private bool _disposed;

    private ReplWorkspace(IFileSystem fileSystem, string root, string programPath)
    {
        _fileSystem = fileSystem;
        Root = root;
        ProgramPath = programPath;
    }

    /// <summary>The workspace root directory — what the platform is pointed at.</summary>
    public string Root { get; }

    /// <summary>The absolute path of the module file backing the program buffer.</summary>
    public string ProgramPath { get; }

    /// <summary>
    /// Scaffolds a private workspace with an empty program module in it.
    /// </summary>
    /// <param name="fileSystem">The file system to scaffold on.</param>
    /// <param name="projectWriter">Writes the <c>.rdproj</c>.</param>
    /// <param name="token">A token that cancels the write.</param>
    public static async Task<ReplWorkspace> CreateAsync(IFileSystem fileSystem, IProjectFileWriter projectWriter, CancellationToken token = default)
    {
        // one per process, so two shells never share a root; the pid also makes an abandoned one (a
        // killed process, which never reached Dispose) identifiable rather than anonymous litter.
        var root = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(), "RDCore", "shell", Environment.ProcessId.ToString());
        fileSystem.Directory.CreateDirectory(root);

        var relativeUri = $"{ReplProgram.ModuleName}.bas";
        var programPath = fileSystem.Path.Combine(root, relativeUri);
        await fileSystem.File.WriteAllTextAsync(programPath, new ReplProgram().ToModuleSource(), token);

        await projectWriter.SaveAsync(new ProjectFile(root, new RDCoreProject
        {
            Name = ReplProgram.ModuleName,
            Modules = [new RDCoreModule { RelativeUri = relativeUri }],
        }), token);

        return new ReplWorkspace(fileSystem, root, programPath);
    }

    /// <summary>
    /// Writes the program buffer to the module file backing it.
    /// </summary>
    /// <param name="program">The buffer to write.</param>
    /// <param name="token">A token that cancels the write.</param>
    public Task SaveProgramAsync(ReplProgram program, CancellationToken token = default)
        => _fileSystem.File.WriteAllTextAsync(ProgramPath, program.ToModuleSource(), token);

    /// <summary>Deletes the scratch workspace.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            if (_fileSystem.Directory.Exists(Root))
            {
                _fileSystem.Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // a scratch directory that outlives the process is litter, not a failure worth reporting
            // on the way out; the next run of the same pid reuses and overwrites it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
