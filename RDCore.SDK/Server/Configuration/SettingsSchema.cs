using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace RDCore.SDK.Server.Configuration;

/// <summary>
/// What a value of a setting is, which decides how it is written and what it can be.
/// </summary>
public enum SettingValueKind
{
    /// <summary><c>True</c> or <c>False</c>.</summary>
    Boolean = 0,

    /// <summary>A whole number, between <see cref="SettingDescriptor.Minimum"/> and <see cref="SettingDescriptor.Maximum"/>.</summary>
    Integer = 1,

    /// <summary>Any text.</summary>
    Text = 2,

    /// <summary>One of <see cref="SettingDescriptor.Choices"/>.</summary>
    Choice = 3,

    /// <summary>A list of texts, written separated by commas.</summary>
    TextList = 4,
}

/// <summary>
/// One setting of the platform: its key, what its value is, when a change of it takes effect, and what it does.
/// </summary>
/// <remarks>
/// A descriptor is data, which travels where the settings types do not: to a client that completes and checks what is typed for a setting.
/// </remarks>
public sealed record class SettingDescriptor
{
    /// <summary>
    /// The key of the setting, its sections and its name separated by dots, as the settings types name them: <c>Server.Verbose</c>. A key is compared
    /// without regard to case.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>What its value is.</summary>
    public SettingValueKind Kind { get; init; }

    /// <summary>The values it can be, for a <see cref="SettingValueKind.Choice"/> or a <see cref="SettingValueKind.Boolean"/>; none otherwise.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>The least value of a <see cref="SettingValueKind.Integer"/>; <see langword="null"/> for other kinds.</summary>
    public long? Minimum { get; init; }

    /// <summary>The greatest value of a <see cref="SettingValueKind.Integer"/>; <see langword="null"/> for other kinds.</summary>
    public long? Maximum { get; init; }

    /// <summary>When a change of it takes effect (<see cref="SettingAttribute"/>).</summary>
    public SettingReach Reach { get; init; }

    /// <summary>Whether it decides what a program may do to the machine it runs on (<see cref="TrustSensitiveSettingAttribute"/>).</summary>
    public bool IsTrustSensitive { get; init; }

    /// <summary>What it does, in the language of the user interface; <see langword="null"/> when nothing describes it.</summary>
    public string? Description { get; init; }

    /// <summary>Whether a client can change it: every setting but the ones only the deployed configuration sets (<see cref="SettingReach.Deployment"/>).</summary>
    public bool IsSettable => Reach != SettingReach.Deployment;
}

/// <summary>
/// The settings a platform application can be configured with, as its settings types declare them: what a client offers to change, and checks a value against
/// before it changes one.
/// </summary>
/// <remarks>
/// <para>
/// A schema is read off the settings types by reflection. A property that has a public setter is a setting when its type is a <see langword="bool"/>, a whole
/// number, an <see langword="enum"/>, a <see langword="string"/> or a list of strings; a property whose type is a settings type of its own is a section, whose
/// settings are read the same way. Any other property is not something a value can be written for, and is not in the schema.
/// </para>
/// <para>
/// When a change takes effect is what <see cref="SettingAttribute"/> says, of the setting or of the section it is in; a setting that nothing says it of is
/// <see cref="SettingReach.Live"/>. A setting only the deployed configuration sets (<see cref="SettingReach.Deployment"/>) is in the schema too, so that a
/// client can say why it is not changed.
/// </para>
/// <para>
/// The description of a setting is the string of its key in <c>descriptions</c>, its dots written as underscores: <c>Server_Verbose</c>.
/// </para>
/// </remarks>
public sealed class SettingsSchema
{
    /// <summary>
    /// The section of the configuration the settings of the platform are under, in every application's <c>appsettings.json</c>.
    /// </summary>
    public const string Root = "Configuration";

    private static readonly ResourceManager PlatformDescriptions = new("RDCore.SDK.Server.Configuration.SettingDescriptions", typeof(SettingsSchema).Assembly);

    private readonly Dictionary<string, SettingDescriptor> _byKey;

    private SettingsSchema(IEnumerable<SettingDescriptor> settings)
    {
        Settings = [.. settings];
        _byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in Settings)
        {
            if (!_byKey.TryAdd(setting.Key, setting))
            {
                throw new ArgumentException($"The setting '{setting.Key}' is declared twice.", nameof(settings));
            }
        }
    }

    /// <summary>
    /// The settings every platform application has (<see cref="SdkAppOptions"/>), described in the language of the user interface.
    /// </summary>
    public static SettingsSchema Platform => For<SdkAppOptions>(string.Empty, PlatformDescriptions);

    /// <summary>
    /// Every setting, in the order the settings types declare them.
    /// </summary>
    public IReadOnlyList<SettingDescriptor> Settings { get; }

    /// <summary>
    /// The schema of a settings type.
    /// </summary>
    /// <typeparam name="TOptions">The settings type.</typeparam>
    /// <param name="section">
    /// The key of the section the type is bound from, under <see cref="Root"/>: <c>CLI</c> for <c>Configuration:CLI</c>; empty for a type whose properties are
    /// the sections themselves.
    /// </param>
    /// <param name="descriptions">Where the descriptions of the settings are; <see langword="null"/> when nothing describes them.</param>
    public static SettingsSchema For<TOptions>(string section, ResourceManager? descriptions = null)
        => For(typeof(TOptions), section, descriptions);

    /// <inheritdoc cref="For{TOptions}(string, ResourceManager?)"/>
    /// <param name="optionsType">The settings type.</param>
    /// <param name="section">The key of the section the type is bound from, under <see cref="Root"/>.</param>
    /// <param name="descriptions">Where the descriptions of the settings are.</param>
    public static SettingsSchema For(Type optionsType, string section, ResourceManager? descriptions = null)
    {
        var settings = new List<SettingDescriptor>();
        Read(optionsType, section, SettingReach.Live, isTrustSensitive: false, descriptions, settings, [optionsType]);
        return new SettingsSchema(settings);
    }

    /// <summary>
    /// The settings of this schema and of <paramref name="other"/>: the platform's and an application's own.
    /// </summary>
    /// <param name="other">The other schema.</param>
    /// <exception cref="ArgumentException">Both schemas have a setting of the same key.</exception>
    public SettingsSchema Combine(SettingsSchema other) => new([.. Settings, .. other.Settings]);

    /// <summary>
    /// Gets the setting of a key, without regard to case.
    /// </summary>
    /// <param name="key">The key: <c>server.verbose</c>.</param>
    /// <param name="setting">The setting.</param>
    /// <returns><see langword="false"/> when no setting has the key.</returns>
    public bool TryFind(string key, [NotNullWhen(true)] out SettingDescriptor? setting) => _byKey.TryGetValue(key, out setting);

    /// <summary>
    /// The setting a path of the configuration is the value of, or a part of the value of - an item of a list: <c>Configuration:Server:Verbose</c>.
    /// </summary>
    /// <param name="configurationPath">The path, its sections separated by <c>:</c>.</param>
    /// <returns>The setting; <see langword="null"/> when the path is of no setting of the schema.</returns>
    public SettingDescriptor? Governing(string configurationPath)
    {
        if (!configurationPath.StartsWith(Root + ":", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = configurationPath[(Root.Length + 1)..].Split(':');
        for (var length = parts.Length; length > 0; length--)
        {
            if (TryFind(string.Join('.', parts[..length]), out var setting))
            {
                return setting;
            }
        }

        return null;
    }

    /// <summary>
    /// The path of the configuration a setting's value is at: <c>Configuration:Server:Verbose</c>.
    /// </summary>
    /// <param name="setting">The setting.</param>
    public static string ConfigurationPathOf(SettingDescriptor setting) => $"{Root}:{setting.Key.Replace('.', ':')}";

    /// <summary>
    /// Reads a value of a setting as it is written - at a prompt, on a command line - and gives it as the configuration has it.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="text">The value, as it is written: <c>false</c>, <c>30</c>, <c>dark</c>, <c>i, j, k</c>.</param>
    /// <param name="value">
    /// The value: a <see langword="bool"/>, a <see langword="long"/>, a <see langword="string"/> - a choice as the schema spells it - or a list of strings.
    /// </param>
    /// <returns><see langword="false"/> when the text is no value of the setting.</returns>
    public static bool TryParse(SettingDescriptor setting, string text, [NotNullWhen(true)] out object? value)
    {
        value = null;
        var trimmed = text.Trim();
        switch (setting.Kind)
        {
            case SettingValueKind.Boolean when bool.TryParse(trimmed, out var flag):
                value = flag;
                return true;

            case SettingValueKind.Integer when long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                && (setting.Minimum is not { } least || number >= least) && (setting.Maximum is not { } greatest || number <= greatest):
                value = number;
                return true;

            case SettingValueKind.Choice when setting.Choices.FirstOrDefault(choice => string.Equals(choice, trimmed, StringComparison.OrdinalIgnoreCase)) is { } chosen:
                value = chosen;
                return true;

            case SettingValueKind.Text:
                value = text;
                return true;

            case SettingValueKind.TextList:
                value = trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                return true;

            default:
                return false;
        }
    }

    private static void Read(
        Type type, string section, SettingReach reach, bool isTrustSensitive, ResourceManager? descriptions, List<SettingDescriptor> settings, HashSet<Type> reading)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.SetMethod is not { IsPublic: true })
            {
                continue;
            }

            var key = section.Length == 0 ? property.Name : $"{section}.{property.Name}";
            var propertyReach = property.GetCustomAttribute<SettingAttribute>()?.Reach ?? reach;
            var propertyIsTrustSensitive = isTrustSensitive || property.IsDefined(typeof(TrustSensitiveSettingAttribute));
            var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (KindOf(valueType) is { } kind)
            {
                var range = property.GetCustomAttribute<RangeAttribute>();
                settings.Add(new SettingDescriptor
                {
                    Key = key,
                    Kind = kind,
                    Choices = kind switch
                    {
                        SettingValueKind.Boolean => [bool.TrueString, bool.FalseString],
                        SettingValueKind.Choice => Enum.GetNames(valueType),
                        _ => [],
                    },
                    Minimum = kind == SettingValueKind.Integer ? range?.Minimum is { } least ? Convert.ToInt64(least, CultureInfo.InvariantCulture) : MinimumOf(valueType) : null,
                    Maximum = kind == SettingValueKind.Integer ? range?.Maximum is { } greatest ? Convert.ToInt64(greatest, CultureInfo.InvariantCulture) : MaximumOf(valueType) : null,
                    Reach = propertyReach,
                    IsTrustSensitive = propertyIsTrustSensitive,
                    Description = descriptions?.GetString(key.Replace('.', '_'), CultureInfo.CurrentUICulture),
                });
            }
            else if (IsSection(valueType) && reading.Add(valueType))
            {
                Read(valueType, key, propertyReach, propertyIsTrustSensitive, descriptions, settings, reading);
                reading.Remove(valueType);
            }
        }
    }

    private static SettingValueKind? KindOf(Type type) => type switch
    {
        _ when type == typeof(bool) => SettingValueKind.Boolean,
        _ when type.IsEnum => SettingValueKind.Choice,
        _ when type == typeof(string) => SettingValueKind.Text,
        _ when type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) => SettingValueKind.Integer,
        _ when type == typeof(string[]) || type != typeof(object) && type.IsAssignableFrom(typeof(List<string>)) => SettingValueKind.TextList,
        _ => null,
    };

    // a type of settings of its own: a class that the configuration can create, and that is no collection.
    private static bool IsSection(Type type)
        => type.IsClass && type != typeof(string) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null;

    private static long MinimumOf(Type type) => type == typeof(long) ? long.MinValue : type == typeof(short) ? short.MinValue : type == typeof(byte) ? byte.MinValue : int.MinValue;

    private static long MaximumOf(Type type) => type == typeof(long) ? long.MaxValue : type == typeof(short) ? short.MaxValue : type == typeof(byte) ? byte.MaxValue : int.MaxValue;
}
