using System.IO.Pipelines;
using System.Text;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.LanguageServer.Diagnostics;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Whether a client is asked to pull its diagnostics again (<c>workspace.diagnostics.refreshSupport</c>): only when it said so, and a capability with no value
/// - what the server registers for a client that sent none - is no support, and no exception.
/// </summary>
[TestClass]
public sealed class DiagnosticRefreshTests
{
    private static ILanguageServerFacade ServerFor(WorkspaceClientCapabilities? workspace)
    {
        var server = Substitute.For<ILanguageServerFacade>();
        server.ClientSettings.Returns(new InitializeParams { Capabilities = new ClientCapabilities { Workspace = workspace } });
        return server;
    }

    [TestMethod]
    public void AClientThatSaysItRefreshes_IsAskedTo()
        => Assert.IsTrue(DiagnosticRefresh.IsSupportedBy(ServerFor(new WorkspaceClientCapabilities
        {
            Diagnostics = new DiagnosticWorkspaceClientCapabilities { RefreshSupport = true },
        })));

    [TestMethod]
    public void AClientThatDoesNot_IsNot()
    {
        Assert.IsFalse(DiagnosticRefresh.IsSupportedBy(ServerFor(null)));
        Assert.IsFalse(DiagnosticRefresh.IsSupportedBy(ServerFor(new WorkspaceClientCapabilities())));
        Assert.IsFalse(DiagnosticRefresh.IsSupportedBy(ServerFor(new WorkspaceClientCapabilities
        {
            Diagnostics = new DiagnosticWorkspaceClientCapabilities { RefreshSupport = false },
        })));
    }

    [TestMethod]
    public void ACapabilityWithNoValue_IsNoSupport_AndThrowsNothing()
        => Assert.IsFalse(DiagnosticRefresh.IsSupportedBy(ServerFor(new WorkspaceClientCapabilities { Diagnostics = new(true) })));

    // over an actual connection: OmniSharp 0.19.9's own SendDiagnosticRefresh writes a notification, with no id.
    [TestMethod]
    public async Task TheRefresh_IsARequest_OnTheWire()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var toClient = new Pipe();
        var wire = new StringBuilder();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var read = await serverToClient.Reader.ReadAsync();
                foreach (var segment in read.Buffer)
                {
                    lock (wire)
                    {
                        wire.Append(Encoding.UTF8.GetString(segment.Span));
                    }

                    await toClient.Writer.WriteAsync(segment);
                }

                serverToClient.Reader.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted)
                {
                    await toClient.Writer.CompleteAsync();
                    return;
                }
            }
        });

        using var connecting = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serverTask = OmniSharpLanguageServer.From(options => options.WithInput(clientToServer.Reader).WithOutput(serverToClient.Writer), connecting.Token);
        var clientTask = LanguageClient.From(options => options
            .WithInput(toClient.Reader)
            .WithOutput(clientToServer.Writer)
            .WithClientCapabilities(new ClientCapabilities
            {
                Workspace = new WorkspaceClientCapabilities { Diagnostics = new DiagnosticWorkspaceClientCapabilities { RefreshSupport = true } },
            }), connecting.Token);
        using var server = await serverTask;
        using var client = await clientTask;

        DiagnosticRefresh.SendIfSupported(server);

        string sent = string.Empty;
        for (var attempt = 0; attempt < 50 && !sent.Contains(DiagnosticRefresh.Method); attempt++)
        {
            await Task.Delay(100);
            lock (wire)
            {
                sent = wire.ToString();
            }
        }

        var message = sent[sent.LastIndexOf('{', sent.IndexOf(DiagnosticRefresh.Method))..];
        StringAssert.Contains(message, "\"id\":");
    }
}
