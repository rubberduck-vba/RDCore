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
    private static readonly string AppData = Path.Combine(Path.GetTempPath(), "rdcore-rubberduck-appdata");
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

        public bool ClientPullsConfiguration { get; init; }

        public List<string> Log { get; } = [];

        public Platform(bool clientExpects)
        {
            var environment = Substitute.For<IPlatformEnvironment>();
            environment.UserDataDirectory.Returns(UserFolder);
            var extensions = Substitute.For<IExtensionsProvider>();
            extensions.Discover().Returns([]);
            Settings = new PlatformSettings(new MockFileSystem(), environment, extensions);
            Live.Said.Returns(new Dictionary<string, string>());
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
            var connecting = LanguageClient.From(client =>
            {
                client
                .WithInput(toClient.Reader)
                .WithOutput(toServer.Writer)
                .WithClientCapabilities(new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.ClientCapabilities
                {
                    Workspace = ClientPullsConfiguration ? new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceClientCapabilities { Configuration = true, DidChangeConfiguration = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.DidChangeConfigurationCapability() } : null,
                })
                .WithServices(services => services.AddSingleton(reader).AddSingleton<IPlatformSettings>(Settings).AddSingleton(Live).AddSingleton(Substitute.For<RDCore.CLI.App.Repl.IReplConsole>()).AddSingleton(new LiveSettingsNotifier(_ => Task.CompletedTask)))
                .WithHandler<RubberduckIndenterSettingsHandler>()
                .WithHandler<RubberduckIndenterSettingsApplyHandler>()
                .OnShowMessageRequest(request =>
                {
                    Questions.Add(request.Message);
                    return Task.FromResult(request.Actions!.First(action => action.Title == AnswerWith));
                });

                // a client that answers the server's configuration requests says so, and a server that is asked to wait for its settings waits.
                if (ClientPullsConfiguration)
                {
                    OmniSharp.Extensions.LanguageServer.Protocol.Workspace.ConfigurationExtensions.OnConfiguration(
                        client, (request, _) => Task.FromResult(new Container<Newtonsoft.Json.Linq.JToken>(request.Items.Select(_ => (Newtonsoft.Json.Linq.JToken)Newtonsoft.Json.Linq.JValue.CreateNull()))));
                }
            });

            await Task.WhenAll(serving, connecting);
            Server = await serving;
            _disposables.Add(Server);
            _disposables.Add(await connecting);
        }

        public IndenterSettingsOffering Offering()
            => new(Server, Capabilities, new IndenterSettingsOffer(Markers, IndenterSettingsOffer.MarkerIn(UserFolder)), Live, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500), Log.Add);

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
    public async Task SettingsTheClientSaidBeforeTheOfferingWasMade_AreNotWaitedFor()
    {
        // the client has configuration to pull, and its settings are in already: no change is raised after the offering subscribes.
        await using var platform = new Platform(clientExpects: true) { ClientPullsConfiguration = true };
        platform.Live.HasReceived.Returns(true);
        await platform.StartAsync();

        using var offering = platform.Offering();
        var outcome = await offering.RunAsync(CancellationToken.None);

        Assert.IsNotNull(outcome, "offered at once");
        Assert.IsEmpty(platform.Log);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClientThatNeverSaysItsSettings_IsNotOffered_AndTheLogSaysWhy()
    {
        await using var platform = new Platform(clientExpects: true) { ClientPullsConfiguration = true };
        await platform.StartAsync();

        using var offering = platform.Offering();

        Assert.IsNull(await offering.RunAsync(CancellationToken.None));
        Assert.AreEqual(0, platform.Questions.Count);
        Assert.HasCount(1, platform.Log);
        StringAssert.StartsWith(platform.Log[0], "The client did not say its settings in time", "the line that says why is the timeout's");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AUserWhoSetFormatting_IsNotAsked()
    {
        await using var platform = new Platform(clientExpects: true);
        platform.Live.Said.Returns(new Dictionary<string, string> { ["Formatting:IndentSpaces"] = "3" });
        await platform.StartAsync();

        using var offering = platform.Offering();

        Assert.IsNull(await offering.RunAsync(CancellationToken.None));
        Assert.AreEqual(0, platform.Questions.Count);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClientThatAnswersTheEditorsTabSize_IsOffered_AsTheUserSetNothing()
    {
        // the follower and the offering as the language server starts them, over a client that has configuration to pull and an editor with a tab size.
        var options = new SdkAppOptions();
        var schema = SettingsSchema.Platform;
        var live = new LiveSettings([new LiveSettingsTarget(schema, string.Empty, options)], schema, Path.Combine(Path.GetTempPath(), "rdcore-nowhere", UserSettingsStore.FileName));
        var capabilities = new PlatformClientCapabilitiesService();
        capabilities.Record(new PlatformInitializeParams
        {
            Expected = new CorePlatformClientCapabilities { LanguageServer = new LanguageServerCapabilities { RubberduckIndenterSettings = new RubberduckIndenterSettings(true) } },
        });
        var environment = Substitute.For<IPlatformEnvironment>();
        environment.UserDataDirectory.Returns(UserFolder);
        var extensions = Substitute.For<IExtensionsProvider>();
        extensions.Discover().Returns([]);
        var settings = new PlatformSettings(new MockFileSystem(), environment, extensions);
        var reader = new RubberduckIndenterSettingsReader(
            new MockFileSystem(new Dictionary<string, MockFileData> { [RubberduckIndenterSettingsReader.ConfigPathIn(AppData)] = new(Config) }), AppData, new NoRegistry(), isWindows: true);
        var questions = new List<string>();
        var offering = new TaskCompletionSource<IndenterSettingsOffering>();
        var services = new ServiceCollection().AddSingleton<ILiveSettings>(live).BuildServiceProvider();

        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .WithConfigurationSection(LiveSettingsProtocol.Section)
            .OnStarted((ILanguageServer started, CancellationToken cancellation) =>
            {
                ClientConfigurationFollower.Follow(services, started, () => Task.CompletedTask, _ => { });
                offering.SetResult(new IndenterSettingsOffering(
                    started, capabilities, new IndenterSettingsOffer(new MockFileSystem(), IndenterSettingsOffer.MarkerIn(UserFolder)), live, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), _ => { }));
                return Task.CompletedTask;
            }));
        var connecting = LanguageClient.From(client =>
        {
            client
                .WithInput(toClient.Reader)
                .WithOutput(toServer.Writer)
                .WithClientCapabilities(new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.ClientCapabilities
                {
                    Workspace = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceClientCapabilities
                    {
                        Configuration = true,
                        DidChangeConfiguration = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.DidChangeConfigurationCapability(),
                    },
                })
                .WithServices(s => s.AddSingleton(reader).AddSingleton<IPlatformSettings>(settings).AddSingleton(Substitute.For<ILiveSettings>()).AddSingleton(Substitute.For<RDCore.CLI.App.Repl.IReplConsole>()).AddSingleton(new LiveSettingsNotifier(_ => Task.CompletedTask)))
                .WithHandler<RubberduckIndenterSettingsHandler>()
                .WithHandler<RubberduckIndenterSettingsApplyHandler>()
                .OnShowMessageRequest(request =>
                {
                    questions.Add(request.Message);
                    return Task.FromResult(request.Actions!.First(action => action.Title == IndenterSettingsImportText.Import()));
                });
            OmniSharp.Extensions.LanguageServer.Protocol.Workspace.ConfigurationExtensions.OnConfiguration(client, (request, _) =>
                Task.FromResult(new Container<Newtonsoft.Json.Linq.JToken>(request.Items.Select(item => item.Section == FormattingTabSize.EditorSection
                    ? new Newtonsoft.Json.Linq.JValue(2)
                    : LiveSettingsProtocol.Answer(new ConfigurationParams { Items = new Container<ConfigurationItem>(item) }, new Dictionary<string, string>()).Single()))));
        });

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;
        using var made = await offering.Task;

        var outcome = await made.RunAsync(CancellationToken.None);

        Assert.AreEqual(2, options.Formatting.IndentSpaces, "the editor's tab size is the indentation");
        Assert.IsFalse(live.Said.ContainsKey("Formatting:IndentSpaces"), "which the user did not set");
        Assert.IsNotNull(outcome, "so the offer is made");
        Assert.AreEqual(1, questions.Count);
    }

    [TestMethod]
    public async Task TheApplyRequest_SetsTheSettingsOfAnImport_AndNoOthers()
    {
        var environment = Substitute.For<IPlatformEnvironment>();
        environment.UserDataDirectory.Returns(UserFolder);
        var extensions = Substitute.For<IExtensionsProvider>();
        extensions.Discover().Returns([]);
        var settings = new PlatformSettings(new MockFileSystem(), environment, extensions);
        var handler = new RubberduckIndenterSettingsApplyHandler(settings, Substitute.For<ILiveSettings>(), new LiveSettingsNotifier(_ => Task.CompletedTask));
        var result = await handler.Handle(new RubberduckIndenterSettingsApplyParams
        {
            Source = RubberduckIndenterSettingsSource.RubberduckConfig,
            Settings = new Dictionary<string, string>
            {
                ["Server.Verbose"] = "False",
                ["Environment.AllowDllImports"] = "False",
                ["Platform.Extensions.Allowed"] = "Anything",
                ["Formatting.FormatOnType"] = "False",
                ["Formatting.IndentCase"] = "True",
            },
        }, CancellationToken.None);

        Assert.AreEqual(1, result.Applied);
        CollectionAssert.AreEquivalent(new[] { "Server.Verbose", "Environment.AllowDllImports", "Platform.Extensions.Allowed", "Formatting.FormatOnType" }, result.Refused.ToArray(), "and the client says what it did not take");
        CollectionAssert.AreEqual(new[] { "Formatting.IndentCase" }, settings.Store.Read().Select(value => value.Setting.Key).ToArray(), "a client owns its configuration, and this is no way to write it");
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
