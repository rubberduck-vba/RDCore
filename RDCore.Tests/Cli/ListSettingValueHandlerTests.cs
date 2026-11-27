using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.CLI.App.Configuration;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli;

/// <summary>
/// The shell takes the change of a list of its user's settings the language server asks for (<c>rdcore/settings/listValue</c>): a code disabled by a code action
/// is added to the user's list, as <c>CONFIG</c> would add it, and the language server told the settings changed. A list of the workspace's settings is
/// changed in the file of the shell's workspace.
/// </summary>
[TestClass]
public sealed class ListSettingValueHandlerTests
{
    private const string Key = "Analysis.DisabledDiagnostics";
    private static readonly string UserFolder = Path.Combine(Path.GetTempPath(), "rdcore-listvalue-user");
    private static readonly string WorkspaceFolder = Path.Combine(Path.GetTempPath(), "rdcore-listvalue-ws");

    private readonly MockFileSystem _fileSystem = new();

    private readonly PlatformSettings _settings;
    private readonly ILiveSettings _live = Substitute.For<ILiveSettings>();
    private int _told;

    public ListSettingValueHandlerTests()
    {
        var environment = Substitute.For<IPlatformEnvironment>();
        environment.UserDataDirectory.Returns(UserFolder);
        var extensions = Substitute.For<IExtensionsProvider>();
        extensions.Discover().Returns([]);
        _settings = new PlatformSettings(_fileSystem, environment, extensions);
    }

    private ListSettingValueHandler Sut() => new(_settings, _live, new LiveSettingsNotifier(_ => { _told++; return Task.CompletedTask; }),
        Options.Create(new SdkAppOptions { Workspace = new() { WorkspaceUri = WorkspaceFolder } }));

    private static ListSettingValueRequest Change(string value, bool remove = false, SettingScope scope = SettingScope.Platform)
        => new(new ListSettingValueParams { Key = Key, Value = value, Scope = scope, Remove = remove, Document = new Uri("file:///c:/ws/Module1.bas") });

    private string? Listed() => _settings.Store.Read().SingleOrDefault(value => value.Setting.Key == Key)?.Value;

    [TestMethod]
    public async Task ACodeDisabled_IsAddedToTheUsersList_AndTheLanguageServerToldTheSettingsChanged()
    {
        var answer = await Sut().Handle(Change("rdc01002"), CancellationToken.None);

        Assert.IsTrue(answer.Applied);
        StringAssert.Contains(Listed(), "RDC01002");
        _live.Received(1).Refresh();
        Assert.AreEqual(1, _told);
    }

    // the list holds the code, as it was asked to: it is applied, and listed once.
    [TestMethod]
    public async Task ACodeAlreadyDisabled_IsListedOnce()
    {
        await Sut().Handle(Change("RDC01002"), CancellationToken.None);

        var answer = await Sut().Handle(Change("rdc01002"), CancellationToken.None);

        Assert.IsTrue(answer.Applied);
        Assert.AreEqual(Listed()!.IndexOf("RDC01002", StringComparison.OrdinalIgnoreCase), Listed()!.LastIndexOf("RDC01002", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ACodeEnabledAgain_IsRemovedFromTheUsersList()
    {
        await Sut().Handle(Change("RDC01001"), CancellationToken.None);
        await Sut().Handle(Change("RDC01002"), CancellationToken.None);

        var answer = await Sut().Handle(Change("RDC01001", remove: true), CancellationToken.None);

        Assert.IsTrue(answer.Applied);
        Assert.DoesNotContain("RDC01001", Listed() ?? string.Empty);
        StringAssert.Contains(Listed(), "RDC01002");
        Assert.AreEqual(3, _told);
    }

    [TestMethod]
    public async Task AChangeOfTheWorkspacesList_IsMadeInTheWorkspacesFile_AndNotInTheUsers()
    {
        _fileSystem.AddFile(WorkspaceSettingsStore.PathIn(WorkspaceFolder), new MockFileData("""{ "ProgramSettings": { "MyApp": { "Theme": "Dark" } } }"""));

        var answer = await Sut().Handle(Change("rdc01002", scope: SettingScope.Workspace), CancellationToken.None);
        await Sut().Handle(Change("RDC01003", scope: SettingScope.Workspace), CancellationToken.None);
        await Sut().Handle(Change("RDC01003", remove: true, scope: SettingScope.Workspace), CancellationToken.None);

        Assert.IsTrue(answer.Applied);
        Assert.IsNull(Listed());
        CollectionAssert.AreEqual(new[] { "RDC01002" }, _settings.Workspace.Read(new Uri(WorkspaceFolder), Key).ToArray());
        StringAssert.Contains(_fileSystem.File.ReadAllText(WorkspaceSettingsStore.PathIn(WorkspaceFolder)), "Dark");
        Assert.AreEqual(3, _told);
    }

    [TestMethod]
    public async Task AWorkspace_IsNotAskedToKeepATrustSensitiveList()
    {
        var request = new ListSettingValueRequest(new ListSettingValueParams
        {
            Key = "Platform.Extensions.Allowed",
            Value = "Anything",
            Scope = SettingScope.Workspace,
        });

        var answer = await Sut().Handle(request, CancellationToken.None);

        Assert.IsFalse(answer.Applied);
        Assert.IsFalse(_fileSystem.File.Exists(WorkspaceSettingsStore.PathIn(WorkspaceFolder)));
        Assert.AreEqual(0, _told);
        _live.DidNotReceive().Refresh();
    }

    // an item the console's configuration lists, and the user did not add: the user's settings add to that list, and do not take from it.
    [TestMethod]
    [DataRow(Key, "RDC01001", false, DisplayName = "a code the application's settings disable")]
    [DataRow("Platform.Extensions.Allowed", "RDCore.Diagnostics", true, DisplayName = "an extension the platform's settings allow")]
    public async Task AnItemTheUserDidNotAdd_IsNotRemoved_AndTheAnswerSaysWhy(string key, string item, bool isExtension)
    {
        var environment = Substitute.For<IPlatformEnvironment>();
        environment.UserDataDirectory.Returns(UserFolder);
        var extensions = Substitute.For<IExtensionsProvider>();
        extensions.Discover().Returns([]);
        var path = $"{SettingsSchema.Root}:{key.Replace('.', ':')}:0";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [path] = item }).Build();
        var settings = new PlatformSettings(_fileSystem, environment, extensions, configuration);
        var sut = new ListSettingValueHandler(settings, _live, new LiveSettingsNotifier(_ => { _told++; return Task.CompletedTask; }),
            Options.Create(new SdkAppOptions()));

        var change = new ListSettingValueRequest(new ListSettingValueParams { Key = key, Value = item, Scope = SettingScope.Platform, Remove = true });

        var answer = await sut.Handle(change, CancellationToken.None);

        Assert.IsFalse(answer.Applied);
        Assert.AreEqual(string.Format(isExtension ? CLI.Resources.Config_NotTheUsers_Extension : CLI.Resources.Config_NotTheUsers, item, key), answer.Reason);
        Assert.IsFalse(_fileSystem.File.Exists(UserSettingsStore.PathIn(environment)));
        Assert.AreEqual(0, _told);
    }
}
