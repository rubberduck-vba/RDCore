using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.LanguageServer.Diagnostics;

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
}
