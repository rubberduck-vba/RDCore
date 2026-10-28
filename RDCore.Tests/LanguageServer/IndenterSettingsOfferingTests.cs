using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.CLI.App.Configuration;
using RDCore.LanguageServer.Workspace;
using RDCore.SDK.Client;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Formatting;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Commands;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The first start of the language server offers, once, to import the indenter settings the user had in Rubberduck - to a client that reads them, and when the user
/// never set a <c>Formatting</c> setting - and the command <c>rdcore.importIndenterSettings</c> is refused by a client that does not.
/// </summary>
[TestClass]
public sealed class IndenterSettingsOfferingTests
{
    private const string AppData = @"C:\Users\duck\AppData\Roaming";
    private static readonly string UserFolder = Path.Combine(Path.GetTempPath(), "rdcore-offering-user");

    private const string Config = "<Configuration><UserSettings><IndenterSettings><IndentCase>true</IndentCase><IndentSpaces>2</IndentSpaces></IndenterSettings></UserSettings></Configuration>";

    private sealed class NoRegistry : ILegacyIndenterRegistry
    {
        public IReadOnlyDictionary<string, object>? ReadSmartIndenter() => null;

        public int? ReadTabWidth() => null;
    }

    private sealed class Platform : IAsyncDisposable
    {
        private readonly List<IDisposable> _disposables = [];

        public MockFileSystem Markers { get; } = new();

        public PlatformSettings Settings { get; }

        public List<string> Questions { get; } = [];

        public string AnswerWith { get; set; } = IndenterSettingsImportText.Import();

        public ILiveSettings Live { get; } = Substitute.For<ILiveSettings>();

        public ILanguageServer Server { get; private set; } = null!;

        public IPlatformClientCapabilitiesService Capabilities { get; } = new PlatformClientCapabilitiesService();

        public Platform(bool clientExpects)
        {
            var environment = Substitute.For<IPlatformEnvironment>();
            environment.UserDataDirectory.Returns(UserFolder);
            var extensions = Substitute.For<IExtensionsProvider>();
            extensions.Discover().Returns([]);
            Settings = new PlatformSettings(new MockFileSystem(), environment, extensions);
            Live.Current.Returns(new Dictionary<string, string>());
            Capabilities.Record(new PlatformInitializeParams
            {
                Expected = new CorePlatformClientCapabilities { LanguageServer = new LanguageServerCapabilities { RubberduckIndenterSettings = new RubberduckIndenterSettings(clientExpects) } },
            });
        }

        public async Task StartAsync()
        {
            var reader = new RubberduckIndenterSettingsReader(
                new MockFileSystem(new Dictionary<string, MockFileData> { [RubberduckIndenterSettingsReader.ConfigPathIn(AppData)] = new(Config) }), AppData, new NoRegistry(), isWindows: true);
            var toServer = new Pipe();
            var toClient = new Pipe();
            var serving = OmniSharpLanguageServer.From(server => server.WithInput(toServer.Reader).WithOutput(toClient.Writer));
            var connecting = LanguageClient.From(client => client
                .WithInput(toClient.Reader)
                .WithOutput(toServer.Writer)
                .WithServices(services => services.AddSingleton(reader).AddSingleton<IPlatformSettings>(Settings).AddSingleton(Live).AddSingleton(new LiveSettingsNotifier(_ => Task.CompletedTask)))
                .WithHandler<RubberduckIndenterSettingsHandler>()
                .WithHandler<RubberduckIndenterSettingsApplyHandler>()
                .OnShowMessageRequest(request =>
                {
                    Questions.Add(request.Message);
                    return Task.FromResult(request.Actions!.First(action => action.Title == AnswerWith));
                }));

            await Task.WhenAll(serving, connecting);
            Server = await serving;
            _disposables.Add(Server);
            _disposables.Add(await connecting);
        }

        public IndenterSettingsOffering Offering()
            => new(Server, Capabilities, new IndenterSettingsOffer(Markers, IndenterSettingsOffer.MarkerIn(UserFolder)), Live, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        public ValueTask DisposeAsync()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheFirstStart_AsksOnce_AndTheClientStoresWhatTheUserSaidYesTo()
    {
        await using var platform = new Platform(clientExpects: true);
        await platform.StartAsync();

        using var offering = platform.Offering();
        var outcome = await offering.RunAsync(CancellationToken.None);

        Assert.AreEqual(2, outcome!.Applied);
        Assert.AreEqual(1, platform.Questions.Count);
        StringAssert.Contains(platform.Questions[0], IndenterSettingsImportText.SourceName(RubberduckIndenterSettingsSource.RubberduckConfig));
        var set = platform.Settings.Store.Read().ToDictionary(value => value.Setting.Key, value => value.Value);
        Assert.AreEqual("True", set["Formatting.IndentCase"]);
        Assert.AreEqual("2", set["Formatting.IndentSpaces"]);

        using var next = platform.Offering();
        Assert.IsNull(await next.RunAsync(CancellationToken.None), "the next start");
        Assert.AreEqual(1, platform.Questions.Count);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AUserWhoSaysNo_IsNotAskedAgain_AndNothingIsStored()
    {
        await using var platform = new Platform(clientExpects: true) { AnswerWith = IndenterSettingsImportText.NotNow() };
        await platform.StartAsync();

        using var offering = platform.Offering();

        Assert.IsNull(await offering.RunAsync(CancellationToken.None));
        Assert.AreEqual(0, platform.Settings.Store.Read().Count);
        using var next = platform.Offering();
        Assert.IsNull(await next.RunAsync(CancellationToken.None), "the next start");
        Assert.AreEqual(1, platform.Questions.Count);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClientThatDoesNotReadRubberduck_IsNeverAsked()
    {
        await using var platform = new Platform(clientExpects: false);
        await platform.StartAsync();

        using var offering = platform.Offering();

        Assert.IsNull(await offering.RunAsync(CancellationToken.None));
        Assert.AreEqual(0, platform.Questions.Count);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AUserWhoSetFormatting_IsNotAsked()
    {
        await using var platform = new Platform(clientExpects: true);
        platform.Live.Current.Returns(new Dictionary<string, string> { ["Formatting:IndentSpaces"] = "3" });
        await platform.StartAsync();

        using var offering = platform.Offering();

        Assert.IsNull(await offering.RunAsync(CancellationToken.None));
        Assert.AreEqual(0, platform.Questions.Count);
    }

    [TestMethod]
    public async Task TheCommand_IsRefused_ByAClientThatDoesNotExpectIt()
    {
        var capabilities = new PlatformClientCapabilitiesService();
        capabilities.Record(new PlatformInitializeParams { Expected = new CorePlatformClientCapabilities { LanguageServer = new LanguageServerCapabilities() } });
        var sut = new ImportIndenterSettingsCommand(Substitute.For<ILanguageServerFacade>(), capabilities);

        var refused = await Assert.ThrowsAsync<ServerCommandRefusedException>(() => sut.ExecuteAsync(null, Substitute.For<IServerCommandContext>(), CancellationToken.None));

        Assert.AreEqual(IndenterSettingsImportText.Unsupported(), refused.Message);
    }
}
