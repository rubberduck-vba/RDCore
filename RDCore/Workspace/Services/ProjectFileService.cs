using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace RDCore.LanguageServer.Workspace.Services;

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

internal class ProjectFileService(ILogger<ProjectFileService> logger,
    System.IO.Abstractions.IPath ioPath,
    System.IO.Abstractions.IFile ioFile) : IProjectFileService
{
    private ProjectFile _projectFile = default!;
    public ProjectFile Project => _projectFile;

    public async Task LoadAsync(string Uri)
    {
        var path = ioPath.Combine(Uri, ProjectFile.FileName);
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Loading project file: {path}", path);
        }

        if (ioFile.Exists(path))
        {
            logger.LogTrace("Project file was found. Deserializing...");

            using var stream = ioFile.Open(path, FileMode.Open);
            if (await JsonSerializer.DeserializeAsync<ProjectFile>(stream) is ProjectFile project)
            {
                _projectFile = project.WithUri(Uri);

                logger.LogInformation("✅ LoadAsync completed. Project file was loaded successfully.");
            }
        }
        else
        {
            logger.LogTrace("No project file exists at the specified location.");
        }

        logger.LogWarning("⚠️ Project file could not be deserialized from specified workspace root.");
        throw new InvalidOperationException("Project file could not be deserialized from specified workspace root.");
    }

    public async Task SaveAsync()
    {
        var path = ioPath.Combine(Project.Uri, ProjectFile.FileName);
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Saving project file: {path}", path);
        }

        if (_projectFile.IsDirty || !ioFile.Exists(path))
        {
            using var stream = ioFile.Open(path, FileMode.Create);
            await JsonSerializer.SerializeAsync(stream, Project);

            logger.LogInformation("✅ SaveAsync completed. Project file was saved successfully.");
        }
        else
        {
            logger.LogTrace("There are no unsaved changes; nothing was done.");
        }
    }

    public void AddSourceFile(WorkspaceDocument document, DocClassType? classType = default)
    {
        var module = new RDCoreModule
        {
            Name = document.Name,
            RelativeUri = ioPath.GetRelativePath(Project.Uri, document.Id.Uri.GetFileSystemPath()),
            Super = classType
        };

        AddSourceFile(module);
    }

    public void AddSourceFile(RDCoreModule module)
    {
        if (Project.ProjectInfo.Modules.Any(e => e.Name == module.Name))
        {
            logger.LogWarning("⚠️ Source file names must be unique in a project, regardless of folder location.");
            throw new InvalidOperationException($"Project already contains a module named '{module.Name}'.");
        }

        _projectFile = Project.WithModule(module);
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Module '{uri}' was added to the project.", module.RelativeUri);
        }
        logger.LogInformation("✅ AddSourceFile completed. A new source file was successfully added to the project.");
    }


    public void AddDocument(WorkspaceDocument document)
    {
        var file = new RDCoreFile
        {
            RelativeUri = ioPath.GetRelativePath(Project.Uri, document.Id.Uri.GetFileSystemPath())
        };
        AddDocument(file);
    }
    public void AddDocument(RDCoreFile document)
    {
        _projectFile = Project.WithDocument(document);
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Document '{uri}' was added to the project.", document.RelativeUri);
        }
        logger.LogInformation("✅ AddDocument completed. A new document was successfully added to the project.");
    }

    public void AddReference(RDCoreReference reference)
    {
        if (Project.ProjectInfo.References.Any(e => e.Name == reference.Name))
        {
            logger.LogWarning("⚠️ Reference name conflict.");
            throw new InvalidOperationException($"Project already contains a reference named '{reference.Name}'.");
        }

        _projectFile = Project.WithReference(reference);
        logger.LogInformation("✅ AddReference completed. A new library reference was successfully added to the project.");
    }

    public void AddFolder(string folder)
    {
        if (Project.ProjectInfo.Folders.Contains(folder))
        {
            logger.LogWarning("⚠️ Folder name conflict.");
            throw new InvalidOperationException($"Project already contains a folder named '{folder}'.");
        }

        _projectFile = Project.WithFolder(folder);
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Folder '{folder}' was added to the project.", folder);
        }
        logger.LogInformation("✅ AddFolder completed. A new folder was successfully added to the project.");
    }

    public void RemoveReference(RDCoreReference reference)
    {
        if (reference.IsUnremovable)
        {
            logger.LogWarning("⚠️ The specified reference is not removable.");
            throw new InvalidOperationException($"Reference '{reference.Name}' is not removable.");
        }

        if (Project.ProjectInfo.References.Contains(reference))
        {
            _projectFile = Project.WithoutReference(reference);
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Reference '{reference}' was removed from the project.", reference.Name);
            }
            logger.LogInformation("✅ RemoveReference completed. A reference was successfully removed from the project.");
        }
        else
        {
            logger.LogWarning("⚠️ The specified reference '{reference}' was not found.", reference.Name);
            throw new InvalidOperationException($"Reference '{reference.Name}' could not be removed.");
        }
    }

    public void RemoveSourceFile(RDCoreModule module)
    {
        if (Project.ProjectInfo.Modules.Any(e => e.RelativeUri == module.RelativeUri))
        {
            _projectFile = Project.WithoutModule(module);
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Module '{uri}' was removed from the project.", module.RelativeUri);
            }
            logger.LogInformation("✅ RemoveSourceFile completed. A module was successfully removed from the project.");
        }
        else
        {
            logger.LogWarning("⚠️ The specified source file '{uri}' was not found.", module.RelativeUri);
            throw new InvalidOperationException($"Source file '{module.Name}' could not be removed.");
        }
    }

    public void RemoveDocument(RDCoreFile document)
    {
        if (Project.ProjectInfo.OtherFiles.Any(e => e.RelativeUri == document.RelativeUri))
        {
            _projectFile = Project.WithoutDocument(document);
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Document '{uri}' was removed from the project.", document.RelativeUri);
            }
            logger.LogInformation("✅ RemoveDocument completed. A document was successfully removed from the project.");
        }
        else
        {
            logger.LogWarning("⚠️ The specified document '{uri}' was not found.", document.RelativeUri);
            throw new InvalidOperationException($"Document '{document.RelativeUri}' could not be removed.");
        }
    }

    public void RemoveFolder(string folder)
    {
        if (Project.ProjectInfo.Folders.Contains(folder))
        {
            _projectFile = Project.WithoutFolder(folder);
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Folder '{folder}' was removed from the project.", folder);
            }
            logger.LogInformation("✅ RemoveFolder completed. A folder was successfully removed from the project.");
        }
        else
        {
            logger.LogWarning("⚠️ The specified folder '{folder}' was not found.", folder);
            throw new InvalidOperationException($"Folder '{folder}' could not be removed.");
        }
    }
}
