using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Folding;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Runtime.Abstract.Execution;

using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Drives <see cref="DocumentSymbolHandler"/> through an actual LSP <c>initialize</c> /
/// <c>textDocument/documentSymbol</c> exchange over an in-memory transport, rather than calling
/// the handler directly, so the end-to-end wiring (registration, serialization, and the
/// hierarchical <see cref="DocumentSymbol"/> shape) is exercised the same way a real editor would use it.
/// </summary>
[TestClass]
public sealed class DocumentSymbolIntegrationTests
{
    private const string WorkspaceRoot = @"c:\ws";
    private const string RelativePath = "Mod1.bas";

    [TestMethod]
    public async Task InitializeThenRequestDocumentSymbol_AnswersTheModuleAndItsProcedure()
    {
        var document = new WorkspaceDocument(RelativePath, WorkspaceRoot);
        var documentUri = document.Id.Uri.ToUri();

        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([document]);

        var parsing = Substitute.For<IParsingClientService>();
        parsing.ParseDocumentAsync(documentUri, Arg.Any<CancellationToken>())
            .Returns(new ModuleParser().Parse(documentUri, string.Join('\n',
                "Attribute VB_Name = \"Mod1\"",
                "Public Sub Alpha()",
                "End Sub")));

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var serverTask = OmniSharpLanguageServer.From(options => options
            .WithInput(clientToServer.Reader)
            .WithOutput(serverToClient.Writer)
            .WithHandler<DocumentSymbolHandler>()
            .WithHandler<FoldingRangeHandler>()
            .WithServices(services =>
            {
                services.AddSingleton(documents);
                services.AddSingleton(parsing);
                services.AddSingleton<ISymbolResolver>(new IntrinsicSymbolResolver());
            }), cts.Token);

        var clientTask = LanguageClient.From(options => options
            .WithInput(serverToClient.Reader)
            .WithOutput(clientToServer.Writer), cts.Token);

        var (server, client) = (await serverTask, await clientTask);
        try
        {
            var foldingRequest = new FoldingRangeRequestParam
            {
                TextDocument = new TextDocumentIdentifier(documentUri),
            };
            var foldingRangesBefore = await client.RequestFoldingRange(foldingRequest, cts.Token);
            Assert.IsNotNull(foldingRangesBefore);
            Assert.HasCount(1, foldingRangesBefore);

            var response = await client.RequestDocumentSymbol(
                new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(documentUri) },
                cts.Token);

            Assert.IsNotNull(response);
            var symbol = response.Single();
            Assert.IsTrue(symbol.IsDocumentSymbol, "the response must prefer the hierarchical DocumentSymbol form.");

            var module = symbol.DocumentSymbol!;
            Assert.AreEqual("Mod1", module.Name);
            Assert.AreEqual(SymbolKind.Module, module.Kind);
            Assert.IsTrue(module.Range.Start.Line >= 0 && module.Range.End.Line >= module.Range.Start.Line,
                "the module's range must be a valid, navigable source location.");

            var procedure = module.Children!.Single();
            Assert.AreEqual("Alpha", procedure.Name);
            Assert.AreEqual(SymbolKind.Method, procedure.Kind);
            Assert.IsTrue(procedure.Range.Start.Line >= 0 && procedure.Range.End.Line >= procedure.Range.Start.Line,
                "the procedure's range must be a valid, navigable source location.");

            var foldingRangesAfter = await client.RequestFoldingRange(foldingRequest, cts.Token);
            Assert.IsNotNull(foldingRangesAfter);
            Assert.HasCount(1, foldingRangesAfter);
        }
        finally
        {
            server.Dispose();
            client.Dispose();
        }
    }
}
