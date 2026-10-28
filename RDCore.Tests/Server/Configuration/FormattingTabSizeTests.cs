using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Formatting;
using RDCore.SDK.Server.Configuration;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Server.Configuration;

/// <summary>
/// <c>Formatting.IndentSpaces</c> is the tab size of the client's editor when the user never set one, through the configuration the client owns: the server is told
/// the client's configuration changed, and asks for it.
/// </summary>
[TestClass]
public sealed class FormattingTabSizeTests
{
    [TestMethod]
    [DataRow(2, 2)]
    [DataRow("8", 8)]
    [DataRow(32, 32)]
    [DataRow(0, null)]
    [DataRow(33, null)]
    [DataRow(-4, null)]
    [DataRow("tab", null)]
    public void TheEditorsTabSize_IsAnIndentationTheFormattingCanHave(object said, int? tabSize)
        => Assert.AreEqual(tabSize, FormattingTabSize.EditorTabSizeOf(new JValue(said)));

    [TestMethod]
    public void AClientThatSaysNoTabSize_HasNone()
    {
        Assert.IsNull(FormattingTabSize.EditorTabSizeOf(null));
        Assert.IsNull(FormattingTabSize.EditorTabSizeOf(JValue.CreateNull()));
        Assert.IsNull(FormattingTabSize.EditorTabSizeOf(new JObject()));
    }

    [TestMethod]
    public void AnIndentationTheUserNeverSet_IsTheEditorsTabSize()
    {
        var said = new Dictionary<string, string> { ["Server:Verbose"] = "True" };

        var adopted = FormattingTabSize.AdoptedFrom(said, 2);

        Assert.AreEqual("2", adopted["Formatting:IndentSpaces"]);
        Assert.AreEqual(1, adopted.Count, "and nothing else");
        Assert.IsFalse(said.ContainsKey("Formatting:IndentSpaces"), "what the client said is not changed");
    }

    [TestMethod]
    public void AnIndentationTheUserSet_IsNotTheEditorsTabSize_WhateverItIs()
    {
        var said = new Dictionary<string, string> { ["Formatting:IndentSpaces"] = "3" };

        Assert.AreEqual(0, FormattingTabSize.AdoptedFrom(said, 8).Count);
    }

    [TestMethod]
    public void AnEditorWithNoTabSize_ChangesNothing()
    {
        var said = new Dictionary<string, string>();

        Assert.AreEqual(0, FormattingTabSize.AdoptedFrom(said, null).Count);
    }

    [TestMethod]
    public void AChangeThatIsNoLongerCurrent_SetsNothing_AndSaysNothingChanged()
    {
        var options = new SdkAppOptions();
        var schema = SettingsSchema.Platform;
        var live = new LiveSettings([new LiveSettingsTarget(schema, string.Empty, options)], schema, Path.Combine(Path.GetTempPath(), "rdcore-nowhere", UserSettingsStore.FileName));
        var changed = 0;
        live.Changed += (_, _) => changed++;
        var said = new Dictionary<string, string> { ["Server:Verbose"] = "True" };

        Assert.IsFalse(live.Apply(said, new Dictionary<string, string>(), () => false));
        Assert.IsFalse(options.Server.Verbose);
        Assert.IsFalse(live.HasReceived);
        Assert.AreEqual(0, changed);

        Assert.IsTrue(live.Apply(said, new Dictionary<string, string> { ["Formatting:IndentSpaces"] = "2" }, () => true));
        Assert.IsTrue(options.Server.Verbose);
        Assert.AreEqual(1, changed);
        Assert.IsFalse(live.Said.ContainsKey("Formatting:IndentSpaces"), "what the client said");
        Assert.AreEqual("2", live.Current["Formatting:IndentSpaces"], "and what is handed down, from the same change");
    }

    // a platform component: a server with its client, followed by the follower a server app starts when it is started (ClientConfigurationFollower.Follow).
    private sealed class Wire : IAsyncDisposable
    {
        private readonly SemaphoreSlim _followed = new(0);
        private LanguageClient? _client;
        private OmniSharpLanguageServer? _server;

        public SdkAppOptions Options { get; } = new();

        public LiveSettings Live { get; }

        public ClientConfigurationFollower? Follower { get; private set; }

        /// <summary>The indentation as each listener of the change saw it.</summary>
        public List<int> Seen { get; } = [];

        public Dictionary<string, string> Said { get; } = [];

        public int TabSize { get; set; } = 2;

        /// <summary>Called with the number of each request for the tab size, before it is answered.</summary>
        public Func<int, Task>? BeforeAnswering { get; set; }

        private int _tabSizeRequests;

        public Wire()
        {
            var schema = SettingsSchema.Platform;
            Live = new LiveSettings([new LiveSettingsTarget(schema, string.Empty, Options)], schema, Path.Combine(Path.GetTempPath(), "rdcore-nowhere", UserSettingsStore.FileName));
            Live.Changed += (_, _) => Seen.Add(Options.Formatting.IndentSpaces);
        }

        public async Task StartAsync()
        {
            var services = new ServiceCollection().AddSingleton<ILiveSettings>(Live).BuildServiceProvider();
            var toServer = new Pipe();
            var toClient = new Pipe();
            var serving = OmniSharpLanguageServer.From(server => server
                .WithInput(toServer.Reader)
                .WithOutput(toClient.Writer)
                .WithConfigurationSection(LiveSettingsProtocol.Section)
                .OnStarted((ILanguageServer started, CancellationToken cancellation) =>
                {
                    Follower = ClientConfigurationFollower.Follow(services, started, () =>
                    {
                        _followed.Release();
                        return Task.CompletedTask;
                    }, _ => { });
                    return Task.CompletedTask;
                }));

            var connecting = LanguageClient.From(client =>
            {
                client
                    .WithInput(toClient.Reader)
                    .WithOutput(toServer.Writer)
                    .WithClientCapabilities(new ClientCapabilities
                    {
                        Workspace = new WorkspaceClientCapabilities { Configuration = true, DidChangeConfiguration = new DidChangeConfigurationCapability() },
                    });
                OmniSharp.Extensions.LanguageServer.Protocol.Workspace.ConfigurationExtensions.OnConfiguration(client, async (request, _) =>
                {
                    var answer = LiveSettingsProtocol.Answer(request, Said);
                    if (request.Items.Any(item => item.Section == FormattingTabSize.EditorSection))
                    {
                        // the editor's tab size is what it is when it is asked, and the answer comes when the test lets it.
                        var number = Interlocked.Increment(ref _tabSizeRequests);
                        var tabSize = TabSize;
                        if (BeforeAnswering is not null)
                        {
                            await BeforeAnswering(number);
                        }

                        return new Container<JToken>(request.Items.Zip(answer, (item, token) => item.Section == FormattingTabSize.EditorSection ? new JValue(tabSize) : token));
                    }

                    return answer;
                });
            });

            await Task.WhenAll(serving, connecting);
            _server = await serving;
            _client = await connecting;
        }

        public Task<bool> FollowedAsync(TimeSpan? wait = null) => _followed.WaitAsync(wait ?? TimeSpan.FromSeconds(20));

        public void ClientSaysConfigurationChanged() => _client!.DidChangeConfiguration(LiveSettingsProtocol.Changed);

        public ValueTask DisposeAsync()
        {
            Follower?.Dispose();
            _client?.Dispose();
            _server?.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AServer_AsksForTheEditorsTabSize_WithTheLiveSettings_AndFollowsItWhenItChanges()
    {
        await using var wire = new Wire();
        await wire.StartAsync();

        Assert.IsTrue(await wire.FollowedAsync(), "asked once it is initialized");
        Assert.AreEqual(2, wire.Options.Formatting.IndentSpaces, "the editor's tab size, as the user set no indentation");
        Assert.IsTrue(wire.Live.HasReceived);
        Assert.IsFalse(wire.Live.Said.ContainsKey("Formatting:IndentSpaces"), "and the user did not set it, whatever the settings hand down");
        Assert.AreEqual("2", wire.Live.Current["Formatting:IndentSpaces"]);

        // the user changes the tab size of the editor: a change of configuration like any other.
        wire.TabSize = 8;
        wire.ClientSaysConfigurationChanged();
        Assert.IsTrue(await wire.FollowedAsync());
        Assert.AreEqual(8, wire.Options.Formatting.IndentSpaces, "followed when it changes");

        // and the user sets the indentation: it is what the server has.
        wire.Said["Formatting:IndentSpaces"] = "3";
        wire.ClientSaysConfigurationChanged();
        Assert.IsTrue(await wire.FollowedAsync());
        Assert.AreEqual(3, wire.Options.Formatting.IndentSpaces, "the setting is not the editor's tab size");

        CollectionAssert.AreEqual(new[] { 2, 8, 3 }, wire.Seen, "whoever listens sees the indentation the settings end up with, never the default in between");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task OfTwoChangesThatOverlap_TheNewerHasTheLastWord()
    {
        await using var wire = new Wire();
        await wire.StartAsync();
        Assert.IsTrue(await wire.FollowedAsync());

        // the second change of the editor's tab size is asked while the first is held back; the first answer, 5, comes after the second, 8.
        var firstAsked = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        wire.BeforeAnswering = async number =>
        {
            if (number == 2)
            {
                firstAsked.SetResult();
                await release.Task;
            }
        };
        wire.TabSize = 5;
        var older = wire.Follower!.FollowAsync();
        await firstAsked.Task.WaitAsync(TimeSpan.FromSeconds(20));
        wire.TabSize = 8;
        var newer = await wire.Follower.FollowAsync();

        release.SetResult();

        Assert.IsTrue(newer);
        Assert.IsFalse(await older.WaitAsync(TimeSpan.FromSeconds(20)), "the older change was dropped, whenever its answer came");
        Assert.AreEqual(8, wire.Options.Formatting.IndentSpaces, "the older answer did not replace the newer one");
        CollectionAssert.AreEqual(new[] { 2, 8 }, wire.Seen, "and no listener saw it");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task OfTwoChangesTheClientSays_TheNewerHasTheLastWord_WhateverOrderTheyAreAnswered()
    {
        await using var wire = new Wire();
        await wire.StartAsync();
        Assert.IsTrue(await wire.FollowedAsync());

        // the client says its configuration changed twice; the editor's answer to the first, 5, comes after the answer to the second, 8.
        var firstAsked = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        wire.BeforeAnswering = async number =>
        {
            if (number == 2)
            {
                firstAsked.SetResult();
                await release.Task;
            }
        };
        wire.TabSize = 5;
        wire.ClientSaysConfigurationChanged();
        await firstAsked.Task.WaitAsync(TimeSpan.FromSeconds(20));
        wire.TabSize = 8;
        wire.ClientSaysConfigurationChanged();
        Assert.IsTrue(await wire.FollowedAsync(), "the newer change was followed");

        release.SetResult();

        Assert.IsFalse(await wire.FollowedAsync(TimeSpan.FromSeconds(1)), "and the older one, answered last, was not");
        Assert.AreEqual(8, wire.Options.Formatting.IndentSpaces);
        CollectionAssert.AreEqual(new[] { 2, 8 }, wire.Seen);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AnErrorAnswer_ForTheEditorsTabSize_IsNoTabSize_AndTheRestOfTheSettingsAreStillSet()
    {
        await using var wire = new Wire();
        await wire.StartAsync();
        Assert.IsTrue(await wire.FollowedAsync());
        Assert.AreEqual(2, wire.Options.Formatting.IndentSpaces);

        // the editor has no such setting: the client answers the request with an error.
        wire.BeforeAnswering = _ => throw new InvalidOperationException("no such setting");
        wire.Said["Server:Verbose"] = "True";
        wire.ClientSaysConfigurationChanged();
        Assert.IsTrue(await wire.FollowedAsync(), "the change was followed");

        Assert.IsTrue(wire.Options.Server.Verbose, "what the client said of the other settings is set");
        Assert.AreEqual(4, wire.Options.Formatting.IndentSpaces, "and the indentation is its own, as no setting and no editor says another");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AFollowerThatIsDisposed_WhileTheEditorIsAsked_SetsNothing()
    {
        await using var wire = new Wire();
        await wire.StartAsync();
        Assert.IsTrue(await wire.FollowedAsync());

        var asked = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        wire.BeforeAnswering = async number =>
        {
            asked.SetResult();
            await release.Task;
        };
        var following = wire.Follower!.FollowAsync();
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(20));

        wire.Follower.Dispose();
        release.SetResult();

        Assert.IsFalse(await following, "a server that is going away is not a client whose editor has no tab size");
        Assert.AreEqual(2, wire.Options.Formatting.IndentSpaces, "the indentation was not set again, to the default or to anything");
        CollectionAssert.AreEqual(new[] { 2 }, wire.Seen);
    }
}
