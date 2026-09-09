using Microsoft.Extensions.Logging;
using RDCore.SDK.Workspace;
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
    System.IO.Abstractions.IFile ioFile,
    IProjectFileLoader projectFileLoader) : IProjectFileService
{
    private ProjectFile _projectFile = default!;
    public ProjectFile Project => _projectFile;

    public async Task LoadAsync(string Uri)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Loading project file under: {uri}", Uri);
        }

        // the SDK loader is the single deserialization path, shared with the environment host.
        _projectFile = await projectFileLoader.LoadAsync(Uri);
        logger.LogInformation("✅ LoadAsync completed. Project file was loaded successfully.");
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
            RelativeUri = ioPath.GetRelativePath(Project.Uri, document.Id.Uri.GetFileSystemPath()),
            Super = classType
        };

        AddSourceFile(module);
    }

    public void AddSourceFile(RDCoreModule module)
    {
        // uniqueness is on the module's Attribute VB_Name (case-insensitive, like every VBA
        // identifier) — two files cannot share one — not on the file name.
        var moduleName = ResolveModuleName(module);
        if (Project.ProjectInfo.Modules.Any(e => string.Equals(ResolveModuleName(e), moduleName, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning("⚠️ Module names must be unique in a project, regardless of folder location.");
            throw new InvalidOperationException($"Project already contains a module named '{moduleName}'.");
        }

        _projectFile = Project.WithModule(module);
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Module '{uri}' was added to the project.", module.RelativeUri);
        }
        logger.LogInformation("✅ AddSourceFile completed. A new source file was successfully added to the project.");
    }

    // the module name is its Attribute VB_Name; there is no parser here, so the raw source is scanned
    // for it, and the file name is the fallback when the source is unreadable or declares none.
    private string ResolveModuleName(RDCoreModule module)
    {
        var path = ioPath.Combine(Project.Uri, module.RelativeUri);
        string? source = null;
        try
        {
            if (ioFile.Exists(path))
            {
                source = ioFile.ReadAllText(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogTrace(exception, "Could not read '{path}' to resolve its module name; falling back to the file name.", path);
        }

        return ModuleName.Resolve(source, module.RelativeUri);
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
            throw new InvalidOperationException($"Source file '{module.RelativeUri}' could not be removed.");
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
