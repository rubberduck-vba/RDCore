using Microsoft.Extensions.Logging;
using RDCore.CLI;
using RDCore.SDK.Server.Configuration;
using System.Globalization;

namespace RDCore.Tests.Server.Configuration;

/// <summary>
/// The settings of the platform are read off its settings types: what each one is, when a change of it takes effect, whether it decides what a program may do
/// to the machine, and what it does - in both languages of the platform.
/// </summary>
[TestClass]
public sealed class SettingsSchemaTests
{
    private static SettingDescriptor Setting(string key)
    {
        Assert.IsTrue(SettingsSchema.Platform.Combine(AppOptions.Schema).TryFind(key, out var setting), key);
        return setting;
    }

    [TestMethod]
    [DataRow("Server.Verbose", SettingValueKind.Boolean, SettingReach.Live, false, DisplayName = "unmarked: live")]
    [DataRow("Server.ConnectTimeoutSeconds", SettingValueKind.Integer, SettingReach.NextStart, false, DisplayName = "marked")]
    [DataRow("Environment.Regional.TwoDigitYearMax", SettingValueKind.Integer, SettingReach.NextStart, false, DisplayName = "of a marked section")]
    [DataRow("Environment.AllowDllImports", SettingValueKind.Boolean, SettingReach.NextStart, true, DisplayName = "trust-sensitive")]
    [DataRow("Platform.Extensions.Allowed", SettingValueKind.TextList, SettingReach.NextStart, true, DisplayName = "of a trust-sensitive section")]
    [DataRow("Platform.Transport.Type", SettingValueKind.Choice, SettingReach.Deployment, false, DisplayName = "of a deployment section")]
    [DataRow("ServerApp.Component", SettingValueKind.Choice, SettingReach.Deployment, false, DisplayName = "the component")]
    [DataRow("Workspace.WorkspaceUri", SettingValueKind.Text, SettingReach.NextStart, false, DisplayName = "text")]
    [DataRow("CLI.Theme", SettingValueKind.Text, SettingReach.NextStart, false, DisplayName = "the console's")]
    public void ASetting_IsWhatItsTypeAndItsMarksSay(string key, SettingValueKind kind, SettingReach reach, bool isTrustSensitive)
    {
        var setting = Setting(key);

        Assert.AreEqual(kind, setting.Kind);
        Assert.AreEqual(reach, setting.Reach);
        Assert.AreEqual(isTrustSensitive, setting.IsTrustSensitive);
        Assert.AreEqual(reach != SettingReach.Deployment, setting.IsSettable);
    }

    [TestMethod]
    public void AChoice_IsOneOfTheNamesOfItsEnum_AndANumber_IsInItsRange()
    {
        CollectionAssert.AreEqual(Enum.GetNames<LogLevel>(), Setting("Server.TraceLevel").Choices.ToArray());
        CollectionAssert.AreEqual(new[] { "True", "False" }, Setting("Server.Verbose").Choices.ToArray());
        Assert.AreEqual(0L, Setting("Server.MaxRestartAttempts").Minimum);
        Assert.AreEqual((long)int.MaxValue, Setting("Server.MaxRestartAttempts").Maximum);
        Assert.AreEqual((long)int.MinValue, Setting("Environment.Lcid").Minimum);
    }

    [TestMethod]
    public void WhatNoValueCanBeWrittenFor_IsNoSetting()
    {
        var keys = SettingsSchema.Platform.Settings.Select(setting => setting.Key).ToArray();

        CollectionAssert.DoesNotContain(keys, "Platform.Extensions.Blocked", "a list of objects");
        CollectionAssert.DoesNotContain(keys, "Platform.Extensions.Manifest", "it has no setter");
        CollectionAssert.DoesNotContain(keys, "Workspace.SupportedLanguage", "it is computed");
    }

    [TestMethod]
    [DataRow("en")]
    [DataRow("fr")]
    public void EverySettingAUserCanSet_IsDescribed_InEachLanguage(string culture)
    {
        var current = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var settable = SettingsSchema.Platform.Combine(AppOptions.Schema).Settings.Where(setting => setting.IsSettable).ToList();

            Assert.IsNotEmpty(settable);
            CollectionAssert.AreEqual(Array.Empty<string>(), settable.Where(setting => string.IsNullOrWhiteSpace(setting.Description)).Select(setting => setting.Key).ToArray());
        }
        finally
        {
            CultureInfo.CurrentUICulture = current;
        }
    }

    // a description that is not translated is the English one, which the test above takes for one.
    [TestMethod]
    public void TheFrenchDescriptions_AreNotTheEnglishOnes()
    {
        var current = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            var english = SettingsSchema.Platform.Combine(AppOptions.Schema).Settings.Where(setting => setting.IsSettable).ToDictionary(setting => setting.Key, setting => setting.Description);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-CA");
            var french = SettingsSchema.Platform.Combine(AppOptions.Schema).Settings.Where(setting => setting.IsSettable).ToDictionary(setting => setting.Key, setting => setting.Description);

            CollectionAssert.AreEqual(Array.Empty<string>(), english.Keys.Where(key => english[key] == french[key]).ToArray());
        }
        finally
        {
            CultureInfo.CurrentUICulture = current;
        }
    }

    [TestMethod]
    public void AKey_IsFoundWithoutRegardToCase_AndAPathByTheSettingItIsOf()
    {
        Assert.IsTrue(SettingsSchema.Platform.TryFind("server.VERBOSE", out var verbose));
        Assert.AreEqual("Server.Verbose", verbose.Key);
        Assert.AreEqual("Configuration:Server:Verbose", SettingsSchema.ConfigurationPathOf(verbose));

        Assert.AreEqual("Platform.Extensions.Allowed", SettingsSchema.Platform.Governing("Configuration:Platform:Extensions:Allowed:0")?.Key, "an item of a list");
        Assert.IsNull(SettingsSchema.Platform.Governing("Configuration:Server"), "a section");
        Assert.IsNull(SettingsSchema.Platform.Governing("Logging:LogLevel:Default"), "not the platform's");
    }

    [TestMethod]
    [DataRow("Server.Verbose", "false", true, DisplayName = "boolean")]
    [DataRow("Server.Verbose", "TRUE", true, DisplayName = "boolean, any case")]
    [DataRow("Server.Verbose", "yes", false, DisplayName = "no boolean")]
    [DataRow("Server.MaxRestartAttempts", "5", true, DisplayName = "number")]
    [DataRow("Server.MaxRestartAttempts", "-1", false, DisplayName = "number out of range")]
    [DataRow("Server.MaxRestartAttempts", "five", false, DisplayName = "no number")]
    [DataRow("Server.TraceLevel", "warning", true, DisplayName = "choice, any case")]
    [DataRow("Server.TraceLevel", "Loud", false, DisplayName = "no choice")]
    [DataRow("Workspace.WorkspaceUri", "", true, DisplayName = "text, empty")]
    public void AValue_IsReadAsTheSettingIs(string key, string text, bool isValue)
        => Assert.AreEqual(isValue, SettingsSchema.TryParse(Setting(key), text, out _));

    [TestMethod]
    public void AValueIsGiven_AsTheConfigurationHasIt()
    {
        Assert.IsTrue(SettingsSchema.TryParse(Setting("Server.TraceLevel"), " warning ", out var level));
        Assert.AreEqual("Warning", level, "a choice as the schema spells it");

        Assert.IsTrue(SettingsSchema.TryParse(Setting("Platform.Extensions.Allowed"), "A, B,,C ", out var list));
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, (string[])list);
    }
}
