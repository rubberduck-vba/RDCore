namespace RDCore.SDK.Workspace;

/// <summary>
/// A project file the user chose to regenerate (<see cref="ProjectFileRegeneration"/>) that could not be written, nor put back as it was: the file as it was
/// stays where it was kept before it was regenerated (<see cref="ProjectFileRegeneration.BackupPathOf"/>), and the message says where, in the user's language,
/// for them to put it back themselves.
/// </summary>
/// <remarks>
/// <para>
/// A regeneration that can't be written is undone: what was written is deleted, and the file as it was is moved back to <see cref="ProjectFilePath"/>. This is
/// what a load fails with when that move fails too (the backup is held open by another process, say), so the folder is left with no project file: the next
/// start would make one up from the folder's source files, without what only the file said, such as the project's references.
/// </para>
/// <para>
/// <see cref="Exception.Message"/> is what to show the user: it names the project file, why it couldn't be regenerated and why it couldn't be put back, and
/// tells them to rename <see cref="BackupPath"/> back to <see cref="ProjectFilePath"/>. It is in <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>
/// as it was when the exception was made. The language server shows it to the client as it shows any workspace that can't be loaded.
/// </para>
/// <para>
/// <see cref="Exception.InnerException"/> is why the project file could not be regenerated; why it could not be put back is <see cref="Restore"/>.
/// </para>
/// </remarks>
/// <seealso cref="ProjectFileRegeneration"/>
public sealed class ProjectFileNotPutBackException : IOException
{
    /// <summary>
    /// Makes the exception, its message in <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>.
    /// </summary>
    /// <param name="projectFilePath">The project file, where the file as it was is to be put back.</param>
    /// <param name="backupPath">Where the file as it was is (<see cref="ProjectFileRegeneration.BackupPathOf"/>).</param>
    /// <param name="regeneration">Why the project file could not be regenerated: the <see cref="Exception.InnerException"/>.</param>
    /// <param name="restore">Why it could not be put back.</param>
    /// <exception cref="ArgumentException"><paramref name="projectFilePath"/> or <paramref name="backupPath"/> is null, empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="regeneration"/> or <paramref name="restore"/> is null.</exception>
    public ProjectFileNotPutBackException(string projectFilePath, string backupPath, Exception regeneration, Exception restore)
        : base(ProjectFileRegeneration.Say("ProjectFile_Regenerate_NotPutBack", projectFilePath, regeneration?.Message ?? string.Empty, restore?.Message ?? string.Empty, backupPath),
            regeneration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentNullException.ThrowIfNull(regeneration);
        ArgumentNullException.ThrowIfNull(restore);

        ProjectFilePath = projectFilePath;
        BackupPath = backupPath;
        Restore = restore;
    }

    /// <summary>The project file, which is no longer there: where the file as it was is to be put back.</summary>
    public string ProjectFilePath { get; }

    /// <summary>Where the project file as it was is: next to it, as <c>.rdproj.bak</c> or the first free <c>.rdproj.N.bak</c>.</summary>
    public string BackupPath { get; }

    /// <summary>Why the project file could not be put back as it was.</summary>
    public Exception Restore { get; }
}
