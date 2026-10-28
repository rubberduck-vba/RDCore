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
/// <c>Formatting.IndentSpaces</c> and the tab size of the client's editor are one setting said twice, through the configuration the client owns: an indentation the
/// user never set is the editor's tab size, and one the user set is not.
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

        var settings = FormattingTabSize.WithEditorTabSize(said, 2);

        Assert.AreEqual("2", settings["Formatting:IndentSpaces"]);
        Assert.AreEqual("True", settings["Server:Verbose"]);
        Assert.IsFalse(said.ContainsKey("Formatting:IndentSpaces"), "what the client said is not changed");
    }

    [TestMethod]
    public void AnIndentationTheUserSet_IsNotTheEditorsTabSize_WhateverItIs()
    {
        var said = new Dictionary<string, string> { ["Formatting:IndentSpaces"] = "3" };

        Assert.AreSame(said, FormattingTabSize.WithEditorTabSize(said, 8));
    }

    [TestMethod]
    public void AnEditorWithNoTabSize_ChangesNothing()
    {
        var said = new Dictionary<string, string>();

        Assert.AreSame(said, FormattingTabSize.WithEditorTabSize(said, null));
    }

    // a platform component: a server with its client, whose answers set the live settings through the follower the server apps use.
    private sealed class Wire : IAsyncDisposable
    {
        private readonly SemaphoreSlim _followed = new(0);
        private LanguageClient? _client;
        private OmniSharpLanguageServer? _server;

        public SdkAppOptions Options { get; } = new();

        public LiveSettings Live { get; }

        public ClientConfigurationFollower Follower { get; private set; } = null!;

        /// <summary>The indentation as each listener of the change saw it.</summary>
        public List<int> Seen { get; } = [];

        public Dictionary<string, string> Said { get; } = [];

        public int TabSize { get; set; } = 2;

        /// <summary>Called with each request for the tab size, before it is answered.</summary>
        public Func<int, Task>? BeforeAnswering { get; set; }

        private int _tabSizeRequests;

        public Wire()
        {
            var schema = SettingsSchema.Platform;
            Live = new LiveSettings([new LiveSettingsTarget(schema, string.Empty, Options)], schema, Path.Combine(Path.GetTempPath(), "rdcore-nowhere", UserSettingsStore.FileName));
            Live.Changed += (_, _) => Seen.Add(Options.Formatting.IndentSpaces);
        }

        public async Task StartAsync(bool follow)
        {
            var toServer = new Pipe();
            var toClient = new Pipe();
            var serving = OmniSharpLanguageServer.From(server => server
                .WithInput(toServer.Reader)
                .WithOutput(toClient.Writer)
                .WithConfigurationSection(LiveSettingsProtocol.Section)
                .OnStarted((ILanguageServer started, CancellationToken cancellation) =>
                {
                    Follower = new ClientConfigurationFollower(started, Live, Options.Formatting, () =>
                    {
                        _followed.Release();
                        return Task.CompletedTask;
                    }, _ => { });
                    if (follow)
                    {
                        Follower.Start();
                    }

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

        public Task<bool> FollowedAsync() => _followed.WaitAsync(TimeSpan.FromSeconds(20));

        public void ClientSaysConfigurationChanged() => _client!.DidChangeConfiguration(LiveSettingsProtocol.Changed);

        public async ValueTask DisposeAsync()
        {
            Follower?.Dispose();
            _client?.Dispose();
            _server?.Dispose();
            await Task.CompletedTask;
        }
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AServer_AsksForTheEditorsTabSize_WithTheLiveSettings_AndFollowsItWhenItChanges()
    {
        await using var wire = new Wire();
        await wire.StartAsync(follow: true);

        Assert.IsTrue(await wire.FollowedAsync(), "asked once it is initialized");
        Assert.AreEqual(2, wire.Options.Formatting.IndentSpaces, "the editor's tab size, as the user set no indentation");

        // the user changes the tab size of the editor: a change of configuration like any other.
        wire.TabSize = 8;
        wire.ClientSaysConfigurationChanged();
        Assert.IsTrue(await wire.FollowedAsync());
        Assert.AreEqual(8, wire.Options.Formatting.IndentSpaces, "followed when it changes");

        // and the user sets the indentation: it wins.
        wire.Said["Formatting:IndentSpaces"] = "3";
        wire.ClientSaysConfigurationChanged();
        Assert.IsTrue(await wire.FollowedAsync());
        Assert.AreEqual(3, wire.Options.Formatting.IndentSpaces, "the setting wins over the editor's tab size");

        CollectionAssert.AreEqual(new[] { 2, 8, 3 }, wire.Seen, "whoever listens sees the indentation the settings end up with, never the default in between");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AChangeThatArrivesWhileTheEditorIsAsked_HasTheLastWord()
    {
        await using var wire = new Wire();
        await wire.StartAsync(follow: false);
        var firstAsked = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        wire.BeforeAnswering = async number =>
        {
            if (number == 1)
            {
                firstAsked.SetResult();
                await release.Task;
            }
        };

        var older = wire.Follower.FollowAsync();
        await firstAsked.Task.WaitAsync(TimeSpan.FromSeconds(20));
        wire.TabSize = 8;
        var newer = await wire.Follower.FollowAsync();
        release.SetResult();

        Assert.IsTrue(newer);
        Assert.IsFalse(await older, "the older change was not set");
        Assert.AreEqual(8, wire.Options.Formatting.IndentSpaces);
        CollectionAssert.AreEqual(new[] { 8 }, wire.Seen);
    }
}
