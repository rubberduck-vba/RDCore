using Microsoft.Extensions.Configuration;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDCore.External.Settings;

/// <summary>
/// The workspace application settings, in the <c>.json</c> files of a workspace (<strong>RD-VBAL §2.1.1.1</strong>).
/// </summary>
/// <remarks>
/// <para>
/// A setting is read the way the platform reads its own <c>appsettings.json</c>: through an <c>IConfiguration</c> built over the file, so that a path is its sections
/// separated by <c>:</c> and compared without regard to case, an array's elements are its sections <c>0</c>, <c>1</c>, ..., and a number or a Boolean reads as its
/// text. The file is read again for each call: what another process wrote to it in the meantime is what is read.
/// </para>
/// <para>
/// A setting is written into the file as a JSON string, under the sections of its path - each one created when it is not there, and found without regard to case when
/// it is. A file that is written to is written whole, indented, and without the comments it had: JSON has none, and a configuration file only tolerates them.
/// </para>
/// <para>
/// A configuration file is one of the workspace's: a <c>.json</c> file under its root. A name that leaves the workspace is refused, so that the settings of one
/// program cannot be a way to read or write any file of the machine.
/// </para>
/// </remarks>
/// <param name="fileSystem">The file system the workspace is on.</param>
/// <param name="workspaceRoot">The root directory of the workspace.</param>
/// <param name="defaultFile">The configuration file a call that names none uses, relative to the workspace.</param>
public sealed class JsonWorkspaceSettings(IFileSystem fileSystem, string workspaceRoot, string defaultFile = JsonWorkspaceSettings.DefaultFileName) : IWorkspaceSettings
{
    /// <summary>
    /// The configuration file of a workspace that is not told otherwise: the platform's own default (<strong>RD-VBAL §2.1.1.1</strong>).
    /// </summary>
    public const string DefaultFileName = "appsettings.json";

    private const char PathSeparator = ':';

    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly Lock _sync = new();

    /// <summary>
    /// The absolute path of a configuration file of the workspace.
    /// </summary>
    /// <param name="file">The file, relative to the workspace; <see langword="null"/> or empty for the default one.</param>
    /// <exception cref="ArgumentException">The file is not a <c>.json</c> file under the root of the workspace.</exception>
    public string PathOf(string? file)
    {
        var name = string.IsNullOrWhiteSpace(file) ? defaultFile : file;
        if (!string.Equals(fileSystem.Path.GetExtension(name), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{name}' is not a .json configuration file.", nameof(file));
        }

        var root = fileSystem.Path.GetFullPath(workspaceRoot);
        var full = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(root, name));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root.EndsWith(fileSystem.Path.DirectorySeparatorChar) ? root : root + fileSystem.Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, comparison))
        {
            throw new ArgumentException($"'{name}' is not a file of the workspace.", nameof(file));
        }

        return full;
    }

    /// <inheritdoc/>
    public string? Get(string path, string? file = null)
    {
        var configuration = Read(PathOf(file));
        return configuration.GetSection(path) is { Value: { } value } ? value : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<KeyValuePair<string, string>>? GetAll(string path, string? file = null)
    {
        var section = Read(PathOf(file)).GetSection(path);
        if (!section.Exists())
        {
            return null;
        }

        return [.. section.GetChildren().Where(child => child.Value is not null).Select(child => KeyValuePair.Create(child.Key, child.Value!))];
    }

    /// <inheritdoc/>
    public void Save(string path, string value, string? file = null)
    {
        var segments = Segments(path);
        if (segments.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException($"'{path}' is not a configuration path: a section of it has no name.", nameof(path));
        }

        var location = PathOf(file);
        lock (_sync)
        {
            var root = Load(location) ?? new JsonObject();
            var node = root;
            foreach (var segment in segments[..^1])
            {
                node = Child(node, segment, create: true)
                    ?? throw new InvalidOperationException($"'{path}': '{segment}' is a value, and a setting cannot be saved under it.");
            }

            switch (node)
            {
                case JsonObject section:
                    section[NameIn(section, segments[^1])] = JsonValue.Create(value);
                    break;
                case JsonArray array when TryIndex(array, segments[^1], out var index):
                    array[index] = JsonValue.Create(value);
                    break;
                default:
                    throw new InvalidOperationException($"'{path}': the setting cannot be saved in what its section is.");
            }

            Write(location, root);
        }
    }

    /// <inheritdoc/>
    public bool Delete(string path, string? file = null)
    {
        var segments = Segments(path);
        var location = PathOf(file);
        lock (_sync)
        {
            if (segments.Any(string.IsNullOrEmpty) || Load(location) is not { } root)
            {
                return false;
            }

            var node = root;
            foreach (var segment in segments[..^1])
            {
                if (Child(node, segment, create: false) is not { } child)
                {
                    return false;
                }

                node = child;
            }

            switch (node)
            {
                case JsonObject section when Existing(section, segments[^1]) is { } name:
                    section.Remove(name);
                    break;
                case JsonArray array when TryIndex(array, segments[^1], out var index):
                    array.RemoveAt(index);
                    break;
                default:
                    return false;
            }

            Write(location, root);
            return true;
        }
    }

    private static string[] Segments(string path) => path.Split(PathSeparator, StringSplitOptions.TrimEntries);

    // a file that is not there, or has nothing in it, has no settings - which is not an error to read, and is where a first setting is saved.
    private IConfiguration Read(string location)
    {
        var builder = new ConfigurationBuilder();
        if (ReadText(location) is { } text)
        {
            builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        }

        try
        {
            return builder.Build();
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw new FormatException($"'{fileSystem.Path.GetFileName(location)}' is not a JSON configuration file: {exception.Message}", exception);
        }
    }

    private JsonNode? Load(string location)
    {
        if (ReadText(location) is not { } text)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
                ?? throw new FormatException($"'{fileSystem.Path.GetFileName(location)}' is not a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new FormatException($"'{fileSystem.Path.GetFileName(location)}' is not a JSON configuration file: {exception.Message}", exception);
        }
    }

    private string? ReadText(string location)
        => fileSystem.File.Exists(location) && fileSystem.File.ReadAllText(location) is { } text && !string.IsNullOrWhiteSpace(text) ? text : null;

    private void Write(string location, JsonNode root)
    {
        if (fileSystem.Path.GetDirectoryName(location) is { Length: > 0 } directory)
        {
            fileSystem.Directory.CreateDirectory(directory);
        }

        fileSystem.File.WriteAllText(location, root.ToJsonString(WriteOptions));
    }

    // the section a segment names in an object (found without regard to case, created when asked) or an array (by its index); null when it is a value.
    private static JsonNode? Child(JsonNode node, string segment, bool create)
    {
        switch (node)
        {
            case JsonObject section:
                if (Existing(section, segment) is { } name)
                {
                    return section[name] is JsonObject or JsonArray ? section[name] : null;
                }

                if (!create)
                {
                    return null;
                }

                var created = new JsonObject();
                section[segment] = created;
                return created;

            case JsonArray array when TryIndex(array, segment, out var index):
                return array[index] is JsonObject or JsonArray ? array[index] : null;

            default:
                return null;
        }
    }

    private static string? Existing(JsonObject section, string segment)
        => section.Select(property => property.Key).FirstOrDefault(key => string.Equals(key, segment, StringComparison.OrdinalIgnoreCase));

    private static string NameIn(JsonObject section, string segment) => Existing(section, segment) ?? segment;

    private static bool TryIndex(JsonArray array, string segment, out int index)
        => int.TryParse(segment, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index) && index < array.Count;
}

/// <summary>
/// The settings of the legacy members, in the workspace application settings: application <c>appName</c>'s key <c>key</c> of section <c>section</c> is the
/// setting <c>ProgramSettings:appName:section:key</c> of the workspace's default configuration file.
/// </summary>
/// <remarks>
/// <c>ProgramSettings</c> echoes the registry key MS-VBA keeps them under (<c>VB and VBA Program Settings</c>), so that <c>GetSetting("App", "Startup", "Top")</c>
/// and <c>GetJsonSetting("ProgramSettings:App:Startup:Top")</c> read the same thing.
/// </remarks>
/// <param name="settings">The workspace application settings.</param>
public sealed class JsonProgramSettings(IWorkspaceSettings settings) : IProgramSettings
{
    /// <summary>
    /// The section of the workspace application settings the legacy members keep their settings under.
    /// </summary>
    public const string RootSection = "ProgramSettings";

    /// <inheritdoc/>
    public string? Get(string appName, string section, string key) => settings.Get(PathOf(appName, section, key));

    /// <inheritdoc/>
    public IReadOnlyList<KeyValuePair<string, string>>? GetAll(string appName, string section) => settings.GetAll(PathOf(appName, section));

    /// <inheritdoc/>
    public void Save(string appName, string section, string key, string value) => settings.Save(PathOf(appName, section, key), value);

    /// <inheritdoc/>
    public bool Delete(string appName, string? section = null, string? key = null)
        => settings.Delete(section is null ? PathOf(appName) : key is null ? PathOf(appName, section) : PathOf(appName, section, key));

    // a name with a ':' in it would be two sections of the path, and a name with nothing in it none at all.
    private static string PathOf(params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains(':'))
            {
                throw new ArgumentException($"'{name}' is not the name of an application, a section or a key: it is empty, or has a ':' in it.");
            }
        }

        return string.Join(':', [RootSection, .. names]);
    }
}
