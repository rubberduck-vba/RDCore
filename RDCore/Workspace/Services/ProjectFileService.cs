using System.Text.Json;

namespace RDCore.Workspace.Services;

internal interface IProjectFileService
{
    ProjectFile Project { get; }

    Task SaveAsync();
    Task LoadAsync(string Uri);

    void AddReference(RDCoreReference reference);
    void RemoveReference(RDCoreReference reference);

    void AddSourceFile(WorkspaceDocument document, DocClassType? classType = default);
    void AddSourceFile(RDCoreModule module);
    void RemoveSourceFile(RDCoreModule module);

    void AddDocument(WorkspaceDocument document);
    void AddDocument(RDCoreFile document);
    void RemoveDocument(RDCoreFile document);

    void AddFolder(string folder);
    void RemoveFolder(string folder);
}

internal class ProjectFileService(
    System.IO.Abstractions.IPath ioPath,
    System.IO.Abstractions.IFile ioFile) : IProjectFileService
{
    private ProjectFile _projectFile = default!;
    public ProjectFile Project => _projectFile;

    public async Task LoadAsync(string Uri)
    {
        var path = ioPath.Combine(Uri, ProjectFile.FileName);
        using var stream = ioFile.Open(path, FileMode.Open);
        if (await JsonSerializer.DeserializeAsync<ProjectFile>(stream) is ProjectFile project)
        {
            _projectFile = project.WithUri(Uri);
        }

        throw new InvalidOperationException("Project file could not be deserialized from specified workspace root.");
    }

    public async Task SaveAsync()
    {
        var path = ioPath.Combine(Project.Uri, ProjectFile.FileName);
        if (_projectFile.IsDirty || !ioFile.Exists(path))
        {
            using var stream = ioFile.Open(path, FileMode.Create);
            await JsonSerializer.SerializeAsync(stream, Project);
        }
    }

    public void AddSourceFile(WorkspaceDocument document, DocClassType? classType = default)
    {
        if (Project.ProjectInfo.Modules.Any(e => e.Name == document.Name))
        {
            throw new InvalidOperationException($"A module named '{document.Name}' already exists in this project.");
        }

        var module = new RDCoreModule
        {
            Name = document.Name,
            RelativeUri = ioPath.GetRelativePath(Project.Uri, document.Id.Uri.GetFileSystemPath()),
            Super = classType
        };

        AddSourceFile(module);
    }

    public void AddDocument(WorkspaceDocument document)
    {
        var file = new RDCoreFile
        {
            RelativeUri = ioPath.GetRelativePath(Project.Uri, document.Id.Uri.GetFileSystemPath())
        };
        AddDocument(file);
    }

    public void AddReference(RDCoreReference reference) => _projectFile = Project.WithReference(reference);
    public void AddSourceFile(RDCoreModule module) => _projectFile = Project.WithModule(module);
    public void AddDocument(RDCoreFile document) => _projectFile = Project.WithDocument(document);
    public void AddFolder(string folder) => _projectFile = Project.WithFolder(folder);

    public void RemoveReference(RDCoreReference reference) => _projectFile = Project.WithoutReference(reference);
    public void RemoveSourceFile(RDCoreModule module) => _projectFile = Project.WithoutModule(module);
    public void RemoveDocument(RDCoreFile document) => _projectFile = Project.WithoutDocument(document);
    public void RemoveFolder(string folder) => _projectFile = Project.WithoutFolder(folder);
}
