using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.LanguageServer.Server.Handlers.Workspace;
using RDCore.SDK.Server.Commands;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What a client of the platform reads of its server commands, over the wire: their names, as the server advertises them; what a command asks its user, as a
/// message request; and a command that is refused, as an error whose message is the client's to show.
/// </summary>
[TestClass]
public sealed class ExecuteCommandWireTests
{
    // asks the user to choose among its arguments, and answers the choice.
    private sealed class Choose : IServerCommand
    {
        public string Name => "test.choose";

        public async Task<JToken?> ExecuteAsync(JArray? arguments, IServerCommandContext context, CancellationToken token)
            => await context.AskAsync("Which one?", [.. arguments!.Select(argument => argument.Value<string>()!)], token);
    }

    private sealed class Refuse : IServerCommand
    {
        public string Name => "test.refuse";

        public Task<JToken?> ExecuteAsync(JArray? arguments, IServerCommandContext context, CancellationToken token)
            => throw new ServerCommandRefusedException("Nothing to do.");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClient_ReadsTheCommands_IsAskedWhatTheyAsk_AndReadsWhyOneIsRefused()
    {
        // a request that is not answered fails the test, rather than keeping it waiting.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();
        var asked = new List<ShowMessageRequestParams>();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .WithServices(services => services.AddSingleton<IServerCommand, Choose>().AddSingleton<IServerCommand, Refuse>())
            .WithHandler<ExecuteCommandHandler>(), timeout.Token);
        var connecting = LanguageClient.From(client => client
            .WithInput(toClient.Reader)
            .WithOutput(toServer.Writer)
            .OnShowMessageRequest(request =>
            {
                asked.Add(request);
                return Task.FromResult(request.Actions!.Last());
            }), timeout.Token);

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;

        CollectionAssert.AreEquivalent(new[] { "test.choose", "test.refuse" }, languageClient.ServerSettings.Capabilities.ExecuteCommandProvider!.Commands!.ToArray());

        var chosen = await languageClient.SendRequest("workspace/executeCommand", new ExecuteCommandParams { Command = "test.choose", Arguments = new JArray("A", "B") })
            .Returning<JToken>(timeout.Token);
        Assert.AreEqual("B", chosen.Value<string>());
        Assert.AreEqual("Which one?", asked.Single().Message);
        CollectionAssert.AreEqual(new[] { "A", "B" }, asked.Single().Actions!.Select(action => action.Title).ToArray());

        var refused = await Assert.ThrowsAsync<OmniSharp.Extensions.JsonRpc.Server.JsonRpcException>(
            () => languageClient.SendRequest("workspace/executeCommand", new ExecuteCommandParams { Command = "test.refuse" }).Returning<JToken>(timeout.Token));
        Assert.AreEqual("Nothing to do.", refused.Message);
        Assert.AreEqual(-32803, refused.ErrorCode, "RequestFailed: an answer, which a client does not log as a fault");

        // OmniSharp routes a command by its name: one no handler advertises is not a method of the server.
        await Assert.ThrowsAsync<OmniSharp.Extensions.JsonRpc.Server.MethodNotSupportedException>(
            () => languageClient.SendRequest("workspace/executeCommand", new ExecuteCommandParams { Command = "test.unknown" }).Returning<JToken>(timeout.Token));
    }
}
