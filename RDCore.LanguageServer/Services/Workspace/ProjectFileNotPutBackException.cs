namespace RDCore.LanguageServer.Services.Workspace;

// a failed regeneration is undone; this is the load failing when putting the original back fails too, so the user must rename the backup back by hand (the
// message says so).
public sealed class ProjectFileNotPutBackException : IOException
{
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

    public string ProjectFilePath { get; }

    public string BackupPath { get; }

    public Exception Restore { get; }
}
