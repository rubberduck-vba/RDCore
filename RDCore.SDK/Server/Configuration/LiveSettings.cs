using Microsoft.Extensions.Configuration;
using RDCore.SDK.Platform;
using System.Globalization;
using System.Reflection;

namespace RDCore.SDK.Server.Configuration;

/// <summary>
/// Registers the settings objects of a component whose live settings are set while it runs (<see cref="ILiveSettings"/>).
/// </summary>
public static class LiveSettingsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the instance of <c>IOptions&lt;TOptions&gt;</c> as one whose live settings are set while the component runs.
    /// </summary>
    /// <typeparam name="TOptions">The settings type.</typeparam>
    /// <param name="services">The services of the component.</param>
    /// <param name="schema">The settings, of which the live ones of <paramref name="section"/> are the object's.</param>
    /// <param name="section">The key of the section the object is bound from, under <see cref="SettingsSchema.Root"/>.</param>
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddLiveSettings<TOptions>(
        this Microsoft.Extensions.DependencyInjection.IServiceCollection services, SettingsSchema schema, string section)
        where TOptions : class
        => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services,
            provider => new LiveSettingsTarget(schema, section,
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Options.IOptions<TOptions>>(provider).Value));
}

/// <summary>
/// The settings of a settings object that take effect while a component runs (<see cref="SettingReach.Live"/>): the object's settings in a schema, and the
/// section of the configuration the object is bound from.
/// </summary>
/// <remarks>
/// The object is the one the component reads its settings off - the instance of its <c>IOptions&lt;T&gt;</c> - and a live setting is set on it in place, so that
/// what reads the setting when it uses it reads the new value. What copied a value when it started keeps the one it copied: a setting that is read so is not a
/// live one, whatever it is marked.
/// </remarks>
/// <param name="schema">The settings, of which the live ones of <paramref name="section"/> are this object's.</param>
/// <param name="section">The key of the section the object is bound from, under <see cref="SettingsSchema.Root"/>; empty for an object whose properties are the sections.</param>
/// <param name="root">The settings object.</param>
public sealed class LiveSettingsTarget(SettingsSchema schema, string section, object root)
{
    /// <summary>The settings object.</summary>
    public object Root { get; } = root;

    /// <summary>The live settings of the object, each with the property it is set on, and the value it had when the component started.</summary>
    internal IReadOnlyList<(SettingDescriptor Setting, PropertyInfo Property, object Owner, object? Started)> Settings { get; } = Read(schema, section, root);

    private static List<(SettingDescriptor, PropertyInfo, object, object?)> Read(SettingsSchema schema, string section, object root)
    {
        var prefix = section.Length == 0 ? string.Empty : section + ".";
        var settings = new List<(SettingDescriptor, PropertyInfo, object, object?)>();
        foreach (var setting in schema.Settings.Where(setting => setting.Reach == SettingReach.Live && setting.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            object? owner = root;
            PropertyInfo? property = null;
            var path = setting.Key[prefix.Length..].Split('.');
            for (var index = 0; index < path.Length && owner is not null; index++)
            {
                property = owner.GetType().GetProperty(path[index], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property is null)
                {
                    owner = null;
                }
                else if (index < path.Length - 1)
                {
                    owner = property.GetValue(owner);
                }
            }

            if (owner is not null && property is { CanWrite: true })
            {
                settings.Add((setting, property, owner, property.GetValue(owner)));
            }
        }

        return settings;
    }
}

/// <summary>
/// The settings a component was given by its client that take effect while it runs (<see cref="SettingReach.Live"/>), and what it hands down to the components
/// it is the client of.
/// </summary>
/// <remarks>
/// <para>
/// A client says what its user set (<strong>LSP 3.17</strong> §Configuration Request): the server asks with <c>workspace/configuration</c>, when it is
/// initialized and whenever the client says something changed (<c>workspace/didChangeConfiguration</c>). The settings travel as a configuration section
/// (<see cref="LiveSettingsProtocol"/>): the keys under <see cref="SettingsSchema.Root"/>, separated by <c>:</c>, as text - <c>Server:Verbose</c> is
/// <c>False</c>.
/// </para>
/// <para>
/// Of what a client says, the live settings this component has (<see cref="LiveSettingsTarget"/>) are set on its settings objects, a value a setting cannot be
/// left out; a live setting the client says nothing of takes the value it had when the component started back. A setting that is not live is not set:
/// it is read when a component starts, from what its user set (<see cref="UserSettingsStore"/>). What is said of a setting this component does not have is
/// another component's, and is handed down as it is.
/// </para>
/// <para>
/// A component no client has said anything to - the shell, which is the client of the others - hands down what its user set, as the user's settings say it now.
/// </para>
/// </remarks>
public interface ILiveSettings
{
    /// <summary>
    /// What this component hands down to the components it is the client of: what its own client said, or - before that, and in a component that has no client -
    /// the live settings its user set, as the user's settings say them now. The keys are under <see cref="SettingsSchema.Root"/>, separated by <c>:</c>.
    /// </summary>
    IReadOnlyDictionary<string, string> Current { get; }

    /// <summary>
    /// What the client said its user set, without what the component took for a setting the user did not set (the editor's tab size, <see cref="Formatting.FormattingTabSize"/>); in a component
    /// that has no client, or before its client said anything, the live settings its user set.
    /// </summary>
    IReadOnlyDictionary<string, string> Said { get; }

    /// <summary>Whether the client has said what its settings are: <see cref="Apply(IReadOnlyDictionary{string, string})"/> was called.</summary>
    bool HasReceived { get; }

    /// <summary>
    /// Takes what the client said the settings are, and sets the live ones on the settings objects.
    /// </summary>
    /// <param name="settings">The settings, by their keys under <see cref="SettingsSchema.Root"/>, separated by <c>:</c>.</param>
    void Apply(IReadOnlyDictionary<string, string> settings);

    /// <summary>
    /// Takes what the client said the settings are, and the settings the component takes for the ones the client did not say, and sets the live ones on the settings objects.
    /// </summary>
    /// <param name="said">What the client said, by the keys under <see cref="SettingsSchema.Root"/>, separated by <c>:</c>: <see cref="Said"/>.</param>
    /// <param name="adopted">What the component takes besides, for settings the client did not say.</param>
    void Apply(IReadOnlyDictionary<string, string> said, IReadOnlyDictionary<string, string> adopted);

    /// <summary>
    /// Sets the live settings on the settings objects as the user's settings say them now: what a component that is no other's server does when its user changes
    /// a setting.
    /// </summary>
    void Refresh();

    /// <summary>Raised once the live settings were set again.</summary>
    event EventHandler? Changed;
}

/// <inheritdoc cref="ILiveSettings"/>
/// <param name="targets">The settings objects of the component.</param>
/// <param name="schema">The settings of the component: what is not one of its live settings is not passed down by it from its user's settings.</param>
/// <param name="userSettingsPath">The file of the user's settings (<see cref="UserSettingsStore.PathIn"/>).</param>
public sealed class LiveSettings(IEnumerable<LiveSettingsTarget> targets, SettingsSchema schema, string userSettingsPath) : ILiveSettings
{
    private readonly IReadOnlyList<LiveSettingsTarget> _targets = [.. targets];
    private readonly object _applying = new();
    private IReadOnlyDictionary<string, string>? _received;
    private IReadOnlyDictionary<string, string>? _said;

    /// <summary>
    /// The live settings of the user's settings of a platform: its settings objects, and its user's file.
    /// </summary>
    /// <param name="targets">The settings objects of the component.</param>
    /// <param name="schema">The settings of the component.</param>
    /// <param name="environment">Where the user's settings are.</param>
    public LiveSettings(IEnumerable<LiveSettingsTarget> targets, SettingsSchema schema, IPlatformEnvironment environment)
        : this(targets, schema, UserSettingsStore.PathIn(environment))
    {
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Current => _received ?? UserLiveSettings();

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Said => _said ?? UserLiveSettings();

    /// <inheritdoc/>
    public bool HasReceived => _received is not null;

    /// <inheritdoc/>
    public void Apply(IReadOnlyDictionary<string, string> settings) => Apply(settings, new Dictionary<string, string>());

    /// <inheritdoc/>
    public void Apply(IReadOnlyDictionary<string, string> said, IReadOnlyDictionary<string, string> adopted)
    {
        lock (_applying)
        {
            _said = new Dictionary<string, string>(said, StringComparer.OrdinalIgnoreCase);
            var received = new Dictionary<string, string>(said, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in adopted)
            {
                received[key] = value;
            }

            _received = received;
            Set(_received);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Refresh()
    {
        lock (_applying)
        {
            Set(_received ?? UserLiveSettings());
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // every live setting of the component takes the value it is said to have, or the one it started with.
    private void Set(IReadOnlyDictionary<string, string> settings)
    {
        foreach (var target in _targets)
        {
            foreach (var (setting, property, owner, started) in target.Settings)
            {
                property.SetValue(owner, ValueOf(setting, settings, property.PropertyType) is { } value ? value : started);
            }
        }
    }

    // a value the setting can be, as the type of its property has it; nothing when it is said to have none, or one it cannot be.
    private static object? ValueOf(SettingDescriptor setting, IReadOnlyDictionary<string, string> settings, Type type)
    {
        var path = setting.Key.Replace('.', ':');
        object? parsed = null;
        if (setting.Kind == SettingValueKind.TextList)
        {
            var items = settings
                .Where(pair => pair.Key.StartsWith(path + ":", StringComparison.OrdinalIgnoreCase) && int.TryParse(pair.Key[(path.Length + 1)..], out _))
                .OrderBy(pair => int.Parse(pair.Key[(path.Length + 1)..], CultureInfo.InvariantCulture))
                .Select(pair => pair.Value)
                .ToArray();
            if (items.Length > 0)
            {
                parsed = items;
            }
            else if (settings.TryGetValue(path, out var list) && SettingsSchema.TryParse(setting, list, out var values))
            {
                parsed = values;
            }
        }
        else if (settings.TryGetValue(path, out var text))
        {
            SettingsSchema.TryParse(setting, text, out parsed);
        }

        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return parsed switch
        {
            null => null,
            string[] items when underlying == typeof(string[]) => items,
            string[] items when underlying.IsAssignableFrom(typeof(List<string>)) => new List<string>(items),
            string choice when underlying.IsEnum => Enum.Parse(underlying, choice, ignoreCase: true),
            long number => Convert.ChangeType(number, underlying, CultureInfo.InvariantCulture),
            _ => parsed,
        };
    }

    // the user's settings, as their file says them now: every value but those of settings this component has that are not live, which it is not the one to pass.
    private Dictionary<string, string> UserLiveSettings()
    {
        var configuration = new ConfigurationBuilder().AddUserSettings(userSettingsPath, schema, reloadOnChange: false).Build();
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in configuration.GetSection(SettingsSchema.Root).AsEnumerable(makePathsRelative: true))
        {
            if (value is not null && schema.Governing($"{SettingsSchema.Root}:{key}") is not { Reach: not SettingReach.Live })
            {
                settings[key] = value;
            }
        }

        return settings;
    }
}
