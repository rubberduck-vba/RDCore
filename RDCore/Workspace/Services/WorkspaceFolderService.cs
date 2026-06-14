namespace RDCore.LanguageServer.Workspace.Services;

internal interface IWorkspaceFolderService
{
    void Create(ProjectFile model);
    void Create(string relativeUri, string rootUri);
    void Copy(string source, string destination);
    void Rename(string relativeUri, string rootUri);
    void Delete(string path);
    string[] GetFiles(string path);
}

internal class WorkspaceFolderService(
    System.IO.Abstractions.IPath ioPath,
    System.IO.Abstractions.IFile ioFile,
    System.IO.Abstractions.IDirectory ioDirectory) : IWorkspaceFolderService
{
    public void Copy(string source, string destination) => ioFile.Copy(source, destination, overwrite: true);

    public void Create(ProjectFile model)
    {
        var rootUri = model.Uri;
        if (!ioDirectory.Exists(rootUri))
        {
            ioDirectory.CreateDirectory(rootUri);
        }

        var srcRoot = ioPath.Combine(rootUri, "src");
        if (!ioDirectory.Exists(srcRoot))
        {
            ioDirectory.CreateDirectory(srcRoot);
        }

        var folders = model.ProjectInfo.GetWorkspaceFolders(srcRoot);
        foreach (var path in folders)
        {
            ioDirectory.CreateDirectory(path);
        }
    }

    public void Create(string relativeUri, string rootUri) => ioDirectory.CreateDirectory(ioPath.Combine(rootUri, relativeUri));

    public void Delete(string path) => ioDirectory.Delete(path);

    public string[] GetFiles(string path) => ioDirectory.GetFiles(path);

    public void Rename(string relativeUri, string rootUri) => ioDirectory.Move(ioPath.Combine(rootUri, relativeUri), ioPath.Combine(rootUri, relativeUri));
}