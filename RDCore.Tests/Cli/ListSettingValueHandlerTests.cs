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
/// is added to the user's list, as <c>CONFIG</c> would add it, and the language server told the settings changed. A list of a workspace's settings is not the shell's.
/// </summary>
[TestClass]
public sealed class ListSettingValueHandlerTests
{
    private const string Key = "Analysis.DisabledDiagnostics";
    private static readonly string UserFolder = Path.Combine(Path.GetTempPath(), "rdcore-listvalue-user");

    private readonly PlatformSettings _settings;
    private readonly ILiveSettings _live = Substitute.For<ILiveSettings>();
    private int _told;

    public ListSettingValueHandlerTests()
    {
        var environment = Substitute.For<IPlatformEnvironment>();
        environment.UserDataDirectory.Returns(UserFolder);
        var extensions = Substitute.For<IExtensionsProvider>();
        extensions.Discover().Returns([]);
        _settings = new PlatformSettings(new MockFileSystem(), environment, extensions);
    }

    private ListSettingValueHandler Sut() => new(_settings, _live, new LiveSettingsNotifier(_ => { _told++; return Task.CompletedTask; }));

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
    public async Task AChangeOfAWorkspacesList_IsNotTheShells_AndIsNotApplied()
    {
        var answer = await Sut().Handle(Change("RDC01002", scope: SettingScope.Workspace), CancellationToken.None);

        Assert.IsFalse(answer.Applied);
        Assert.IsNull(Listed());
        Assert.AreEqual(0, _told);
        _live.DidNotReceive().Refresh();
    }
}
