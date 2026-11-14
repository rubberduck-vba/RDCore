using RDCore.Diagnostics;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Server.Configuration;

[assembly: ExtensionSettings(typeof(DiagnosticsOptions), DiagnosticsOptions.Section, DiagnosticsOptions.DescriptionsResource)]

namespace RDCore.Diagnostics;

/// <summary>
/// What the diagnostics extension can be configured with, bound from the <c>Configuration:Diagnostics</c> section of its <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// The settings are described in the extension's manifest (<see cref="ExtensionSettingsAttribute"/>), so that a client offers them without the extension running.
/// </remarks>
public sealed record class DiagnosticsOptions
{
    /// <summary>The key of the section the settings are bound from, under <see cref="SettingsSchema.Root"/>.</summary>
    public const string Section = "Diagnostics";

    /// <summary>The base name of the resources that describe the settings.</summary>
    public const string DescriptionsResource = "RDCore.Diagnostics.SettingDescriptions";

    /// <summary>The settings of the extension, described in the language of the user interface.</summary>
    public static SettingsSchema Schema => SettingsSchema.For<DiagnosticsOptions>(Section, new System.Resources.ResourceManager(DescriptionsResource, typeof(DiagnosticsOptions).Assembly));

    /// <summary>
    /// The settings of the <c>RDC01001</c> diagnostic: a name that does not say what it is.
    /// </summary>
    public NameRuleOptions MeaningfulNames { get; set; } = new();

    /// <summary>
    /// The settings of the <c>RDC01002</c> diagnostic: a name that begins with a prefix that states its type.
    /// </summary>
    public NameRuleOptions HungarianNotation { get; set; } = new();

    /// <summary>
    /// Whether a <c>For</c> loop whose step is <c>1</c> states it. <see cref="ForStepConvention.Implicit"/>, the default, reports <c>Step 1</c> (<c>RDC00313</c>);
    /// <see cref="ForStepConvention.Explicit"/> reports a loop with no <c>Step</c> (<c>RDC00116</c>).
    /// </summary>
    public ForStepConvention ForStep { get; set; } = ForStepConvention.Implicit;

    /// <summary>
    /// Whether a parameter passed by reference states <c>ByRef</c>. <see cref="ByRefConvention.Explicit"/>, the default, reports a parameter that states neither
    /// <c>ByRef</c> nor <c>ByVal</c> (<c>RDC00104</c>); <see cref="ByRefConvention.Implicit"/> reports <c>ByRef</c> written (<c>RDC00314</c>).
    /// </summary>
    public ByRefConvention ByRefModifier { get; set; } = ByRefConvention.Explicit;
}

/// <summary>
/// How a <c>For</c> loop whose step is <c>1</c> is written.
/// </summary>
public enum ForStepConvention
{
    /// <summary>
    /// With no <c>Step</c>, which is <c>1</c>.
    /// </summary>
    Implicit,

    /// <summary>
    /// With <c>Step 1</c>.
    /// </summary>
    Explicit,
}

/// <summary>
/// How a parameter passed by reference is declared.
/// </summary>
public enum ByRefConvention
{
    /// <summary>
    /// With <c>ByRef</c>.
    /// </summary>
    Explicit,

    /// <summary>
    /// With neither <c>ByRef</c> nor <c>ByVal</c>, which is <c>ByRef</c>.
    /// </summary>
    Implicit,
}

/// <summary>
/// The settings of a diagnostic about the names of declarations.
/// </summary>
public sealed record class NameRuleOptions
{
    /// <summary>
    /// The names the diagnostic accepts although they break its rule: the bad names that are approved, such as <c>i</c> for the counter of a loop. A name is compared
    /// without regard to case, like every identifier of the language.
    /// </summary>
    public string[] AllowedNames { get; set; } = [];
}
