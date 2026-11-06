namespace RDCore.SDK.Workspace;

/// <summary>
/// A project file the user chose to regenerate (<see cref="ProjectFileRegeneration"/>) that could not be written, nor put back as it was: the file as it was
/// stays where it was kept before it was regenerated (<see cref="ProjectFileRegeneration.BackupPathOf"/>), and the message says where, in the user's language,
/// for them to put it back themselves.
/// </summary>
/// <remarks>
/// <see cref="Exception.InnerException"/> is why the project file could not be regenerated; why it could not be put back is in <see cref="Restore"/>.
/// </remarks>
public sealed class ProjectFileNotPutBackException : IOException
{
    /// <summary>
    /// Makes the exception, its message in <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>.
    /// </summary>
    /// <param name="projectFilePath">The project file.</param>
    /// <param name="backupPath">Where the file as it was is.</param>
    /// <param name="regeneration">Why the project file could not be regenerated.</param>
    /// <param name="restore">Why it could not be put back.</param>
    public ProjectFileNotPutBackException(string projectFilePath, string backupPath, Exception regeneration, Exception restore)
        : base(ProjectFileRegeneration.Say("ProjectFile_Regenerate_NotPutBack", projectFilePath, regeneration?.Message ?? string.Empty, restore?.Message ?? string.Empty, backupPath),
            regeneration)
    {
        ArgumentNullException.ThrowIfNull(regeneration);
        ArgumentNullException.ThrowIfNull(restore);

        BackupPath = backupPath;
        Restore = restore;
    }

    /// <summary>Where the project file as it was is.</summary>
    public string BackupPath { get; }

    /// <summary>Why the project file could not be put back as it was.</summary>
    public Exception Restore { get; }
}
