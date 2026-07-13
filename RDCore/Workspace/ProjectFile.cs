using RDCore.SDK.Extensibility;
using System.Text.Json.Serialization;

namespace RDCore.LanguageServer.Workspace;

internal sealed record class ProjectFile : IEquatable<ProjectFile>
{
    /// <summary>
    /// The <c>ProjectFile</c> filename is the same for all instances.
    /// </summary>
    public static readonly string FileName = ".rdproj";

    public ProjectFile() : this(RDCoreUriNamespaces.RDCoreWorkspaceUri) { }
    public ProjectFile(string uri) : this(uri, new()) { }
    public ProjectFile(string uri, RDCoreProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri, nameof(uri));
        ArgumentNullException.ThrowIfNull(project, nameof(project));

        Uri = uri;
        ProjectInfo = project;
        Version = CoreLanguageServerHost.Info.Version!.ToString(3);
        IsDirty = false;
        Configuration = [];
    }
    public ProjectFile(ProjectFile source)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        Uri = source.Uri;
        ProjectInfo = source.ProjectInfo;
        Version = CoreLanguageServerHost.Info.Version!.ToString(3);
        IsDirty = false;
        Configuration = source.Configuration;
    }

    /// <summary>
    /// <c>true</c> if the project file has unsaved changes.
    /// </summary>
    [JsonIgnore]
    public bool IsDirty { get; init; }

    /// <summary>
    /// The location of the project file on disk.
    /// </summary>
    /// <remarks>
    /// This property is assigned on load rather than de/serialized, and serves as the workspace root everything else is relative to.
    /// </remarks>
    [JsonIgnore]
    public string Uri { get; init; } = string.Empty;
    /// <summary>
    /// The <c>RDCore.ServerApp</c> version that this project was created with.
    /// </summary>
    public string Version { get; init; }
    /// <summary>
    /// An array of string values containing the relative paths of configuration settings file(s) included in this project.
    /// </summary>
    public string[] Configuration { get; init; }
    /// <summary>
    /// Describes the workspace structure (files, folders, project references, etc.)
    /// </summary>
    public RDCoreProject ProjectInfo { get; init; } = new RDCoreProject();

    public ProjectFile WithUri(string uri) => this with { Uri = uri };

    public ProjectFile WithReference(RDCoreReference reference) => this with { ProjectInfo = ProjectInfo.WithReference(reference), IsDirty = true };
    public ProjectFile WithModule(RDCoreModule module) => this with { ProjectInfo = ProjectInfo.WithModule(module), IsDirty = true };
    public ProjectFile WithDocument(RDCoreFile document) => this with { ProjectInfo = ProjectInfo.WithDocument(document), IsDirty = true };
    public ProjectFile WithFolder(string folder) => this with { ProjectInfo = ProjectInfo.WithFolder(folder), IsDirty = true };

    public ProjectFile WithoutReference(RDCoreReference reference) => this with { ProjectInfo = ProjectInfo.WithoutReference(reference), IsDirty = true };
    public ProjectFile WithoutModule(RDCoreModule module) => this with { ProjectInfo = ProjectInfo.WithoutModule(module), IsDirty = true };
    public ProjectFile WithoutDocument(RDCoreFile document) => this with { ProjectInfo = ProjectInfo.WithoutDocument(document), IsDirty = true };
    public ProjectFile WithoutFolder(string folder) => this with { ProjectInfo = ProjectInfo.WithoutFolder(folder), IsDirty = true };

    public override int GetHashCode() => Uri.GetHashCode();
    public bool Equals(ProjectFile? other) => other?.Uri is string uri && Uri.Equals(uri);
}

internal record class RDCoreProject
{
    public string Name { get; init; } = string.Empty;
    public RDCoreReference[] References { get; init; } = [RDCoreReference.VBStandardLibrary];
    public RDCoreModule[] Modules { get; init; } = [];
    public RDCoreFile[] OtherFiles { get; init; } = [];
    public string[] Folders { get; set; } = [];

    public HashSet<string> GetWorkspaceFolders(string srcRoot) =>
        [
            .. Modules.Select(module => Path.Combine(srcRoot, module.RelativeUri)),
            .. OtherFiles.Select(file => Path.Combine(srcRoot, file.RelativeUri)),
            .. Folders.Select(folder => Path.Combine(srcRoot, folder)),
        ];

    public IEnumerable<RDCoreFile> GetFolderFiles(string folder) =>
        [
            .. Modules.Where(m => Path.GetDirectoryName(m.RelativeUri)!.Replace('\\', '/').StartsWith(folder)),
            .. OtherFiles.Where(f => Path.GetDirectoryName(f.RelativeUri)!.Replace('\\', '/').StartsWith(folder)),
        ];

    public RDCoreProject WithName(string name) => this with { Name = name };
    public RDCoreProject WithReference(RDCoreReference reference) => this with { References = [.. References, reference] };
    public RDCoreProject WithModule(RDCoreModule module) => this with { Modules = [.. Modules, module] };
    public RDCoreProject WithDocument(RDCoreFile document) => this with { OtherFiles = [.. OtherFiles, document] };
    public RDCoreProject WithFolder(string folder) => this with { Folders = [.. Folders, folder] };

    public RDCoreProject WithoutReference(RDCoreReference reference) => this with { References = [.. References.Where(r => r != reference)] };
    public RDCoreProject WithoutModule(RDCoreModule module) => this with { Modules = [.. Modules.Where(m => m != module)] };
    public RDCoreProject WithoutDocument(RDCoreFile document) => this with { OtherFiles = [.. OtherFiles.Where(d => d != document)] };
    public RDCoreProject WithoutFolder(string folder) => this with { Folders = [.. Folders.Where(f => f != folder)] };
}

internal sealed record class RDCoreReference : IEquatable<RDCoreReference>
{
    public static RDCoreReference VBStandardLibrary { get; } = new RDCoreReference
    {
        Name = "VBA",
        Guid = new Guid("000204ef-0000-0000-c000-000000000046"),
        AbsolutePath = "C:\\Program Files\\Microsoft Office\\root\\vfs\\ProgramFilesCommonX64\\Microsoft Shared\\VBA\\VBA7.1\\VBE7.DLL",
        Major = 4,
        Minor = 2,
        IsUnremovable = true,
    };

    public string Name { get; init; } = string.Empty;
    public string? AbsolutePath { get; init; }
    public Guid? Guid { get; init; }
    public int? Major { get; init; }
    public int? Minor { get; init; }
    public string? TypeLibInfoPath { get; init; }

    public bool IsUnremovable { get; init; } = false;

    public override int GetHashCode() => HashCode.Combine(Name, Guid, Major, Minor);

    public bool Equals(RDCoreReference? other) => other?.Name is string name && Name.Equals(name);
}

internal record class RDCoreFile : IEquatable<RDCoreFile>
{
    public string RelativeUri { get; init; } = string.Empty;

    [JsonIgnore]
    public string Extension => RelativeUri[^RelativeUri.LastIndexOf('.')..];

    [JsonIgnore]
    public string DefaultName => Path.GetFileNameWithoutExtension(RelativeUri.Split(Path.PathSeparator).Last());

    public override int GetHashCode() => RelativeUri.GetHashCode();

    public virtual bool Equals(RDCoreFile? other) => other?.RelativeUri is string uri && RelativeUri.Equals(uri);
}

internal enum DocClassType
{
    Unknown = 0,
    ExcelWorkbook = 1,
    ExcelWorksheet = 2,
    AccessForm = 3,
    AccessReport = 4,
}

internal record class RDCoreModule : RDCoreFile
{
    /// <summary>
    /// The name of the module; must be unique across the entire workspace.
    /// </summary>
    /// <remarks>
    /// The value of the module's <c>VB_Name</c> attribute.
    /// </remarks>
    public string Name { get; init; } = string.Empty;
    public DocClassType? Super { get; init; }
}
