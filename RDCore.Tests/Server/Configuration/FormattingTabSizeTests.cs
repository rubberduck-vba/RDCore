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
using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
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

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public void TheChangesOfTheSettings_AreToldInTheOrderTheyWereMade_AndNeverAtOnce()
    {
        var options = new SdkAppOptions();
        var schema = SettingsSchema.Platform;
        var live = new LiveSettings([new LiveSettingsTarget(schema, string.Empty, options)], schema, Path.Combine(Path.GetTempPath(), "rdcore-nowhere", UserSettingsStore.FileName));

        // the listener of the first change is held while the second is made on another thread.
        var inListener = 0;
        var most = 0;
        var told = new List<bool>();
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var released = true;
        live.Changed += (_, _) =>
        {
            most = Math.Max(most, Interlocked.Increment(ref inListener));
            lock (told)
            {
                told.Add(options.Server.Verbose);
            }

            if (told.Count == 1)
            {
                holding.Set();
                released &= release.Wait(TimeSpan.FromSeconds(20));
            }

            Interlocked.Decrement(ref inListener);
        };

        var first = new Thread(() => live.Apply(new Dictionary<string, string> { ["Server:Verbose"] = "False" }));
        first.Start();
        Assert.IsTrue(holding.Wait(TimeSpan.FromSeconds(20)), "the first change is being told");

        var secondDone = live.Apply(new Dictionary<string, string> { ["Server:Verbose"] = "True" }, new Dictionary<string, string>(), () => true);
        Assert.IsTrue(secondDone, "the second change was set while the first was being told, and its call returned");
        release.Set();
        first.Join();

        Assert.IsTrue(released, "the listener of the first change was let go");
        Assert.AreEqual(2, told.Count, "each change is told");
        Assert.AreEqual(1, most, "never by two threads at once");
        Assert.IsTrue(options.Server.Verbose);
        Assert.IsTrue(told[1], "the second change is told after the first");
    }

    private static LiveSettings LiveOf(SdkAppOptions options, Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        var schema = SettingsSchema.Platform;
        return new LiveSettings([new LiveSettingsTarget(schema, string.Empty, options)], schema, Path.Combine(Path.GetTempPath(), "rdcore-nowhere", UserSettingsStore.FileName), logger);
    }

    // what a component logs, kept: the error lines, with the exceptions they carry.
    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<(string Message, Exception? Exception)> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Error)
            {
                lock (Errors)
                {
                    Errors.Add((formatter(state, exception), exception));
                }
            }
        }
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AListenerThatThrows_DoesNotKeepTheOthersFromBeingTold_NorStrandTheChangesBehindIt()
    {
        var options = new SdkAppOptions();
        var logger = new CapturingLogger();
        var live = LiveOf(options, logger);

        // the first listener holds the thread that tells the first change, and throws once it is let go; the second change is made meanwhile, on another thread.
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var released = true;
        var calls = 0;
        live.Changed += (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                holding.Set();
                released &= release.Wait(TimeSpan.FromSeconds(20));
            }

            throw new InvalidOperationException("a listener that fails");
        };
        var told = new List<bool>();
        live.Changed += (_, _) =>
        {
            lock (told)
            {
                told.Add(options.Server.Verbose);
            }
        };

        var first = Task.Run(() => live.Apply(new Dictionary<string, string> { ["Server:Verbose"] = "False" }));
        Assert.IsTrue(holding.Wait(TimeSpan.FromSeconds(20)), "the first change is being told");
        var second = live.ApplyAsync(new Dictionary<string, string> { ["Server:Verbose"] = "True" }, new Dictionary<string, string>(), () => true);
        release.Set();

        await first.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.IsTrue(await second.WaitAsync(TimeSpan.FromSeconds(20)), "the call that made the change behind it is not left waiting");
        Assert.IsTrue(released, "the failing listener was let go");
        Assert.HasCount(2, told, "both changes were told to the listener that does not fail");
        Assert.HasCount(2, logger.Errors, "the failing listener is logged once for each change it failed on");
        Assert.IsTrue(logger.Errors.All(error => error.Exception is InvalidOperationException { Message: "a listener that fails" }), "with the exception it threw");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AChange_IsToldOnceItsCallCompletes_WhicheverThreadTellsIt()
    {
        var options = new SdkAppOptions();
        var live = LiveOf(options);
        var told = 0;
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var released = true;
        live.Changed += (_, _) =>
        {
            if (Interlocked.Increment(ref told) == 1)
            {
                holding.Set();
                released &= release.Wait(TimeSpan.FromSeconds(20));
            }
        };

        var first = Task.Run(() => live.Apply(new Dictionary<string, string> { ["Server:Verbose"] = "False" }));
        Assert.IsTrue(holding.Wait(TimeSpan.FromSeconds(20)), "the first change is being told, and the thread that tells it is held");

        // the second change is made while that is so: its settings are set, and nobody has told its listeners.
        var second = live.ApplyAsync(new Dictionary<string, string> { ["Server:Verbose"] = "True" }, new Dictionary<string, string>(), () => true);
        Assert.IsTrue(options.Server.Verbose, "its settings are set");
        Assert.IsFalse(second.IsCompleted, "but its call is not done: its listeners were not told");
        Assert.AreEqual(1, told);

        release.Set();
        Assert.IsTrue(await second.WaitAsync(TimeSpan.FromSeconds(20)));
        await first.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.IsTrue(released, "the listener of the first change was let go");
        Assert.AreEqual(2, told, "and now they were");
    }

    [TestMethod]
    public async Task AChange_WithNoListener_CompletesAtOnce_AndIsSet()
    {
        var options = new SdkAppOptions();
        var live = LiveOf(options);

        Assert.IsTrue(await live.ApplyAsync(new Dictionary<string, string> { ["Server:Verbose"] = "True" }, new Dictionary<string, string>(), () => true).WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.IsTrue(options.Server.Verbose);
        Assert.IsTrue(live.HasReceived);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AListenerThatChangesTheSettingsItself_IsToldOfThatChange_AfterItReturns_AndNothingWaits()
    {
        var options = new SdkAppOptions();
        var live = LiveOf(options);
        var told = new List<bool>();
        Task<bool>? inner = null;
        live.Changed += (_, _) =>
        {
            told.Add(options.Server.Verbose);
            if (told.Count == 1)
            {
                // on the thread that tells the change: the call returns at once, and the listener is told of this change once it has returned from the first.
                inner = live.ApplyAsync(new Dictionary<string, string> { ["Server:Verbose"] = "False" }, new Dictionary<string, string>(), () => true);
                told.Add(inner.IsCompleted);
            }
        };

        Assert.IsTrue(await live.ApplyAsync(new Dictionary<string, string> { ["Server:Verbose"] = "True" }, new Dictionary<string, string>(), () => true).WaitAsync(TimeSpan.FromSeconds(20)));

        Assert.IsNotNull(inner);
        Assert.IsTrue(await inner.WaitAsync(TimeSpan.FromSeconds(20)), "the change the listener made was told, and its call completed");
        CollectionAssert.AreEqual(new[] { true, false, false }, told, "told of the first (verbose), the re-entrant call was pending, then told of the second (not verbose)");
    }

    [TestMethod]
    public async Task AChangeThatIsNotCurrent_IsNotToldAndItsCallCompletesAtOnce()
    {
        var live = LiveOf(new SdkAppOptions());
        var told = 0;
        live.Changed += (_, _) => told++;

        Assert.IsFalse(await live.ApplyAsync(new Dictionary<string, string>(), new Dictionary<string, string>(), () => false));
        Assert.AreEqual(0, told);
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

        /// <summary>
        /// The indentation as each listener of the change saw it, each time it saw another: a server that follows its configuration twice as it starts is
        /// told the same settings twice.
        /// </summary>
        public List<int> Seen { get; } = [];

        public Dictionary<string, string> Said { get; } = [];

        /// <summary>What the follower said happened, in the order it said it.</summary>
        public List<string> Log { get; } = [];

        private readonly SemaphoreSlim _logged = new(0);

        /// <summary>Waits for the follower to say something it has not been waited for.</summary>
        public Task<bool> LoggedAsync() => _logged.WaitAsync(TimeSpan.FromSeconds(20));

        /// <summary>Waits until what the follower said is what <paramref name="condition"/> asks for, each time it says something.</summary>
        public async Task<bool> LoggedUntilAsync(Func<IReadOnlyList<string>, bool> condition)
        {
            while (true)
            {
                lock (Log)
                {
                    if (condition(Log))
                    {
                        return true;
                    }
                }

                if (!await LoggedAsync())
                {
                    return false;
                }
            }
        }

        /// <summary>How many of the lines the follower said so far contain <paramref name="text"/>.</summary>
        public int LinesWith(string text)
        {
            lock (Log)
            {
                return Log.Count(line => line.Contains(text, StringComparison.Ordinal));
            }
        }

        public int TabSize { get; set; } = 2;

        /// <summary>
        /// Called with the number of each request for the tab size, and the tab size it is answered with, before it is answered. A server may pull its
        /// configuration more than once as it starts, so a test that holds an answer back tells it by the tab size it set, not by its number.
        /// </summary>
        public Func<int, int, Task>? BeforeAnswering { get; set; }

        private int _tabSizeRequests;

        private readonly bool _declaresDidChangeConfiguration;

        /// <param name="declaresDidChangeConfiguration">
        /// Whether the client declares <c>workspace.didChangeConfiguration</c> as well as <c>workspace.configuration</c>; a client may send the notification
        /// without declaring that it registers for it dynamically.
        /// </param>
        public Wire(bool declaresDidChangeConfiguration = true)
        {
            _declaresDidChangeConfiguration = declaresDidChangeConfiguration;
            var schema = SettingsSchema.Platform;
            Live = new LiveSettings([new LiveSettingsTarget(schema, string.Empty, Options)], schema, Path.Combine(Path.GetTempPath(), "rdcore-nowhere", UserSettingsStore.FileName));
            Live.Changed += (_, _) =>
            {
                lock (Seen)
                {
                    if (Seen.Count == 0 || Seen[^1] != Options.Formatting.IndentSpaces)
                    {
                        Seen.Add(Options.Formatting.IndentSpaces);
                    }
                }
            };
        }

        public async Task StartAsync()
        {
            var services = new ServiceCollection().AddSingleton<ILiveSettings>(Live).BuildServiceProvider();
            var toServer = new Pipe();
            var toClient = new Pipe();
            var serving = OmniSharpLanguageServer.From(server => server
                .WithInput(toServer.Reader)
                .WithOutput(toClient.Writer)
                .WithClientConfiguration(LiveSettingsProtocol.Section)
                .OnStarted((ILanguageServer started, CancellationToken cancellation) =>
                {
                    // Follow starts following the client's configuration; FollowAsync is what a follower does once it changes, and not Follow made async.
#pragma warning disable VSTHRD103
                    Follower = ClientConfigurationFollower.Follow(services, started, () =>
#pragma warning restore VSTHRD103
                    {
                        Followed();
                        return Task.CompletedTask;
                    }, line =>
                    {
                        lock (Log)
                        {
                            Log.Add(line);
                        }

                        _logged.Release();
                    });
                    return Task.CompletedTask;
                }));

            var connecting = LanguageClient.From(client =>
            {
                client
                    .WithInput(toClient.Reader)
                    .WithOutput(toServer.Writer)
                    .WithClientCapabilities(new ClientCapabilities
                    {
                        Workspace = new WorkspaceClientCapabilities
                        {
                            Configuration = true,
                            DidChangeConfiguration = _declaresDidChangeConfiguration ? new DidChangeConfigurationCapability() : null,
                        },
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
                            await BeforeAnswering(number, tabSize);
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

        /// <summary>
        /// Waits for a change the follower applied that read what the client said as change number <paramref name="change"/>
        /// (<see cref="ClientSaysConfigurationChanged()"/>), or a later one; any change it applied, when none is given.
        /// </summary>
        /// <remarks>
        /// A server may follow its configuration twice as it starts, and the second can be applied after the first was waited for: a wait for whichever
        /// change is applied next can end on that one, before the change a test made is.
        /// </remarks>
        public async Task<bool> FollowedAsync(int change = 0)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (Volatile.Read(ref _followedChange) < change)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !await _followed.WaitAsync(left))
                {
                    return false;
                }
            }

            return true;
        }

        // the change each change the follower applied read, in the order they were applied.
        private readonly List<int> _applied = [];

        /// <summary>
        /// The changes the follower applied that read change number <paramref name="change"/> or a later one, each once, in the order they were first applied.
        /// </summary>
        public int[] AppliedSince(int change)
        {
            lock (_applied)
            {
                return [.. _applied.Where(applied => applied >= change).Distinct()];
            }
        }

        /// <summary>How many requests for the tab size the client was sent: one for each change the follower follows.</summary>
        public int TabSizeRequests => Volatile.Read(ref _tabSizeRequests);

        /// <summary>
        /// The client says its configuration changed, as change number this returns: what it answers from then on says that number
        /// (<see cref="ChangeKey"/>), and what a change the follower applies read is the number it has.
        /// </summary>
        public int ClientSaysConfigurationChanged()
        {
            var change = NextChange();
            _client!.DidChangeConfiguration(LiveSettingsProtocol.Changed);
            return change;
        }

        /// <summary>The client says its configuration changed, with settings of its own in the notification.</summary>
        public int ClientSaysConfigurationChanged(JToken settings)
        {
            var change = NextChange();
            _client!.DidChangeConfiguration(new DidChangeConfigurationParams { Settings = settings });
            return change;
        }

        // a setting no schema has, which the live settings keep among what the client said as they keep any other: what tells one change from another.
        private const string ChangeKey = "Wire:Change";

        private int _changes;

        // -1 until a change is applied: the first, as the server starts, reads change 0.
        private int _followedChange = -1;

        private int NextChange()
        {
            var change = Interlocked.Increment(ref _changes);
            Said[ChangeKey] = change.ToString(CultureInfo.InvariantCulture);
            return change;
        }

        private void Followed()
        {
            var change = Live.Said.TryGetValue(ChangeKey, out var said) ? int.Parse(said, CultureInfo.InvariantCulture) : 0;
            lock (_applied)
            {
                _applied.Add(change);
                _followedChange = Math.Max(_followedChange, change);
            }

            _followed.Release();
        }

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
        Assert.IsTrue(await wire.FollowedAsync(wire.ClientSaysConfigurationChanged()));
        Assert.AreEqual(8, wire.Options.Formatting.IndentSpaces, "followed when it changes");

        // and the user sets the indentation: it is what the server has.
        wire.Said["Formatting:IndentSpaces"] = "3";
        Assert.IsTrue(await wire.FollowedAsync(wire.ClientSaysConfigurationChanged()));
        Assert.AreEqual(3, wire.Options.Formatting.IndentSpaces, "the setting is not the editor's tab size");

        CollectionAssert.AreEqual(new[] { 2, 8, 3 }, wire.Seen, "whoever listens sees the indentation the settings end up with, never the default in between");
    }

    // workspace.configuration is what says a client answers workspace/configuration (LSP 3.17 §Configuration Request); workspace.didChangeConfiguration only
    // says whether it registers for the notification dynamically, and a client that declares no such thing still says its configuration changed.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClientThatAnswersWorkspaceConfiguration_ButDeclaresNoDidChangeConfiguration_IsAskedAndFollowed()
    {
        await using var wire = new Wire(declaresDidChangeConfiguration: false);
        await wire.StartAsync();

        Assert.IsTrue(await wire.FollowedAsync(), "asked once it is initialized");
        Assert.AreEqual(2, wire.Options.Formatting.IndentSpaces);

        wire.TabSize = 8;
        Assert.IsTrue(await wire.FollowedAsync(wire.ClientSaysConfigurationChanged()), "asked again when it says its configuration changed");
        Assert.AreEqual(8, wire.Options.Formatting.IndentSpaces);
    }

    // workspace.configuration declared false is a client that is not asked: OmniSharp reads it as a capability declared, whose value is false.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClientThatDeclaresWorkspaceConfigurationFalse_IsNotAsked()
    {
        await using var client = new RawClient(new JObject { ["workspace"] = new JObject { ["configuration"] = false } });
        await client.StartAsync();

        Assert.IsEmpty(client.Asked, "a server is started once what it asks as it starts is answered");
    }

    // a client that is not asked, but says its settings changed - in the settings it pushes - is not asked for its editor's tab size either.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClientThatDeclaresWorkspaceConfigurationFalse_IsNotAskedForTheEditorsTabSize()
    {
        await using var client = new RawClient(new JObject { ["workspace"] = new JObject { ["configuration"] = false, ["didChangeConfiguration"] = new JObject() } });
        await client.StartAsync();

        await client.SendAsync(new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "workspace/didChangeConfiguration",
            ["params"] = new JObject { ["settings"] = new JObject { [LiveSettingsProtocol.Section] = new JObject { ["Formatting"] = new JObject { ["IndentSpaces"] = "5" } } } },
        });

        // OmniSharp asks for its section as it starts any client that declares didChangeConfiguration, and what that is answered with is followed too: each
        // change followed is waited for, until the one the client pushed.
        while (client.Options.Formatting.IndentSpaces != 5)
        {
            Assert.IsTrue(await client.FollowedAsync(), "the settings it pushes are followed, as it says them");
        }

        Assert.IsFalse(client.Asked.Contains(FormattingTabSize.EditorSection), string.Join(", ", client.Asked));
    }

    // a client that is not OmniSharp's: it says the capabilities it is given, as they are - OmniSharp's own fills in what the handlers it has can answer - and
    // answers workspace/configuration with nothing, saying which sections it was asked for.
    private sealed class RawClient(JObject capabilities) : IAsyncDisposable
    {
        private readonly Pipe _toServer = new();
        private readonly Pipe _toClient = new();
        private readonly SemaphoreSlim _followed = new(0);
        private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromMinutes(1));
        private readonly List<string> _asked = [];
        private OmniSharpLanguageServer? _server;
        private ClientConfigurationFollower? _follower;
        private Task? _reading;

        public SdkAppOptions Options { get; } = new();

        /// <summary>The sections the server asked for, in the order it asked.</summary>
        public IReadOnlyList<string> Asked
        {
            get
            {
                lock (_asked)
                {
                    return [.. _asked];
                }
            }
        }

        public async Task StartAsync()
        {
            var schema = SettingsSchema.Platform;
            var live = new LiveSettings([new LiveSettingsTarget(schema, string.Empty, Options)], schema, Path.Combine(Path.GetTempPath(), "rdcore-nowhere", UserSettingsStore.FileName));
            var services = new ServiceCollection().AddSingleton<ILiveSettings>(live).BuildServiceProvider();
            var serving = OmniSharpLanguageServer.From(server => server
                .WithInput(_toServer.Reader)
                .WithOutput(_toClient.Writer)
                .WithClientConfiguration(LiveSettingsProtocol.Section)
                .OnStarted((ILanguageServer started, CancellationToken _) =>
                {
                    // Follow starts following the client's configuration; FollowAsync is what a follower does once it changes, and not Follow made async.
#pragma warning disable VSTHRD103
                    _follower = ClientConfigurationFollower.Follow(services, started, () =>
#pragma warning restore VSTHRD103
                    {
                        _followed.Release();
                        return Task.CompletedTask;
                    }, _ => { });
                    return Task.CompletedTask;
                }), _lifetime.Token);

            _reading = ReadAsync(_lifetime.Token);
            await SendAsync(new JObject
            {
                ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize",
                ["params"] = new JObject { ["processId"] = null, ["rootUri"] = null, ["capabilities"] = capabilities },
            });
            await _initialized.Task.WaitAsync(_lifetime.Token);
            await SendAsync(new JObject { ["jsonrpc"] = "2.0", ["method"] = "initialized", ["params"] = new JObject() });
            _server = await serving.WaitAsync(_lifetime.Token);
        }

        public Task<bool> FollowedAsync() => _followed.WaitAsync(TimeSpan.FromSeconds(20));

        public async Task SendAsync(JObject message)
        {
            var body = Encoding.UTF8.GetBytes(message.ToString(Newtonsoft.Json.Formatting.None));
            _ = await _toServer.Writer.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), _lifetime.Token);
            _ = await _toServer.Writer.WriteAsync(body, _lifetime.Token);
        }

        // what the server writes: the answer to initialize, and each workspace/configuration request, answered with a null for each item.
        private async Task ReadAsync(CancellationToken token)
        {
            while (await ReadMessageAsync(_toClient.Reader, token) is { } message)
            {
                if (message["id"]?.Value<int?>() == 1 && message["method"] is null)
                {
                    _initialized.TrySetResult();
                }
                else if (message["method"]?.Value<string>() == "workspace/configuration" && message["params"]?["items"] is JArray items)
                {
                    lock (_asked)
                    {
                        _asked.AddRange(items.Select(item => item["section"]?.Value<string>() ?? string.Empty));
                    }

                    await SendAsync(new JObject { ["jsonrpc"] = "2.0", ["id"] = message["id"], ["result"] = new JArray(items.Select(_ => JValue.CreateNull())) });
                }
            }
        }

        private static async Task<JObject?> ReadMessageAsync(PipeReader reader, CancellationToken token)
        {
            int? length = null;
            while (true)
            {
                var read = await reader.ReadAsync(token);
                var buffer = read.Buffer;
                if (length is null)
                {
                    var text = Encoding.ASCII.GetString(buffer.ToArray());
                    var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        length = int.Parse(text[..end].Split("Content-Length:")[1].Split("\r\n")[0].Trim(), CultureInfo.InvariantCulture);
                        reader.AdvanceTo(buffer.GetPosition(end + 4));
                        continue;
                    }
                }
                else if (buffer.Length >= length)
                {
                    var body = Encoding.UTF8.GetString(buffer.Slice(0, length.Value).ToArray());
                    reader.AdvanceTo(buffer.GetPosition(length.Value));
                    return JObject.Parse(body);
                }

                if (read.IsCompleted)
                {
                    return null;
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _follower?.Dispose();
            _server?.Dispose();
            await _lifetime.CancelAsync();
            try
            {
                await (_reading ?? Task.CompletedTask);
            }
            catch (OperationCanceledException)
            {
            }

            _lifetime.Dispose();
        }
    }

    // a client that answers workspace/configuration and says its configuration changed with an object - HexIDE sends {} - is asked: what it pushes is no
    // answer for the section.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClientThatAnswersWorkspaceConfiguration_AndPushesAnObject_IsAskedAgain()
    {
        await using var wire = new Wire(declaresDidChangeConfiguration: false);
        await wire.StartAsync();
        Assert.IsTrue(await wire.FollowedAsync());

        wire.Said["Formatting:IndentSpaces"] = "3";
        Assert.IsTrue(await wire.FollowedAsync(wire.ClientSaysConfigurationChanged(new JObject())), "the section as the client answers it, not the empty object it pushed");

        Assert.AreEqual(3, wire.Options.Formatting.IndentSpaces);
        Assert.AreEqual("3", wire.Live.Said["Formatting:IndentSpaces"]);
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
        wire.BeforeAnswering = async (_, tabSize) =>
        {
            if (tabSize == 5)
            {
                firstAsked.TrySetResult();
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

        // the client says its configuration changed twice; the editor's answer to the first, 5, comes after the answer to the second, 8. What the server
        // follows as it starts - more than once, at times, and still after it was first waited for - reads no change of the test's, and is told apart by that.
        var firstAsked = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        wire.BeforeAnswering = async (_, tabSize) =>
        {
            if (tabSize == 5)
            {
                firstAsked.TrySetResult();
                await release.Task;
            }
        };
        wire.TabSize = 5;
        var older = wire.ClientSaysConfigurationChanged();
        await firstAsked.Task.WaitAsync(TimeSpan.FromSeconds(20));
        wire.TabSize = 8;
        var newer = wire.ClientSaysConfigurationChanged();
        Assert.IsTrue(await wire.FollowedAsync(newer), "the newer change was followed");

        release.SetResult();

        // the older change, answered last, says what became of it: it is done once every follow that asked for the tab size said what became of it.
        Assert.IsTrue(await wire.LoggedUntilAsync(_ => wire.LinesWith("were applied") + wire.LinesWith("dropped") + wire.LinesWith("could not be applied") == wire.TabSizeRequests),
            string.Join(Environment.NewLine, wire.Log));
        Assert.AreEqual(0, wire.LinesWith("could not be applied"), string.Join(Environment.NewLine, wire.Log));
        Assert.IsGreaterThanOrEqualTo(1, wire.LinesWith("dropped"), "the older change was dropped, and said so");
        Assert.AreEqual(8, wire.Options.Formatting.IndentSpaces, "the older answer did not replace the newer one");
        CollectionAssert.AreEqual(new[] { 2, 8 }, wire.Seen, "and no listener saw it");
        CollectionAssert.AreEqual(new[] { newer }, wire.AppliedSince(older), "and nothing else was told as applied but the newer change");
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
        wire.BeforeAnswering = (_, _) => throw new InvalidOperationException("no such setting");
        wire.Said["Server:Verbose"] = "True";
        Assert.IsTrue(await wire.FollowedAsync(wire.ClientSaysConfigurationChanged()), "the change was followed");

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
        wire.BeforeAnswering = async (_, _) =>
        {
            asked.TrySetResult();
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
