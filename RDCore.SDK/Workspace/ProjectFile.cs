using RDCore.SDK.Extensibility;
using RDCore.SDK.Server;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Workspace;

/// <summary>
/// The in-memory model of a workspace's <c>.rdproj</c> project file.
/// </summary>
public sealed record class ProjectFile : IEquatable<ProjectFile>
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
        Version = CurrentVersion;
        IsDirty = false;
        Configuration = [];
    }

    // a copy is of the same project file: made by the same version - only a write stamps the version of the platform that writes it (WithCurrentVersion) - and
    // as synthesized as it was, until it is written.
    public ProjectFile(ProjectFile source)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        Uri = source.Uri;
        ProjectInfo = source.ProjectInfo;
        Version = source.Version;
        IsSynthesized = source.IsSynthesized;
        // deliberate: a copy is a new, clean value, and a change says it is dirty (the With* methods, ProjectFileService.Update).
        IsDirty = false;
        Configuration = source.Configuration;
    }

    /// <summary>
    /// The version of the running platform, which a project file it writes is stamped with (<see cref="WithCurrentVersion"/>).
    /// </summary>
    public static string CurrentVersion => AppHost<RDCoreServerApp>.Info.Version!.ToString(3);

    /// <summary>
    /// The project file as the running platform writes it: stamped with its version (<see cref="CurrentVersion"/>). A project file that is read, or copied,
    /// keeps the version that wrote it, so that a platform can refuse a project a later version wrote.
    /// </summary>
    public ProjectFile WithCurrentVersion() => this with { Version = CurrentVersion };

    /// <summary>
    /// <c>true</c> if the project file has unsaved changes.
    /// </summary>
    [JsonIgnore]
    public bool IsDirty { get; init; }

    /// <summary>
    /// <c>true</c> for a project the platform made up for a folder that has no <c>.rdproj</c>, from the source files under it
    /// (<see cref="IProjectFileLoader.LoadOrSynthesizeAsync"/>), and that was not written yet.
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
    /// The <c>RDCore.ServerApp</c> version that last wrote this project file: kept as it is read and copied, and stamped when it is written
    /// (<see cref="WithCurrentVersion"/>).
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
    public ProjectFile WithPrecompilerConstant(string name, string value) => this with { ProjectInfo = ProjectInfo.WithPrecompilerConstant(name, value), IsDirty = true };

    public ProjectFile WithoutReference(RDCoreReference reference) => this with { ProjectInfo = ProjectInfo.WithoutReference(reference), IsDirty = true };
    public ProjectFile WithoutModule(RDCoreModule module) => this with { ProjectInfo = ProjectInfo.WithoutModule(module), IsDirty = true };
    public ProjectFile WithoutDocument(RDCoreFile document) => this with { ProjectInfo = ProjectInfo.WithoutDocument(document), IsDirty = true };
    public ProjectFile WithoutFolder(string folder) => this with { ProjectInfo = ProjectInfo.WithoutFolder(folder), IsDirty = true };
    public ProjectFile WithoutPrecompilerConstant(string name) => this with { ProjectInfo = ProjectInfo.WithoutPrecompilerConstant(name), IsDirty = true };

    public override int GetHashCode() => Uri.GetHashCode();
    public bool Equals(ProjectFile? other) => other?.Uri is string uri && Uri.Equals(uri);
}

/// <summary>
/// The workspace structure and project-level configuration declared by a <c>.rdproj</c>.
/// </summary>
public record class RDCoreProject
{
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The libraries and projects the project references, in their priority order (<strong>RD-VBAL §2.2.3.2</strong>).
    /// </summary>
    /// <remarks>
    /// The standard library (<c>VBA</c>) is none of them: every project has it, in every dialect (<strong>RD-VBAL §6.0</strong>), and a project file does not
    /// list it. A project file that does - with the path of Office's <c>VBE7.DLL</c>, as a VB6 project's references do - is read without it
    /// (<see cref="ProjectFileLoader"/>), and written without it (<see cref="RDCoreReference.IsStandardLibrary"/>).
    /// </remarks>
    public RDCoreReference[] References { get; init; } = [];

    public RDCoreModule[] Modules { get; init; } = [];
    public RDCoreFile[] OtherFiles { get; init; } = [];
    public string[] Folders { get; set; } = [];

    /// <summary>
    /// The Visual Basic 6 project (<c>.vbp</c>) the project was imported from, by its path relative to the folder of the <c>.rdproj</c>: the import writes it
    /// (<see cref="VbpImport.Into"/>), a second import of the same <c>.vbp</c> keeps it, and nothing else changes it. <see langword="null"/> for a project that was not imported - a
    /// <c>.vbp</c> next to a <c>.rdproj</c> does not make it one.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceProject { get; init; }

    /// <summary>Whether the project was imported from a Visual Basic 6 project (<see cref="SourceProject"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsImported => SourceProject is not null;

    /// <summary>
    /// Project-level precompiler constants (<c>#Const</c>), keyed by name. The value is the literal
    /// source text of the constant expression (e.g. <c>"1"</c>, <c>"-1"</c>, <c>"\"debug\""</c>).
    /// CLI <c>--define</c> arguments override these.
    /// </summary>
    public Dictionary<string, string> PrecompilerConstants { get; init; } = [];

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
    public RDCoreProject WithPrecompilerConstant(string name, string value)
        => this with { PrecompilerConstants = new(PrecompilerConstants, StringComparer.OrdinalIgnoreCase) { [name] = value } };

    public RDCoreProject WithoutReference(RDCoreReference reference) => this with { References = [.. References.Where(r => r != reference)] };
    public RDCoreProject WithoutModule(RDCoreModule module) => this with { Modules = [.. Modules.Where(m => m != module)] };
    public RDCoreProject WithoutDocument(RDCoreFile document) => this with { OtherFiles = [.. OtherFiles.Where(d => d != document)] };
    public RDCoreProject WithoutFolder(string folder) => this with { Folders = [.. Folders.Where(f => f != folder)] };
    public RDCoreProject WithoutPrecompilerConstant(string name)
        => this with { PrecompilerConstants = new(PrecompilerConstants.Where(e => !string.Equals(e.Key, name, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase) };
}

public sealed record class RDCoreReference : IEquatable<RDCoreReference>
{
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
        => string.Equals(Name, Runtime.StdLib.StdLibSymbolProvider.LibraryName, StringComparison.OrdinalIgnoreCase) || Guid == StandardLibraryGuid;

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

/// <summary>
/// A file of a project that is not a module: a <c>README.md</c>, a licence, a picture (<see cref="RDCoreProject.OtherFiles"/>).
/// </summary>
/// <remarks>
/// A project file writes it as its path alone, <c>"README.md"</c>, so that a <c>.rdproj</c> reads as the list of its files (<see cref="RDCoreFileJsonConverter"/>);
/// an object with a <c>RelativeUri</c> is read the same.
/// </remarks>
[JsonConverter(typeof(RDCoreFileJsonConverter))]
public record class RDCoreFile : IEquatable<RDCoreFile>
{
    public string RelativeUri { get; init; } = string.Empty;

    /// <summary>
    /// The file's extension with its leading period, <c>".md"</c> for <c>"docs/README.md"</c>; an empty string when the file's name has none.
    /// </summary>
    [JsonIgnore]
    public string Extension => Path.GetExtension(RelativeUri);

    [JsonIgnore]
    public string DefaultName => ModuleName.FromFileName(RelativeUri);

    public override int GetHashCode() => RelativeUri.GetHashCode();

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
    /// What host document the module is the code of, which makes it a document module (<see cref="ModuleKinds.Of(RDCoreModule?, string?)"/>); <see langword="null"/>
    /// for a standard or class module.
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
        => new() { RelativeUri = ProjectFileEntry.Read(ref reader, options, out _) };

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RDCoreFile value, JsonSerializerOptions options) => writer.WriteStringValue(value.RelativeUri);
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
        var relativeUri = ProjectFileEntry.Read(ref reader, options, out var super);
        return new() { RelativeUri = relativeUri, Super = super };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RDCoreModule value, JsonSerializerOptions options)
    {
        if (value.Super is not { } super)
        {
            writer.WriteStringValue(value.RelativeUri);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString(nameof(RDCoreFile.RelativeUri), value.RelativeUri);
        writer.WritePropertyName(nameof(RDCoreModule.Super));
        JsonSerializer.Serialize(writer, super, options);
        writer.WriteEndObject();
    }
}

// an entry of a project file's list of files: a path, or an object that names it (and, for a module, its document).
internal static class ProjectFileEntry
{
    public static string Read(ref Utf8JsonReader reader, JsonSerializerOptions options, out DocClassType? super)
    {
        super = null;
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString()!;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"A file of a project is its path or an object, not a {reader.TokenType}.");
        }

        var relativeUri = string.Empty;
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
                // a member this model has no use for.
                reader.Skip();
            }
        }

        return relativeUri;
    }
}
