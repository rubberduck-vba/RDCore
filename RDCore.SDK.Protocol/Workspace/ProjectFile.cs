using RDCore.SDK.Extensibility;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Workspace;

/// <summary>
/// The in-memory model of a workspace's <c>.rdproj</c> project file (<strong>RD-VBAL §2.2.3</strong>).
/// </summary>
/// <remarks>
/// It reads with <see cref="JsonSerializer"/> and nothing else, so a client of the platform, such as an IDE's add-in, reads a project's modules and files
/// without the platform.
/// </remarks>
public sealed record class ProjectFile : IEquatable<ProjectFile>
{
    /// <summary>
    /// The <c>ProjectFile</c> filename is the same for all instances.
    /// </summary>
    public static readonly string FileName = ".rdproj";

    /// <summary>
    /// The version of the <c>.rdproj</c> format the platform reads and writes, as <c>major.minor</c> (<strong>RD-VBAL §2.2.3</strong>), which a project file
    /// is stamped with (<see cref="Version"/>). A change an older platform can still read, such as a new member, raises the minor; a change it cannot read
    /// raises the major.
    /// </summary>
    /// <remarks>
    /// Before the format had a version of its own, a project file was stamped with the version of the platform that wrote it: <c>1.0.0</c> by the first builds,
    /// <c>0.1.x</c> by the next. They read as the format 1.0 and an older one (<see cref="SchemaOf"/>), and are both the format 1.0.
    /// </remarks>
    public const string SchemaVersion = "1.0";

    /// <summary>
    /// How the format a project file says it is (<see cref="Version"/>) compares with the one the platform reads and writes (<see cref="SchemaVersion"/>).
    /// </summary>
    /// <remarks>
    /// Only the major and minor are compared, and whatever follows them is not: <c>1.0.0</c> is the format 1.0, and <c>0.87.2-rc3</c> the format 0.87. A
    /// lone number is a major: <c>2</c> is the format 2.0.
    /// </remarks>
    /// <param name="version">What a project file says (<see cref="Version"/>).</param>
    public static ProjectFileSchemaMatch SchemaOf(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return ProjectFileSchemaMatch.Unstamped;
        }

        if (!TryParseSchema(version, out var major, out var minor) || !TryParseSchema(SchemaVersion, out var currentMajor, out var currentMinor))
        {
            return ProjectFileSchemaMatch.Unparseable;
        }

        return major > currentMajor ? ProjectFileSchemaMatch.NewerMajor
            : major < currentMajor ? ProjectFileSchemaMatch.Older
            : minor > currentMinor ? ProjectFileSchemaMatch.NewerMinor
            : minor < currentMinor ? ProjectFileSchemaMatch.Older
            : ProjectFileSchemaMatch.Same;
    }

    // "major.minor", followed by '.', '-' or '+' and anything at all; or "major", followed by '-' or '+' and anything: "1.x" is no version.
    private static bool TryParseSchema(string version, out int major, out int minor)
    {
        major = minor = 0;
        var match = System.Text.RegularExpressions.Regex.Match(version.Trim(), @"^(\d+)(?:\.(\d+)(?:[.+-].*)?|[+-].*)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success
            && int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out major)
            && (!match.Groups[2].Success || int.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out minor));
    }

    /// <summary>
    /// Reads a <c>.rdproj</c> as the platform reads it: the project file at <paramref name="workspaceRoot"/>, with what a file may leave out made empty and the
    /// standard library dropped from its references (<see cref="RDCoreReference.IsStandardLibrary"/>).
    /// </summary>
    /// <remarks>
    /// A list the file gives as <c>null</c> (<c>"Modules": null</c>), or a project the file gives as <c>null</c>, reads as empty, and a <c>null</c> item of a
    /// list is left out. The file is UTF-8, with or without a byte order mark; its member names are matched as <see cref="JsonSerializer"/> matches them by
    /// default, case-sensitively, except a file entry's <c>RelativeUri</c> and <c>Super</c>, which are matched whatever their case. A file that is no JSON throws the <see cref="JsonException"/> that says where.
    /// </remarks>
    /// <param name="stream">The file's content.</param>
    /// <param name="workspaceRoot">The workspace root the project file is at (<see cref="Uri"/>).</param>
    /// <param name="token">Cancels the read.</param>
    /// <exception cref="JsonException">The file is no JSON, or no JSON object.</exception>
    public static async Task<ProjectFile> ReadAsync(Stream stream, string workspaceRoot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot, nameof(workspaceRoot));

        var project = await JsonSerializer.DeserializeAsync<ProjectFile>(stream, cancellationToken: token)
            ?? throw new JsonException($"A {FileName} is a JSON object, not null.");
        return project.WithUri(workspaceRoot).Normalized();
    }

    // what a file may leave out, as the rest of the platform expects it: empty, never null; and the standard library, which is every project's own and no
    // reference of it - a project file written before lists it, with the path of Office's VBE7.DLL - left out.
    private ProjectFile Normalized()
    {
        var info = ProjectInfo ?? new RDCoreProject();
        info = info with
        {
            Name = info.Name ?? string.Empty,
            References = [.. (info.References ?? []).Where(reference => reference is not null && !reference.IsStandardLibrary)],
            Modules = [.. (info.Modules ?? []).Where(module => module is not null)],
            OtherFiles = [.. (info.OtherFiles ?? []).Where(file => file is not null)],
            Folders = [.. (info.Folders ?? []).Where(folder => folder is not null)],
            PrecompilerConstants = info.PrecompilerConstants ?? [],
        };
        return this with { ProjectInfo = info, Configuration = Configuration ?? [], Version = Version ?? string.Empty };
    }

    /// <summary>
    /// A project file of an empty project, at the platform's virtual workspace (<see cref="RDCoreUriNamespaces.RDCoreWorkspaceUri"/>).
    /// </summary>
    public ProjectFile() : this(RDCoreUriNamespaces.RDCoreWorkspaceUri) { }

    /// <summary>
    /// A project file of an empty project, at <paramref name="uri"/>.
    /// </summary>
    /// <param name="uri">The workspace root (<see cref="Uri"/>).</param>
    public ProjectFile(string uri) : this(uri, new()) { }

    /// <summary>
    /// A project file of <paramref name="project"/>, at <paramref name="uri"/>, that no platform wrote yet (its <see cref="Version"/> is empty).
    /// </summary>
    /// <param name="uri">The workspace root (<see cref="Uri"/>).</param>
    /// <param name="project">The project the file describes.</param>
    public ProjectFile(string uri, RDCoreProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri, nameof(uri));
        ArgumentNullException.ThrowIfNull(project, nameof(project));

        Uri = uri;
        ProjectInfo = project;
        Version = string.Empty;
        IsDirty = false;
        Configuration = [];
    }

    /// <summary>
    /// A copy of <paramref name="source"/>: of the same version, and as synthesized as it was, until it is written. A copy is clean.
    /// </summary>
    /// <param name="source">The project file to copy.</param>
    public ProjectFile(ProjectFile source)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        Uri = source.Uri;
        ProjectInfo = source.ProjectInfo;
        Version = source.Version;
        ExtensionData = source.ExtensionData;
        IsSynthesized = source.IsSynthesized;
        // deliberate: a copy is a new, clean value, and a change says it is dirty (the With* methods, ProjectFileService.Update).
        IsDirty = false;
        Configuration = source.Configuration;
    }

    /// <summary>
    /// The project file stamped with the version of a format (<see cref="Version"/>).
    /// </summary>
    /// <param name="version">The format's version, as <c>major.minor</c>.</param>
    public ProjectFile WithVersion(string version) => this with { Version = version };

    /// <summary>
    /// The project file as the platform writes it, stamped with the format it writes (<see cref="SchemaVersion"/>), unless a newer format is what it says.
    /// </summary>
    /// <remarks>
    /// A project file of a newer format keeps its stamp: what this platform does not know of it is written back as it was read (<see cref="ExtensionData"/>),
    /// and the platform that wrote it reads it as its own. A project file of an older format, or no format, or one that cannot be read, is stamped with this one.
    /// </remarks>
    public ProjectFile WithSchemaVersion() => SchemaOf(Version) is ProjectFileSchemaMatch.NewerMinor or ProjectFileSchemaMatch.NewerMajor ? this : WithVersion(SchemaVersion);

    /// <summary>
    /// <c>true</c> if the project file has unsaved changes.
    /// </summary>
    [JsonIgnore]
    public bool IsDirty { get; init; }

    /// <summary>
    /// <c>true</c> for a project the platform made up for a folder that has no <c>.rdproj</c>, from the source files under it, and that was not written yet.
    /// </summary>
    /// <remarks>
    /// Opening a folder never writes a project file into it: a synthesized project lives in memory until the first change made to it - a file the client
    /// created, renamed or deleted - is saved, and it is then a <c>.rdproj</c> like any other (<strong>RD-VBAL §2.2.3</strong>).
    /// </remarks>
    [JsonIgnore]
    public bool IsSynthesized { get; init; }

    /// <summary>
    /// The location of the project file on disk.
    /// </summary>
    /// <remarks>
    /// This property is assigned on load rather than de/serialized, and serves as the workspace root everything else is relative to.
    /// </remarks>
    [JsonIgnore]
    public string Uri { get; init; } = string.Empty;
    /// <summary>
    /// The version of the <c>.rdproj</c> format the project file is in (<see cref="SchemaVersion"/>): kept as it is read and copied, and stamped when it is
    /// written (<see cref="WithSchemaVersion"/>). It is empty for a project file no platform wrote yet.
    /// </summary>
    /// <remarks>
    /// It is read whatever it is: a number is read as its text, and anything else that is not a string as its JSON, which no format is
    /// (<see cref="ProjectFileSchemaMatch.Unparseable"/>). A project file is never refused for what it says here.
    /// </remarks>
    [JsonConverter(typeof(ProjectFileVersionJsonConverter))]
    public string Version { get; init; }
    /// <summary>
    /// The members of the project file this model does not know, as they were read, which are written back: a project file a newer platform wrote keeps
    /// what that platform added when an older one writes it (<see cref="SchemaVersion"/>).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    /// <summary>
    /// An array of string values containing the relative paths of configuration settings file(s) included in this project.
    /// </summary>
    public string[] Configuration { get; init; }
    /// <summary>
    /// Describes the workspace structure (files, folders, project references, etc.)
    /// </summary>
    public RDCoreProject ProjectInfo { get; init; } = new RDCoreProject();

    /// <summary>The project file at another workspace root (<see cref="Uri"/>); this is not a change to the project.</summary>
    /// <param name="uri">The workspace root.</param>
    public ProjectFile WithUri(string uri) => this with { Uri = uri };

    /// <summary>The project file with <paramref name="reference"/> added last to the project's references; it is dirty.</summary>
    /// <param name="reference">The reference to add.</param>
    public ProjectFile WithReference(RDCoreReference reference) => this with { ProjectInfo = ProjectInfo.WithReference(reference), IsDirty = true };
    /// <summary>The project file with <paramref name="module"/> added to the project's modules; it is dirty.</summary>
    /// <param name="module">The module to add.</param>
    public ProjectFile WithModule(RDCoreModule module) => this with { ProjectInfo = ProjectInfo.WithModule(module), IsDirty = true };
    /// <summary>The project file with <paramref name="document"/> added to the project's other files; it is dirty.</summary>
    /// <param name="document">The file to add.</param>
    public ProjectFile WithDocument(RDCoreFile document) => this with { ProjectInfo = ProjectInfo.WithDocument(document), IsDirty = true };
    /// <summary>The project file with <paramref name="folder"/> added to the project's folders; it is dirty.</summary>
    /// <param name="folder">The folder to add, relative to the workspace root.</param>
    public ProjectFile WithFolder(string folder) => this with { ProjectInfo = ProjectInfo.WithFolder(folder), IsDirty = true };
    /// <summary>The project file with the precompiler constant <paramref name="name"/> set to <paramref name="value"/>; it is dirty.</summary>
    /// <param name="name">The constant's name.</param>
    /// <param name="value">The source text of its value.</param>
    public ProjectFile WithPrecompilerConstant(string name, string value) => this with { ProjectInfo = ProjectInfo.WithPrecompilerConstant(name, value), IsDirty = true };

    /// <summary>The project file without <paramref name="reference"/>; it is dirty.</summary>
    /// <param name="reference">The reference to remove.</param>
    public ProjectFile WithoutReference(RDCoreReference reference) => this with { ProjectInfo = ProjectInfo.WithoutReference(reference), IsDirty = true };
    /// <summary>The project file without <paramref name="module"/>; it is dirty.</summary>
    /// <param name="module">The module to remove.</param>
    public ProjectFile WithoutModule(RDCoreModule module) => this with { ProjectInfo = ProjectInfo.WithoutModule(module), IsDirty = true };
    /// <summary>The project file without <paramref name="document"/>; it is dirty.</summary>
    /// <param name="document">The file to remove.</param>
    public ProjectFile WithoutDocument(RDCoreFile document) => this with { ProjectInfo = ProjectInfo.WithoutDocument(document), IsDirty = true };
    /// <summary>The project file without <paramref name="folder"/>; it is dirty.</summary>
    /// <param name="folder">The folder to remove.</param>
    public ProjectFile WithoutFolder(string folder) => this with { ProjectInfo = ProjectInfo.WithoutFolder(folder), IsDirty = true };
    /// <summary>The project file without the precompiler constant <paramref name="name"/>; it is dirty.</summary>
    /// <param name="name">The constant's name.</param>
    public ProjectFile WithoutPrecompilerConstant(string name) => this with { ProjectInfo = ProjectInfo.WithoutPrecompilerConstant(name), IsDirty = true };

    /// <inheritdoc/>
    public override int GetHashCode() => Uri.GetHashCode();
    /// <summary>Whether <paramref name="other"/> is the project file at the same workspace root (<see cref="Uri"/>).</summary>
    /// <param name="other">The project file to compare with.</param>
    public bool Equals(ProjectFile? other) => other?.Uri is string uri && Uri.Equals(uri);
}

/// <summary>
/// The workspace structure and project-level configuration declared by a <c>.rdproj</c>.
/// </summary>
public record class RDCoreProject
{
    /// <summary>
    /// The name of a project that is given none of its own: <c>VBAProject</c>, as MS-VBA names a new project.
    /// </summary>
    /// <remarks>
    /// A workspace whose project file names no project is named after its folder, or this when its folder's name is no identifier. The shell's own
    /// project is named this too, which leaves its one module, <c>Program</c>, no project of its own name (<strong>RD-VBAL §2.2.3</strong>).
    /// </remarks>
    public const string DefaultName = "VBAProject";

    /// <summary>
    /// The project's name: what a project-qualified reference to its members names.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The libraries and projects the project references, in their priority order (<strong>RD-VBAL §2.2.3.2</strong>).
    /// </summary>
    /// <remarks>
    /// The standard library (<c>VBA</c>) is none of them: every project has it, in every dialect (<strong>RD-VBAL §6.0</strong>), and a project file does not
    /// list it. A project file that does - with the path of Office's <c>VBE7.DLL</c>, as a VB6 project's references do - is read without it, and written
    /// without it (<see cref="RDCoreReference.IsStandardLibrary"/>).
    /// </remarks>
    public RDCoreReference[] References { get; init; } = [];

    /// <summary>
    /// The project's modules: its source files, by their paths relative to the workspace root (<strong>RD-VBAL §2.2.3.3</strong>).
    /// </summary>
    public RDCoreModule[] Modules { get; init; } = [];
    /// <summary>
    /// The project's files that are not modules, by their paths relative to the workspace root.
    /// </summary>
    public RDCoreFile[] OtherFiles { get; init; } = [];
    /// <summary>
    /// The project's folders, relative to the workspace root, including those no file is in.
    /// </summary>
    public string[] Folders { get; set; } = [];

    /// <summary>
    /// The Visual Basic 6 project (<c>.vbp</c>) the project was imported from, by its path relative to the folder of the <c>.rdproj</c>: the import writes it,
    /// a second import of the same <c>.vbp</c> keeps it, and nothing else changes it. <see langword="null"/> for a project that was not imported - a
    /// <c>.vbp</c> next to a <c>.rdproj</c> does not make it one.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceProject { get; init; }

    /// <summary>Whether the project was imported from a Visual Basic 6 project (<see cref="SourceProject"/>).</summary>
    [JsonIgnore]
    public bool IsImported => SourceProject is not null;

    /// <summary>
    /// Project-level precompiler constants (<c>#Const</c>), keyed by name. The value is the literal
    /// source text of the constant expression (e.g. <c>"1"</c>, <c>"-1"</c>, <c>"\"debug\""</c>).
    /// CLI <c>--define</c> arguments override these.
    /// </summary>
    public Dictionary<string, string> PrecompilerConstants { get; init; } = [];

    /// <summary>
    /// The members of the project this model does not know, as they were read, which are written back (<see cref="ProjectFile.ExtensionData"/>).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>
    /// The full paths of the project's modules, other files and folders, under <paramref name="srcRoot"/>.
    /// </summary>
    /// <param name="srcRoot">The workspace root the project's paths are relative to.</param>
    public HashSet<string> GetWorkspaceFolders(string srcRoot) =>
        [
            .. Modules.Select(module => Path.Combine(srcRoot, module.RelativeUri)),
            .. OtherFiles.Select(file => Path.Combine(srcRoot, file.RelativeUri)),
            .. Folders.Select(folder => Path.Combine(srcRoot, folder)),
        ];

    /// <summary>
    /// The project's modules and other files in <paramref name="folder"/> or under it.
    /// </summary>
    /// <param name="folder">A folder relative to the workspace root, with <c>/</c> separators.</param>
    public IEnumerable<RDCoreFile> GetFolderFiles(string folder) =>
        [
            .. Modules.Where(m => Path.GetDirectoryName(m.RelativeUri)!.Replace('\\', '/').StartsWith(folder)),
            .. OtherFiles.Where(f => Path.GetDirectoryName(f.RelativeUri)!.Replace('\\', '/').StartsWith(folder)),
        ];

    /// <summary>The project under another name.</summary>
    /// <param name="name">The project's name.</param>
    public RDCoreProject WithName(string name) => this with { Name = name };
    /// <summary>The project with <paramref name="reference"/> added last to its references, at the lowest priority.</summary>
    /// <param name="reference">The reference to add.</param>
    public RDCoreProject WithReference(RDCoreReference reference) => this with { References = [.. References, reference] };
    /// <summary>The project with <paramref name="module"/> added to its modules.</summary>
    /// <param name="module">The module to add.</param>
    public RDCoreProject WithModule(RDCoreModule module) => this with { Modules = [.. Modules, module] };
    /// <summary>The project with <paramref name="document"/> added to its other files.</summary>
    /// <param name="document">The file to add.</param>
    public RDCoreProject WithDocument(RDCoreFile document) => this with { OtherFiles = [.. OtherFiles, document] };
    /// <summary>The project with <paramref name="folder"/> added to its folders.</summary>
    /// <param name="folder">The folder to add, relative to the workspace root.</param>
    public RDCoreProject WithFolder(string folder) => this with { Folders = [.. Folders, folder] };
    /// <summary>The project with the precompiler constant <paramref name="name"/> set to <paramref name="value"/>, whatever the case of its name.</summary>
    /// <param name="name">The constant's name.</param>
    /// <param name="value">The source text of its value.</param>
    public RDCoreProject WithPrecompilerConstant(string name, string value)
        => this with { PrecompilerConstants = new(PrecompilerConstants, StringComparer.OrdinalIgnoreCase) { [name] = value } };

    /// <summary>The project without <paramref name="reference"/>.</summary>
    /// <param name="reference">The reference to remove.</param>
    public RDCoreProject WithoutReference(RDCoreReference reference) => this with { References = [.. References.Where(r => r != reference)] };
    /// <summary>The project without <paramref name="module"/>.</summary>
    /// <param name="module">The module to remove.</param>
    public RDCoreProject WithoutModule(RDCoreModule module) => this with { Modules = [.. Modules.Where(m => m != module)] };
    /// <summary>The project without <paramref name="document"/>.</summary>
    /// <param name="document">The file to remove.</param>
    public RDCoreProject WithoutDocument(RDCoreFile document) => this with { OtherFiles = [.. OtherFiles.Where(d => d != document)] };
    /// <summary>The project without <paramref name="folder"/>.</summary>
    /// <param name="folder">The folder to remove.</param>
    public RDCoreProject WithoutFolder(string folder) => this with { Folders = [.. Folders.Where(f => f != folder)] };
    /// <summary>The project without the precompiler constant <paramref name="name"/>, whatever the case of its name.</summary>
    /// <param name="name">The constant's name.</param>
    public RDCoreProject WithoutPrecompilerConstant(string name)
        => this with { PrecompilerConstants = new(PrecompilerConstants.Where(e => !string.Equals(e.Key, name, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase) };
}

/// <summary>
/// A library or project a project references (<strong>RD-VBAL §2.2.3.2</strong>).
/// </summary>
public sealed record class RDCoreReference : IEquatable<RDCoreReference>
{
    /// <summary>
    /// The name of the standard library, which every project has (<strong>RD-VBAL §6.0</strong>).
    /// </summary>
    public const string StandardLibraryName = "VBA";

    /// <summary>
    /// The identifier of the type library of <c>VBA</c>, the standard library, by which a VB6 project references it.
    /// </summary>
    public static readonly Guid StandardLibraryGuid = new("000204ef-0000-0000-c000-000000000046");

    /// <summary>
    /// Whether the reference is to the standard library (<c>VBA</c>), by its name or the identifier of its type library: the platform's own library, which
    /// every project has and no project file lists (<see cref="RDCoreProject.References"/>).
    /// </summary>
    [JsonIgnore]
    public bool IsStandardLibrary
        => string.Equals(Name, StandardLibraryName, StringComparison.OrdinalIgnoreCase) || Guid == StandardLibraryGuid;

    /// <summary>
    /// The referenced library's or project's name, which identifies the reference.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// Where the referenced library is, when the reference has a file; <see langword="null"/> otherwise.
    /// </summary>
    public string? AbsolutePath { get; init; }
    /// <summary>
    /// The identifier of the referenced type library, for a reference to one; <see langword="null"/> otherwise.
    /// </summary>
    public Guid? Guid { get; init; }
    /// <summary>
    /// The major version of the referenced type library; <see langword="null"/> when the reference does not say.
    /// </summary>
    public int? Major { get; init; }
    /// <summary>
    /// The minor version of the referenced type library; <see langword="null"/> when the reference does not say.
    /// </summary>
    public int? Minor { get; init; }
    /// <summary>
    /// Where the description of the referenced library's members is, when it is a file of its own; <see langword="null"/> otherwise.
    /// </summary>
    public string? TypeLibInfoPath { get; init; }

    /// <summary>
    /// Whether the reference cannot be removed from the project.
    /// </summary>
    public bool IsUnremovable { get; init; } = false;

    /// <summary>
    /// The members of the reference this model does not know, as they were read, which are written back (<see cref="ProjectFile.ExtensionData"/>).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Name, Guid, Major, Minor);

    /// <summary>Whether <paramref name="other"/> references the library or project of the same name.</summary>
    /// <param name="other">The reference to compare with.</param>
    public bool Equals(RDCoreReference? other) => other?.Name is string name && Name.Equals(name);
}

/// <summary>
/// A file of a project that is not a module: a <c>README.md</c>, a licence, a picture (<see cref="RDCoreProject.OtherFiles"/>).
/// </summary>
/// <remarks>
/// A project file writes it as its path alone, <c>"README.md"</c>, so that a <c>.rdproj</c> reads as the list of its files (<see cref="RDCoreFileJsonConverter"/>);
/// an object with a <c>RelativeUri</c> is read the same, and is written as an object when it has members this model does not know (<see cref="ExtensionData"/>).
/// </remarks>
[JsonConverter(typeof(RDCoreFileJsonConverter))]
public record class RDCoreFile : IEquatable<RDCoreFile>
{
    /// <summary>
    /// The file's path, relative to the workspace root, which identifies the file.
    /// </summary>
    public string RelativeUri { get; init; } = string.Empty;

    /// <summary>
    /// The members of the file's entry this model does not know, as they were read, which are written back (<see cref="ProjectFile.ExtensionData"/>).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>
    /// The file's extension with its leading period, <c>".md"</c> for <c>"docs/README.md"</c>; an empty string when the file's name has none.
    /// </summary>
    [JsonIgnore]
    public string Extension => Path.GetExtension(RelativeUri);

    /// <summary>
    /// The name of a module in this file whose source declares none (<see cref="ModuleName.FromFileName"/>).
    /// </summary>
    [JsonIgnore]
    public string DefaultName => ModuleName.FromFileName(RelativeUri);

    /// <inheritdoc/>
    public override int GetHashCode() => RelativeUri.GetHashCode();

    /// <summary>Whether <paramref name="other"/> is the file at the same path.</summary>
    /// <param name="other">The file to compare with.</param>
    public virtual bool Equals(RDCoreFile? other) => other?.RelativeUri is string uri && RelativeUri.Equals(uri);
}

/// <summary>
/// What a host's document is, whose code a document module is (<strong>MS-VBAL §4.2.1</strong>): the class the host has for it, whose members and events the
/// module's object has.
/// </summary>
/// <remarks>
/// A project file says it by name (<c>"ExcelWorksheet"</c>), and a number (<c>2</c>) is read the same. A name or a number
/// this does not have is <see cref="Unknown"/>: the project still loads, and the module is one RD-VBA does not support.
/// </remarks>
[JsonConverter(typeof(DocClassTypeJsonConverter))]
public enum DocClassType
{
    /// <summary>A document of a host RD-VBA does not know.</summary>
    Unknown = 0,
    /// <summary>An Excel workbook (<c>Excel.Workbook</c>): <c>ThisWorkbook</c>.</summary>
    ExcelWorkbook = 1,
    /// <summary>An Excel worksheet (<c>Excel.Worksheet</c>): <c>Sheet1</c>.</summary>
    ExcelWorksheet = 2,
    /// <summary>An Access form (<c>Access.Form</c>).</summary>
    AccessForm = 3,
    /// <summary>An Access report (<c>Access.Report</c>).</summary>
    AccessReport = 4,
    /// <summary>A Word document (<c>Word.Document</c>): <c>ThisDocument</c>.</summary>
    WordDocument = 5,
}

/// <summary>
/// Reads a <see cref="DocClassType"/> by its name or its number, and writes it by its name; a name or a number it does not have reads as
/// <see cref="DocClassType.Unknown"/>.
/// </summary>
public sealed class DocClassTypeJsonConverter : JsonConverter<DocClassType>
{
    /// <inheritdoc/>
    public override DocClassType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.TryGetInt32(out var number) && Enum.IsDefined((DocClassType)number) ? (DocClassType)number : DocClassType.Unknown;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            // one of the names, and nothing else: Enum.TryParse would take "2" for the number, and "ExcelWorkbook, ExcelWorksheet" for the two OR'ed together.
            var name = reader.GetString();
            return Enum.GetValues<DocClassType>().FirstOrDefault(value => string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase));
        }

        throw new JsonException($"A {nameof(DocClassType)} is a name or a number, not a {reader.TokenType}.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DocClassType value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

/// <summary>
/// A module of a project: a source file, and what the project says of it that its text does not.
/// </summary>
/// <remarks>
/// A project file writes a standard or class module as its path alone, <c>"Module1.bas"</c>, and a document module as an object that also says its document:
/// <c>{ "RelativeUri": "Sheet1.doccls", "Super": "ExcelWorksheet" }</c> (<see cref="RDCoreModuleJsonConverter"/>). Either form is read, and an object whose
/// <c>Super</c> is <c>null</c> or absent is a standard or class module.
/// </remarks>
[JsonConverter(typeof(RDCoreModuleJsonConverter))]
public record class RDCoreModule : RDCoreFile
{
    /// <summary>
    /// What host document the module is the code of, which makes it a document module; <see langword="null"/> for a standard or class module, whose own
    /// text says which it is.
    /// </summary>
    public DocClassType? Super { get; init; }
}

/// <summary>
/// Reads an <see cref="RDCoreFile"/> from its path, or from an object with a <c>RelativeUri</c>; writes it as its path.
/// </summary>
public sealed class RDCoreFileJsonConverter : JsonConverter<RDCoreFile>
{
    /// <inheritdoc/>
    public override RDCoreFile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new() { RelativeUri = ProjectFileEntry.Read(ref reader, options, out _, out var extensionData), ExtensionData = extensionData };

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RDCoreFile value, JsonSerializerOptions options)
    {
        if (value.ExtensionData is not { Count: > 0 } extensionData)
        {
            writer.WriteStringValue(value.RelativeUri);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString(nameof(RDCoreFile.RelativeUri), value.RelativeUri);
        ProjectFileEntry.WriteExtensionData(writer, extensionData);
        writer.WriteEndObject();
    }
}

/// <summary>
/// Reads an <see cref="RDCoreModule"/> from its path, or from an object with a <c>RelativeUri</c> and a <c>Super</c>; writes it as its path unless it is a
/// document module.
/// </summary>
public sealed class RDCoreModuleJsonConverter : JsonConverter<RDCoreModule>
{
    /// <inheritdoc/>
    public override RDCoreModule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var relativeUri = ProjectFileEntry.Read(ref reader, options, out var super, out var extensionData);
        return new() { RelativeUri = relativeUri, Super = super, ExtensionData = extensionData };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RDCoreModule value, JsonSerializerOptions options)
    {
        if (value.Super is null && value.ExtensionData is not { Count: > 0 })
        {
            writer.WriteStringValue(value.RelativeUri);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString(nameof(RDCoreFile.RelativeUri), value.RelativeUri);
        if (value.Super is { } super)
        {
            writer.WritePropertyName(nameof(RDCoreModule.Super));
            JsonSerializer.Serialize(writer, super, options);
        }

        if (value.ExtensionData is { } extensionData)
        {
            ProjectFileEntry.WriteExtensionData(writer, extensionData);
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// Reads a project file's <see cref="ProjectFile.Version"/> whatever it is: a string as itself, a number as its text, and anything else as its JSON.
/// </summary>
public sealed class ProjectFileVersionJsonConverter : JsonConverter<string>
{
    /// <inheritdoc/>
    public override bool HandleNull => false;

    /// <inheritdoc/>
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString()!,
        JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.HasValueSequence ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence) : reader.ValueSpan),
        _ => JsonElement.ParseValue(ref reader).GetRawText(),
    };

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

// an entry of a project file's list of files: a path, or an object that names it (and, for a module, its document).
internal static class ProjectFileEntry
{
    public static string Read(ref Utf8JsonReader reader, JsonSerializerOptions options, out DocClassType? super, out IReadOnlyDictionary<string, JsonElement>? extensionData)
    {
        super = null;
        extensionData = null;
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString()!;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"A file of a project is its path or an object, not a {reader.TokenType}.");
        }

        var relativeUri = string.Empty;
        Dictionary<string, JsonElement>? unknown = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            if (string.Equals(name, nameof(RDCoreFile.RelativeUri), StringComparison.OrdinalIgnoreCase))
            {
                relativeUri = reader.GetString() ?? string.Empty;
            }
            else if (string.Equals(name, nameof(RDCoreModule.Super), StringComparison.OrdinalIgnoreCase))
            {
                super = JsonSerializer.Deserialize<DocClassType?>(ref reader, options);
            }
            else
            {
                // a member this model has no use for, which a newer platform may have written: it is kept, and written back.
                (unknown ??= [])[name!] = JsonElement.ParseValue(ref reader);
            }
        }

        extensionData = unknown;
        return relativeUri;
    }

    public static void WriteExtensionData(Utf8JsonWriter writer, IReadOnlyDictionary<string, JsonElement> extensionData)
    {
        foreach (var (name, value) in extensionData)
        {
            writer.WritePropertyName(name);
            value.WriteTo(writer);
        }
    }
}

/// <summary>
/// How the format a project file says it is compares with the one the platform reads and writes (<see cref="ProjectFile.SchemaOf"/>).
/// </summary>
/// <remarks>
/// A project file is read whatever it says: one of an older format or the same one loads, and so does one of a newer minor, whose members this platform does
/// not know are kept. One of a newer major, or whose version cannot be read, is read all the same, and loads if it can be.
/// </remarks>
public enum ProjectFileSchemaMatch
{
    /// <summary>The project file says no version: no platform wrote it yet.</summary>
    Unstamped,
    /// <summary>The project file is in the format the platform reads and writes.</summary>
    Same,
    /// <summary>The project file is in an older format, or was written before the format had a version; it is in this one once it is written.</summary>
    Older,
    /// <summary>The project file is in a newer minor of the format: a newer platform added members this one does not know, and keeps.</summary>
    NewerMinor,
    /// <summary>The project file is in a newer major of the format, which this platform may not read.</summary>
    NewerMajor,
    /// <summary>The project file's version is no version: neither <c>major.minor</c> nor a number.</summary>
    Unparseable,
}
