using Microsoft.Extensions.Configuration;
using NSubstitute;
using RDCore.CLI;
using RDCore.CLI.App.Commands;
using RDCore.CLI.App.Configuration;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.Diagnostics;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>CONFIG</c> shows the settings of the platform - its own, the console's, and its extensions' - and sets them for the user, in their own settings: never a value
/// a setting cannot be, never one only the deployment sets, and a setting that decides what a program may do to the machine with a ⚠️ callout.
/// </summary>
[TestClass]
public sealed class ConfigCommandTests
{
    private static readonly string UserFolder = Path.Combine(Path.GetTempPath(), "rdcore-config-user");
    private static readonly string UserFile = Path.Combine(UserFolder, UserSettingsStore.FileName);

    private readonly MockFileSystem _fileSystem = new();
    private readonly IExtensionsProvider _extensions = Substitute.For<IExtensionsProvider>();
    private readonly PlatformSettings _settings;

    public ConfigCommandTests()
    {
        var environment = Substitute.For<IPlatformEnvironment>();
        environment.UserDataDirectory.Returns(UserFolder);
        _extensions.Discover().Returns([Manifest()]);
        _settings = new PlatformSettings(_fileSystem, environment, _extensions);
    }

    // the manifest of the diagnostics extension, as describe-ext writes it.
    private static ExtensionInfo Manifest()
        => new ExtensionInfo("RDCore.Diagnostics.exe", "RDCore.Diagnostics", new Version(1, 0), "", "", "", "", []).WithSettingsOf(typeof(DiagnosticsOptions).Assembly);

    private ConfigCommand Sut => new(_settings);

    private static string[] Texts(IEnumerable<ConfigOutput> outputs) => [.. outputs.Select(output => output.Text)];

    private IConfigurationRoot UserSettings() => new ConfigurationBuilder().AddJsonStream(_fileSystem.File.OpenRead(UserFile)).Build();

    [TestMethod]
    public void AValue_IsSetForTheUser_AndListedAsWhatTheySet()
    {
        var set = Sut.Run("server.verbose false").Single();

        Assert.AreEqual(MessageKind.Success, set.Kind);
        Assert.AreEqual(string.Format(Resources.Config_Set, "Server.Verbose", "False"), set.Text);
        Assert.AreEqual("False", UserSettings()["Configuration:Server:Verbose"]);
        CollectionAssert.AreEqual(new[] { "  Server.Verbose = False" }, Texts(Sut.Run("")));
    }

    [TestMethod]
    public void NothingSet_IsSaid()
        => Assert.AreEqual(Resources.Config_NothingSet, Sut.Run(" ").Single().Text);

    [TestMethod]
    public void ASetting_IsShownWithWhatItIs_AndWhatItCanBe()
    {
        var shown = Texts(Sut.Run("Server.TraceLevel"));

        Assert.AreEqual(string.Format(Resources.Config_NotSet, "Server.TraceLevel"), shown[0]);
        Assert.AreEqual(3, shown.Length, "what it is, what it does, what it can be");
        StringAssert.Contains(shown[2], "Warning");

        Sut.Run("Server.TraceLevel error");
        Assert.AreEqual("Server.TraceLevel = Error", Texts(Sut.Run("Server.TraceLevel"))[0]);
    }

    [TestMethod]
    public void ASection_IsShownAsItsSettings()
    {
        Sut.Run("Diagnostics.MeaningfulNames.AllowedNames i, j, k");

        CollectionAssert.AreEqual(
            new[] { "  Diagnostics.MeaningfulNames.AllowedNames = i, j, k", "  Diagnostics.HungarianNotation.AllowedNames" },
            Texts(Sut.Run("diagnostics")));
    }

    [TestMethod]
    public void AValueThatCannotBeSet_IsAnError_AndNothingIsWritten()
    {
        var refused = new[]
        {
            (Sut.Run("Server.Loudness true").Single(), string.Format(Resources.Config_Unknown, "Server.Loudness")),
            (Sut.Run("Platform.Transport.Type NamedPipe").Single(), string.Format(Resources.Config_Deployment, "Platform.Transport.Type")),
            (Sut.Run("Server.ConnectTimeoutSeconds soon").Single(), string.Format(Resources.Config_Invalid, "soon", "Server.ConnectTimeoutSeconds")),
        };

        foreach (var (output, expected) in refused)
        {
            Assert.AreEqual(MessageKind.Error, output.Kind);
            Assert.AreEqual(expected, output.Text);
        }

        Assert.IsFalse(_fileSystem.File.Exists(UserFile));
    }

    [TestMethod]
    public void AnInvalidValue_SaysWhatTheSettingCanBe()
        => Assert.AreEqual(string.Format(Resources.Config_Values_Integer, 0, int.MaxValue), Sut.Run("Server.ConnectTimeoutSeconds soon").Single().Detail);

    [TestMethod]
    public void ASettingThatDecidesWhatAProgramMayDo_IsSet_WithACautionCallout()
    {
        var outputs = Sut.Run("Environment.AllowDllImports False");

        Assert.AreEqual(MessageKind.Success, outputs[0].Kind);
        Assert.AreEqual(MessageKind.Warning, outputs[1].Kind, "the console draws a warning with its ⚠️ glyph");
        Assert.AreEqual(string.Format(Resources.Config_TrustSensitive, "Environment.AllowDllImports"), outputs[1].Text);
        Assert.IsNotNull(outputs[1].Detail, "what the setting does");
    }

    [TestMethod]
    public void Resetting_PutsASettingASectionOrEverythingBack()
    {
        Sut.Run("Server.Verbose false");
        Sut.Run("Server.TraceLevel Error");
        Sut.Run("CLI.Theme light");

        Assert.AreEqual(string.Format(Resources.Config_Reset, 2), Sut.Run("reset server").Single().Text);
        Assert.AreEqual(Resources.Config_NothingToReset, Sut.Run("RESET Server").Single().Text);
        Assert.AreEqual(string.Format(Resources.Config_Reset, 1), Sut.Run("RESET").Single().Text);
        Assert.IsFalse(_fileSystem.File.Exists(UserFile));
    }

    [TestMethod]
    public void AFileThatCannotBeRead_IsSaid_AndResetDeletesIt()
    {
        _fileSystem.AddFile(UserFile, new MockFileData("{ not json"));

        Assert.AreEqual(string.Format(Resources.Config_Unreadable, UserFile), Sut.Run("Server.Verbose false").Single().Text);
        Assert.AreEqual(Resources.Config_NothingToReset, Sut.Run("RESET").Single().Text);
        Assert.IsFalse(_fileSystem.File.Exists(UserFile));
    }

    [TestMethod]
    public void TheVerb_DescribesTheSettingsAUserCanSet_TheConsolesThemesAmongTheirValues()
    {
        var descriptor = Sut.Describe(["light", "dark"]);
        var settings = descriptor.Forms[1][0].Settings;

        Assert.AreEqual(VerbArgumentKind.Setting, descriptor.Forms[1][0].Kind);
        Assert.AreEqual(VerbArgumentKind.SettingValue, descriptor.Forms[1][1].Kind);
        Assert.IsTrue(settings.All(setting => setting.IsSettable), "no deployment setting");
        Assert.IsTrue(settings.Any(setting => setting.Key == "Diagnostics.HungarianNotation.AllowedNames"), "an extension's");
        CollectionAssert.AreEqual(new[] { "dark", "light" }, settings.Single(setting => setting.Key == "CLI.Theme").Choices.ToArray());
    }

    [TestMethod]
    public void AnExtensionsSettingThatThePlatformHasAlready_IsThePlatforms()
    {
        var copy = Manifest() with { Settings = [new SettingDescriptor { Key = "Server.Verbose", Kind = SettingValueKind.Text }] };
        _extensions.Discover().Returns([copy]);

        Assert.IsTrue(_settings.Schema.TryFind("Server.Verbose", out var verbose));
        Assert.AreEqual(SettingValueKind.Boolean, verbose.Kind);
    }

    [TestMethod]
    public async Task TheShellsVerb_WritesWhatItSays()
    {
        var console = Substitute.For<IReplConsole>();
        var themes = Substitute.For<IAppThemeService>();
        var command = new ConfigReplCommand(Sut, themes);
        var context = new ReplCommandContext(new ReplProgram(), console, Substitute.For<IReplPlatformClient>(), null!, []);

        Assert.AreEqual(ReplCommandResult.Continue, await command.ExecuteAsync(context, "Server.Verbose false", CancellationToken.None));
        console.Received(1).WriteMessage(MessageKind.Success, string.Format(Resources.Config_Set, "Server.Verbose", "False"), null);
        CollectionAssert.AreEqual(new[] { "CONFIGURE" }, command.Aliases.ToArray());
    }

    [TestMethod]
    public async Task TheCommandModeVerb_FailsWhereTheShellsSaysSo()
    {
        var command = new ConfigCliCommand(Sut, Substitute.For<IConsoleMessageWriter>());

        Assert.AreEqual(0, await command.ExecuteAsync(["Server.Verbose", "false"], CancellationToken.None));
        Assert.AreEqual(1, await command.ExecuteAsync(["Server.Verbose", "maybe"], CancellationToken.None));
        Assert.AreEqual(0, await command.ExecuteAsync(["Platform.Extensions.Allowed", "A,", "B"], CancellationToken.None), "a value is the rest of the line");
        Assert.AreEqual("B", UserSettings()["Configuration:Platform:Extensions:Allowed:1"]);
    }
}
