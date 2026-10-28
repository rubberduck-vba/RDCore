using Microsoft.Extensions.Primitives;
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
        var formatting = new SdkFormattingOptions();

        Assert.IsTrue(FormattingTabSize.Adopt(formatting, new Dictionary<string, string> { ["Server:Verbose"] = "True" }, 2));
        Assert.AreEqual(2, formatting.IndentSpaces);
    }

    [TestMethod]
    public void AnIndentationTheUserSet_IsNotTheEditorsTabSize_WhateverItIs()
    {
        var formatting = new SdkFormattingOptions { IndentSpaces = 3 };

        Assert.IsFalse(FormattingTabSize.Adopt(formatting, new Dictionary<string, string> { ["Formatting:IndentSpaces"] = "3" }, 8));
        Assert.AreEqual(3, formatting.IndentSpaces);
    }

    [TestMethod]
    public void AnEditorWithNoTabSize_ChangesNothing()
    {
        var formatting = new SdkFormattingOptions();

        Assert.IsFalse(FormattingTabSize.Adopt(formatting, new Dictionary<string, string>(), null));
        Assert.AreEqual(4, formatting.IndentSpaces);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AServer_AsksForTheEditorsTabSize_WithTheLiveSettings_AndFollowsItWhenItChanges()
    {
        var options = new SdkAppOptions();
        var schema = SettingsSchema.Platform;
        var live = new LiveSettings([new LiveSettingsTarget(schema, string.Empty, options)], schema, Path.Combine(Path.GetTempPath(), "rdcore-nowhere", UserSettingsStore.FileName));
        var tabSize = 2;
        var said = new Dictionary<string, string>();
        var applied = new SemaphoreSlim(0);

        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .WithConfigurationSection(LiveSettingsProtocol.Section)
            .OnStarted((ILanguageServer started, CancellationToken cancellation) =>
            {
                ChangeToken.OnChange(started.Configuration.GetReloadToken, () =>
                {
                    var settings = LiveSettingsProtocol.Read(started.Configuration);
                    live.Apply(settings);
                    _ = Task.Run(async () =>
                    {
                        FormattingTabSize.Adopt(options.Formatting, settings, await FormattingTabSize.RequestEditorTabSizeAsync(started));
                        applied.Release();
                    });
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
                    Workspace = new WorkspaceClientCapabilities { Configuration = true, DidChangeConfiguration = new DidChangeConfigurationCapability() },
                });
            OmniSharp.Extensions.LanguageServer.Protocol.Workspace.ConfigurationExtensions.OnConfiguration(client, (request, _) =>
            {
                var answer = LiveSettingsProtocol.Answer(request, said);
                return Task.FromResult(new Container<JToken>(request.Items.Zip(answer, (item, token) => item.Section == FormattingTabSize.EditorSection ? new JValue(tabSize) : token)));
            });
        });

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;

        Assert.IsTrue(await applied.WaitAsync(TimeSpan.FromSeconds(20)), "asked once it is initialized");
        Assert.AreEqual(2, options.Formatting.IndentSpaces, "the editor's tab size, as the user set no indentation");

        // the user changes the tab size of the editor: a change of configuration like any other.
        tabSize = 8;
        languageClient.DidChangeConfiguration(LiveSettingsProtocol.Changed);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (options.Formatting.IndentSpaces != 8 && DateTime.UtcNow < deadline)
        {
            await applied.WaitAsync(TimeSpan.FromSeconds(1));
        }

        Assert.AreEqual(8, options.Formatting.IndentSpaces, "followed when it changes");

        // and the user sets the indentation: it wins.
        said["Formatting:IndentSpaces"] = "3";
        languageClient.DidChangeConfiguration(LiveSettingsProtocol.Changed);
        deadline = DateTime.UtcNow.AddSeconds(20);
        while (options.Formatting.IndentSpaces != 3 && DateTime.UtcNow < deadline)
        {
            await applied.WaitAsync(TimeSpan.FromSeconds(1));
        }

        Assert.AreEqual(3, options.Formatting.IndentSpaces, "the setting wins over the editor's tab size");
    }
}
