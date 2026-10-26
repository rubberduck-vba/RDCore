using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Server.Configuration;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Server.Configuration;

/// <summary>
/// A server asks its client for the live settings (<c>workspace/configuration</c>) once it is initialized, and again when the client says they changed
/// (<c>workspace/didChangeConfiguration</c>) - over the wire, as a platform's components do.
/// </summary>
[TestClass]
public sealed class LiveSettingsWireTests
{
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AServer_AsksForTheLiveSettings_WhenItStarts_AndWhenTheClientSaysTheyChanged()
    {
        var options = new SdkAppOptions { Server = new SdkServerOptions { Verbose = true } };
        var live = new LiveSettings([new LiveSettingsTarget(SettingsSchema.Platform, string.Empty, options)], SettingsSchema.Platform, Path.Combine(Path.GetTempPath(), "rdcore-nowhere", UserSettingsStore.FileName));
        var said = new Dictionary<string, string> { ["Server:Verbose"] = "False" };
        var applied = new SemaphoreSlim(0);

        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .WithConfigurationSection(LiveSettingsProtocol.Section)
            .OnStarted((ILanguageServer started, CancellationToken _) =>
            {
                ChangeToken.OnChange(started.Configuration.GetReloadToken, () =>
                {
                    live.Apply(LiveSettingsProtocol.Read(started.Configuration));
                    applied.Release();
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
            ConfigurationExtensions.OnConfiguration(client, (request, _) => Task.FromResult(LiveSettingsProtocol.Answer(request, said)));
        });

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;

        Assert.IsTrue(await applied.WaitAsync(TimeSpan.FromSeconds(20)), "asked once it is initialized");
        Assert.IsFalse(options.Server.Verbose);

        said["Server:Verbose"] = "True";
        languageClient.DidChangeConfiguration(new DidChangeConfigurationParams { Settings = new JObject() });

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!options.Server.Verbose && DateTime.UtcNow < deadline)
        {
            await applied.WaitAsync(TimeSpan.FromSeconds(1));
        }

        Assert.IsTrue(options.Server.Verbose, "asked again when the client said it changed");
    }
}
