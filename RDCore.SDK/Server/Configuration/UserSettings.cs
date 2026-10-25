using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using RDCore.SDK.Platform;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDCore.SDK.Server.Configuration;

/// <summary>
/// Why a value was not set (<see cref="UserSettingsStore.Set"/>).
/// </summary>
public enum UserSettingProblem
{
    /// <summary>The value was set.</summary>
    None = 0,

    /// <summary>No setting has the key.</summary>
    UnknownSetting = 1,

    /// <summary>Only the deployed configuration sets the setting (<see cref="SettingReach.Deployment"/>).</summary>
    NotSettable = 2,

    /// <summary>The value is not one the setting can be (<see cref="SettingsSchema.TryParse"/>).</summary>
    InvalidValue = 3,

    /// <summary>The file of the user's settings is there, and is not one that can be read: it is not written over (<see cref="UserSettingsStore.Reset"/>).</summary>
    Unreadable = 4,
}

/// <summary>
/// What became of a value a user set.
/// </summary>
/// <param name="Problem">Why it was not set; <see cref="UserSettingProblem.None"/> when it was.</param>
/// <param name="Setting">The setting the key is of, when there is one.</param>
public sealed record class UserSettingChange(UserSettingProblem Problem, SettingDescriptor? Setting = null)
{
    /// <summary>Whether the value was set.</summary>
    public bool IsSet => Problem == UserSettingProblem.None;
}

/// <summary>
/// A value a user set for a setting.
/// </summary>
/// <param name="Setting">The setting.</param>
/// <param name="Value">The value, as it is written: <c>False</c>, <c>30</c>, <c>i, j, k</c>.</param>
public sealed record class UserSettingValue(SettingDescriptor Setting, string Value);

/// <summary>
/// The settings a user set for themselves, which take the place of what the deployed configuration says: the file <c>user.settings.json</c>, in the folder of
/// the platform's user (<see cref="IPlatformEnvironment.UserDataDirectory"/>).
/// </summary>
/// <remarks>
/// <para>
/// The file has the shape of an <c>appsettings.json</c> (<c>{ "Configuration": { "Server": { "Verbose": false } } }</c>), and holds only what the user set: a
/// deployed <c>appsettings.json</c> is never written to, so that what it says is always there to go back to, and what the user set outlives a platform that is
/// deployed again. Going back to the deployed configuration is removing what the user set (<see cref="Reset"/>).
/// </para>
/// <para>
/// A value is set only for a setting of the schema that a user can set, and only when it is one the setting can be: a file this writes never keeps the platform
/// from starting. What is read from the file is checked again when the platform starts (<see cref="UserSettingsConfiguration.AddUserSettings"/>), for a file
/// that was written by hand.
/// </para>
/// </remarks>
/// <param name="fileSystem">The file system the file is in.</param>
/// <param name="path">The path of the file.</param>
/// <param name="schema">The settings that can be set.</param>
public sealed class UserSettingsStore(IFileSystem fileSystem, string path, SettingsSchema schema)
{
    /// <summary>
    /// The name of the file.
    /// </summary>
    public const string FileName = "user.settings.json";

    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// The path of the file of the user's settings, in the folder of the platform's user.
    /// </summary>
    /// <param name="environment">The platform's environment.</param>
    public static string PathIn(IPlatformEnvironment environment) => Path.Combine(environment.UserDataDirectory, FileName);

    /// <summary>
    /// The path of the file.
    /// </summary>
    public string FilePath => path;

    /// <summary>
    /// Whether the file can be read: it is not there, or it is a JSON object. A file that cannot be read is not written over, and the platform starts as if it were
    /// not there; resetting every setting (<see cref="Reset"/>) deletes it.
    /// </summary>
    public bool IsReadable => TryLoad(out _);

    /// <summary>
    /// The values the user set, of the settings of the schema that a user can set, in the order of the schema.
    /// </summary>
    public IReadOnlyList<UserSettingValue> Read()
    {
        if (!TryLoad(out var root) || root is null)
        {
            return [];
        }

        return [.. schema.Settings
            .Where(setting => setting.IsSettable)
            .Select(setting => (Setting: setting, Node: Find(root, setting)))
            .Where(found => found.Node is not null)
            .Select(found => new UserSettingValue(found.Setting, TextOf(found.Node!)))];
    }

    /// <summary>
    /// Sets the value of a setting for the user.
    /// </summary>
    /// <param name="key">The key of the setting, without regard to case: <c>server.verbose</c>.</param>
    /// <param name="value">The value, as it is written (<see cref="SettingsSchema.TryParse"/>).</param>
    /// <returns>What became of the value: the file is written only when it is set.</returns>
    public UserSettingChange Set(string key, string value)
    {
        if (!schema.TryFind(key.Trim(), out var setting))
        {
            return new(UserSettingProblem.UnknownSetting);
        }

        if (!setting.IsSettable)
        {
            return new(UserSettingProblem.NotSettable, setting);
        }

        if (!SettingsSchema.TryParse(setting, value, out var parsed))
        {
            return new(UserSettingProblem.InvalidValue, setting);
        }

        if (!TryLoad(out var root))
        {
            return new(UserSettingProblem.Unreadable, setting);
        }

        root ??= new JsonObject(NodeOptions);
        var parts = SettingsSchema.ConfigurationPathOf(setting).Split(':');
        var section = root;
        foreach (var part in parts[..^1])
        {
            if (section[part] is not JsonObject child)
            {
                child = new JsonObject(NodeOptions);
                section.Remove(part);
                section[part] = child;
            }

            section = child;
        }

        section.Remove(parts[^1]);
        section[parts[^1]] = parsed switch
        {
            bool flag => JsonValue.Create(flag),
            long number => JsonValue.Create(number),
            string[] items => new JsonArray(items.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()),
            _ => JsonValue.Create((string)parsed),
        };

        Save(root);
        return new(UserSettingProblem.None, setting);
    }

    /// <summary>
    /// Removes what the user set, so that the deployed configuration says it again.
    /// </summary>
    /// <param name="key">
    /// The key of a setting, or of a section - every setting in it: <c>Server.Verbose</c>, <c>Server</c>; without regard to case. <see langword="null"/> for every
    /// setting, which deletes the file whether or not it can be read.
    /// </param>
    /// <returns>How many values the user had set that are removed.</returns>
    public int Reset(string? key = null)
    {
        if (key is null)
        {
            var count = Read().Count;
            if (fileSystem.File.Exists(path))
            {
                fileSystem.File.Delete(path);
            }

            return count;
        }

        if (!TryLoad(out var root) || root is null)
        {
            return 0;
        }

        var trimmed = key.Trim();
        var removed = 0;
        foreach (var set in Read().Where(set => string.Equals(set.Setting.Key, trimmed, StringComparison.OrdinalIgnoreCase)
            || set.Setting.Key.StartsWith(trimmed + ".", StringComparison.OrdinalIgnoreCase)))
        {
            var parts = SettingsSchema.ConfigurationPathOf(set.Setting).Split(':');
            if (Section(root, parts[..^1]) is { } section && section.Remove(parts[^1]))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            Prune(root);
            if (root.Count == 0)
            {
                fileSystem.File.Delete(path);
            }
            else
            {
                Save(root);
            }
        }

        return removed;
    }

    // a file that is not there is read as nothing; one that is not a JSON object cannot be read.
    private bool TryLoad(out JsonObject? root)
    {
        root = null;
        if (!fileSystem.File.Exists(path))
        {
            return true;
        }

        try
        {
            root = JsonNode.Parse(fileSystem.File.ReadAllText(path), NodeOptions, ReadOptions) as JsonObject;
            return root is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void Save(JsonObject root)
    {
        if (fileSystem.Path.GetDirectoryName(path) is { Length: > 0 } directory)
        {
            fileSystem.Directory.CreateDirectory(directory);
        }

        fileSystem.File.WriteAllText(path, root.ToJsonString(WriteOptions));
    }

    private static JsonNode? Find(JsonObject root, SettingDescriptor setting)
    {
        var parts = SettingsSchema.ConfigurationPathOf(setting).Split(':');
        return Section(root, parts[..^1])?[parts[^1]];
    }

    private static JsonObject? Section(JsonObject root, IEnumerable<string> parts)
    {
        JsonObject? section = root;
        foreach (var part in parts)
        {
            section = section?[part] as JsonObject;
        }

        return section;
    }

    // a section that holds nothing any more is removed with what it held.
    private static void Prune(JsonObject section)
    {
        foreach (var (name, child) in section.ToList())
        {
            if (child is JsonObject inner)
            {
                Prune(inner);
                if (inner.Count == 0)
                {
                    section.Remove(name);
                }
            }
        }
    }

    private static string TextOf(JsonNode node) => node switch
    {
        JsonArray items => string.Join(", ", items.Select(item => item is null ? string.Empty : TextOf(item))),
        JsonValue value when value.GetValueKind() == JsonValueKind.True => bool.TrueString,
        JsonValue value when value.GetValueKind() == JsonValueKind.False => bool.FalseString,
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        _ => node.ToJsonString(),
    };
}

/// <summary>
/// Adds the settings a user set (<see cref="UserSettingsStore"/>) to the configuration of a platform application.
/// </summary>
public static class UserSettingsConfiguration
{
    /// <summary>
    /// Adds the file of the user's settings to a configuration, after the deployed <c>appsettings.json</c> and before what the environment and the command line
    /// say: what a user set takes the place of what the platform was deployed with, and a process that is told otherwise when it is started is told so on purpose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A file that is not there is no setting, and neither is one that cannot be read: the platform starts as it was deployed. Of the file, only the values of
    /// settings of <paramref name="schema"/> that a user can set, and that are values the setting can be, are read; whatever else it says is not - a file written
    /// by hand never keeps the platform from starting, and with it the client that could put it back. A setting the schema does not have is read as it is: it is
    /// another application's to check.
    /// </para>
    /// <para>The file is read again when it changes.</para>
    /// </remarks>
    /// <param name="builder">The configuration.</param>
    /// <param name="path">The path of the file (<see cref="UserSettingsStore.PathIn"/>).</param>
    /// <param name="schema">The settings of the application.</param>
    public static IConfigurationBuilder AddUserSettings(this IConfigurationBuilder builder, string path, SettingsSchema schema)
    {
        var source = new UserSettingsConfigurationSource(schema)
        {
            Path = path,
            Optional = true,
            ReloadOnChange = true,
            OnLoadException = context => context.Ignore = true,
        };
        source.ResolveFileProvider();

        var deployed = builder.Sources
            .Select((candidate, index) => (Source: candidate, Index: index))
            .LastOrDefault(candidate => candidate.Source is JsonConfigurationSource { Path: { } json }
                && json.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && candidate.Source is not UserSettingsConfigurationSource);
        if (deployed.Source is null)
        {
            builder.Add(source);
        }
        else
        {
            builder.Sources.Insert(deployed.Index + 1, source);
        }

        return builder;
    }

    private sealed class UserSettingsConfigurationSource(SettingsSchema schema) : JsonConfigurationSource
    {
        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            EnsureDefaults(builder);
            return new UserSettingsConfigurationProvider(this, schema);
        }
    }

    private sealed class UserSettingsConfigurationProvider(JsonConfigurationSource source, SettingsSchema schema) : JsonConfigurationProvider(source)
    {
        public override void Load(Stream stream)
        {
            base.Load(stream);
            foreach (var (key, value) in Data.ToList())
            {
                if (schema.Governing(key) is not { } setting)
                {
                    continue;
                }

                // an item of a list is any text; any other value is one of the setting's, or nothing.
                var isItem = !string.Equals(SettingsSchema.ConfigurationPathOf(setting), key, StringComparison.OrdinalIgnoreCase);
                if (!setting.IsSettable || !isItem && (value is null || !SettingsSchema.TryParse(setting, value, out _)))
                {
                    Data.Remove(key);
                }
            }
        }
    }
}
