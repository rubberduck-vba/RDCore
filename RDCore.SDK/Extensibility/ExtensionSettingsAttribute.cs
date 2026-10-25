namespace RDCore.SDK.Extensibility;

/// <summary>
/// Says which settings an extension is configured with: the type it binds them to, the section of the configuration they are in, and where they are described.
/// </summary>
/// <remarks>
/// <para>
/// What this says is written into the extension's manifest when it is described (<see cref="IExtensionsProvider.Describe"/>): a client knows the settings of
/// every extension the platform has - offers them to a user, checks what is set - without starting any of them (<see cref="ExtensionInfo.Settings"/>).
/// </para>
/// <para>
/// The settings type is read as the platform's are (<see cref="Server.Configuration.SettingsSchema"/>): when a change of a setting takes effect is what its
/// <see cref="Server.Configuration.SettingAttribute"/> says, and a setting is described by the string of its key in <see cref="DescriptionsResource"/>, its dots
/// written as underscores, in each language of the platform (<see cref="Server.Configuration.SettingsSchema.Languages"/>).
/// </para>
/// </remarks>
/// <param name="optionsType">The type the settings are bound to.</param>
/// <param name="section">The key of the section they are bound from, under <see cref="Server.Configuration.SettingsSchema.Root"/>: <c>Diagnostics</c>.</param>
/// <param name="descriptionsResource">The base name of the resources of the extension's assembly that describe them: <c>RDCore.Diagnostics.SettingDescriptions</c>.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ExtensionSettingsAttribute(Type optionsType, string section, string descriptionsResource) : Attribute
{
    /// <summary>The type the settings are bound to.</summary>
    public Type OptionsType { get; } = optionsType;

    /// <summary>The key of the section the settings are bound from, under <see cref="Server.Configuration.SettingsSchema.Root"/>.</summary>
    public string Section { get; } = section;

    /// <summary>The base name of the resources of the extension's assembly that describe the settings.</summary>
    public string DescriptionsResource { get; } = descriptionsResource;
}
