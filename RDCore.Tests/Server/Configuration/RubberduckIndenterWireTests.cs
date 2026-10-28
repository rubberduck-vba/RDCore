using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.CLI.App.Configuration;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Formatting;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Server.Configuration;

/// <summary>
/// The language server asks its client for the indenter settings its user had in Rubberduck, maps what the client read, and asks the client to take the mapped settings as
/// its own - over the wire, with the handlers of the shell.
/// </summary>
[TestClass]
public sealed class RubberduckIndenterWireTests
{
    private const string AppData = @"C:\Users\duck\AppData\Roaming";

    private static readonly string UserFolder = Path.Combine(Path.GetTempPath(), "rdcore-import-wire-user");

    private const string Config = """
        <Configuration><UserSettings><IndenterSettings>
          <IndentCase>true</IndentCase>
          <EndOfLineCommentStyle>SameGap</EndOfLineCommentStyle>
          <IndentSpaces>2</IndentSpaces>
        </IndenterSettings></UserSettings></Configuration>
        """;

    private sealed class NoRegistry : ILegacyIndenterRegistry
    {
        public IReadOnlyDictionary<string, object>? ReadSmartIndenter() => null;

        public int? ReadTabWidth() => null;
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheClientReadsRubberduck_TheServerMapsIt_AndTheClientStoresWhatTheServerMapped()
    {
        var rubberduckFiles = new MockFileSystem(new Dictionary<string, MockFileData> { [RubberduckIndenterSettingsReader.ConfigPathIn(AppData)] = new(Config) });
        var reader = new RubberduckIndenterSettingsReader(rubberduckFiles, AppData, new NoRegistry(), isWindows: true);

        var userFiles = new MockFileSystem();
        var environment = Substitute.For<IPlatformEnvironment>();
        environment.UserDataDirectory.Returns(UserFolder);
        var extensions = Substitute.For<IExtensionsProvider>();
        extensions.Discover().Returns([]);
        var settings = new PlatformSettings(userFiles, environment, extensions);
        var live = Substitute.For<ILiveSettings>();
        var told = 0;
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server.WithInput(toServer.Reader).WithOutput(toClient.Writer));
        var connecting = LanguageClient.From(client => client
            .WithInput(toClient.Reader)
            .WithOutput(toServer.Writer)
            .WithServices(services => services.AddSingleton(reader).AddSingleton<IPlatformSettings>(settings).AddSingleton(live).AddSingleton(new LiveSettingsNotifier(_ =>
            {
                told++;
                return Task.CompletedTask;
            })))
            .WithHandler<RubberduckIndenterSettingsHandler>()
            .WithHandler<RubberduckIndenterSettingsApplyHandler>());

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;

        var outcome = await IndenterSettingsImportFlow.ImportAsync(
            token => server.SendRequest(new RubberduckIndenterSettingsParams(), token),
            (request, token) => server.SendRequest(request, token),
            CancellationToken.None);

        Assert.AreEqual(RubberduckIndenterSettingsSource.RubberduckConfig, outcome.Source);
        Assert.AreEqual(3, outcome.Found);
        Assert.AreEqual(3, outcome.Applied);
        var set = settings.Store.Read().ToDictionary(value => value.Setting.Key, value => value.Value);
        Assert.AreEqual("True", set["Formatting.IndentCase"]);
        Assert.AreEqual("SameGap", set["Formatting.EndOfLineCommentStyle"]);
        Assert.AreEqual("2", set["Formatting.IndentSpaces"]);
        live.Received(1).Refresh();
        Assert.AreEqual(1, told, "the client says its settings changed, as it does for any change");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClientOnAnotherPlatform_AnswersThatItFoundNothing_AndIsAskedToApplyNothing()
    {
        var reader = new RubberduckIndenterSettingsReader(new MockFileSystem(), AppData, new NoRegistry(), isWindows: false);

        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server.WithInput(toServer.Reader).WithOutput(toClient.Writer));
        var connecting = LanguageClient.From(client => client
            .WithInput(toClient.Reader)
            .WithOutput(toServer.Writer)
            .WithServices(services => services.AddSingleton(reader))
            .WithHandler<RubberduckIndenterSettingsHandler>());

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;

        var outcome = await IndenterSettingsImportFlow.ImportAsync(
            token => server.SendRequest(new RubberduckIndenterSettingsParams(), token),
            (_, _) => throw new AssertFailedException("nothing was found, so nothing is applied"),
            CancellationToken.None);

        Assert.AreEqual(RubberduckIndenterSettingsSource.None, outcome.Source);
        Assert.AreEqual(IndenterSettingsImportText.Nothing(), outcome.Message);
    }
}
