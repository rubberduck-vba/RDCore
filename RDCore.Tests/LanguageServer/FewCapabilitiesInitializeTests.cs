using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using RDCore.LanguageServer.Folding;
using RDCore.LanguageServer.Navigation;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Server;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Server.Configuration;
using RDCore.Symbols;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A client that sends few capabilities - HexIDE sends a <c>diagnostic</c> one and little else - is answered <c>initialize</c>, and served every request
/// the server has: a handler reads the capability it is registered with, and is given none for a capability the client did not send.
/// </summary>
[TestClass]
public sealed class FewCapabilitiesInitializeTests
{
    // every handler that reads its client capability, and the others of the same families.
    private static void AddHandlers(OmniSharp.Extensions.LanguageServer.Server.LanguageServerOptions server)
    {
        var workspace = Substitute.For<IRenameService>();
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        var options = Options.Create(new SdkAppOptions());
        server
            .AddHandler(new DefinitionHandler(workspace))
            .AddHandler(new DeclarationHandler(workspace))
            .AddHandler(new TypeDefinitionHandler(workspace))
            .AddHandler(new ImplementationHandler(workspace))
            .AddHandler(new HoverHandler(workspace))
            .AddHandler(new ReferencesHandler(workspace))
            .AddHandler(new DocumentHighlightHandler(workspace))
            .AddHandler(new CallHierarchyPrepareHandler(workspace))
            .AddHandler(new TypeHierarchyPrepareHandler(workspace))
            .AddHandler(new InlayHintHandler(workspace))
            .AddHandler(new SelectionRangeHandler(documents, parsing))
            .AddHandler(new WorkspaceSymbolHandler(documents, parsing, new IntrinsicSymbolResolver(), options));
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    [DataRow(false, DisplayName = "a diagnostic capability and nothing else")]
    [DataRow(true, DisplayName = "no capability at all")]
    public async Task AClientThatSendsFewCapabilities_IsAnsweredInitialize_AndServedEveryRequest(bool none)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server =>
        {
            server.WithInput(toServer.Reader).WithOutput(toClient.Writer);
            AddHandlers(server);
        }, timeout.Token);
        var connecting = LanguageClient.From(client =>
        {
            client.WithInput(toClient.Reader).WithOutput(toServer.Writer);
            client.ClientCapabilities = none
                ? new ClientCapabilities()
                : new ClientCapabilities { TextDocument = new TextDocumentClientCapabilities { Diagnostic = new(true) } };
        }, timeout.Token);

        // a server that does not answer initialize keeps the client waiting forever: the test waits for so long only.
        await Task.WhenAll(serving, connecting).WaitAsync(TimeSpan.FromSeconds(20));
        using var server = await serving;
        using var languageClient = await connecting;

        var advertised = languageClient.ServerSettings.Capabilities;
        Assert.IsNotNull(advertised.DefinitionProvider, "definition");
        Assert.IsNotNull(advertised.DeclarationProvider, "declaration");
        Assert.IsNotNull(advertised.TypeDefinitionProvider, "typeDefinition");
        Assert.IsNotNull(advertised.ImplementationProvider, "implementation");
        Assert.IsNotNull(advertised.HoverProvider, "hover");
        Assert.IsNotNull(advertised.ReferencesProvider, "references");
        Assert.IsNotNull(advertised.DocumentHighlightProvider, "documentHighlight");
        Assert.IsNotNull(advertised.CallHierarchyProvider, "callHierarchy");
        Assert.IsNotNull(advertised.TypeHierarchyProvider, "typeHierarchy");
        Assert.IsNotNull(advertised.InlayHintProvider, "inlayHint");
        Assert.IsNotNull(advertised.SelectionRangeProvider, "selectionRange");
        Assert.IsNotNull(advertised.WorkspaceSymbolProvider, "workspace/symbol");
    }

    [TestMethod]
    public void TheCapabilitiesTheServerServes_AreCompleted_NeverWithANullValue()
    {
        var capabilities = new ClientCapabilities();

        ClientCapabilityDefaults.Complete(capabilities);

        Assert.IsNotNull(capabilities.TextDocument!.Definition.Value);
        Assert.IsNotNull(capabilities.TextDocument.Declaration.Value);
        Assert.IsNotNull(capabilities.TextDocument.Diagnostic.Value);
        Assert.IsNotNull(capabilities.TextDocument.Synchronization.Value);
        Assert.IsNotNull(capabilities.Window!.ShowMessage.Value);
        Assert.IsNotNull(capabilities.Workspace!.Diagnostics.Value);
        Assert.IsNotNull(capabilities.Workspace.Symbol.Value);
    }

    [TestMethod]
    public void WhatTheClientSaidOfACapability_IsKept()
    {
        var capabilities = new ClientCapabilities
        {
            TextDocument = new TextDocumentClientCapabilities
            {
                Definition = new(true, new DefinitionCapability { LinkSupport = true }),
                Hover = new(true, new HoverCapability()),
            },
            Workspace = new WorkspaceClientCapabilities { Diagnostics = new(true, new DiagnosticWorkspaceClientCapabilities { RefreshSupport = true }) },
        };

        ClientCapabilityDefaults.Complete(capabilities);

        Assert.IsTrue(capabilities.TextDocument!.Definition.Value!.LinkSupport, "what the client reads is read");
        Assert.IsNotNull(capabilities.TextDocument.Hover.Value, "a capability the server does not complete is the client's, as it sent it");
        Assert.IsTrue(capabilities.Workspace!.Diagnostics.Value!.RefreshSupport, "whether it is asked to pull its diagnostics again");
    }
}
