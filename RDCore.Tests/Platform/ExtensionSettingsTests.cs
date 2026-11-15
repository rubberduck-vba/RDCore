using RDCore.Diagnostics;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Server.Configuration;
using System.Globalization;
using System.Text.Json;

namespace RDCore.Tests.Platform;

/// <summary>
/// An extension says which settings it is configured with, and its manifest carries them, described in each language of the platform: a client knows them without
/// the extension running.
/// </summary>
[TestClass]
public sealed class ExtensionSettingsTests
{
    private static ExtensionInfo Described(string title) => new()
    {
        Name = $"{title}.exe",
        Title = title,
        Version = new Version(1, 0),
        Publisher = "",
        PublisherWebUrl = "",
        Description = "",
        FileHash = "",
        Capabilities = [],
    };

    // the manifest as describe-ext writes it, and as the platform reads it back.
    private static ExtensionInfo Manifest()
    {
        var described = Described("RDCore.Diagnostics").WithSettingsOf(typeof(DiagnosticsOptions).Assembly);
        return JsonSerializer.Deserialize<ExtensionInfo>(JsonSerializer.Serialize(described))!;
    }

    [TestMethod]
    public void TheManifest_CarriesTheSettingsOfTheExtension()
        => CollectionAssert.AreEqual(
            new[] { "Diagnostics.MeaningfulNames.AllowedNames", "Diagnostics.HungarianNotation.AllowedNames", "Diagnostics.ForStep", "Diagnostics.ByRefModifier" },
            Manifest().Settings.Select(setting => setting.Key).ToArray());

    [TestMethod]
    public void TheSettings_AreDescribedInTheLanguageAsked_OrInThePlatformsFirst()
    {
        var manifest = Manifest();
        string? Description(string culture) => manifest.SettingsIn(CultureInfo.GetCultureInfo(culture)).Settings[0].Description;

        StringAssert.StartsWith(Description("fr-CA"), "Les noms");
        StringAssert.StartsWith(Description("en-US"), "The names");
        Assert.AreEqual(Description("en"), Description("de"), "a language the platform does not describe it in");
    }

    [TestMethod]
    public void AnAssemblyThatSaysNothingOfItsSettings_HasNone()
    {
        var manifest = Described("x").WithSettingsOf(typeof(ExtensionSettingsTests).Assembly);

        Assert.IsEmpty(manifest.Settings);
        Assert.IsEmpty(manifest.SettingsIn(CultureInfo.InvariantCulture).Settings);
    }

    [TestMethod]
    public void TheExtensionsOwnSchema_IsTheOneItsManifestSays()
        => CollectionAssert.AreEqual(
            DiagnosticsOptions.Schema.Settings.Select(setting => (setting.Key, setting.Kind, setting.Reach)).ToArray(),
            Manifest().Settings.Select(setting => (setting.Key, setting.Kind, setting.Reach)).ToArray());
}
