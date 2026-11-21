using RDCore.SDK.Client;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Mcp;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>workspace/applyEdit</c> under <c>rdc mcp</c>: the edit is written to the file in its own encoding, and the language server is told which files changed
/// before it is answered; an edit that is refused writes nothing, and the server is told of no change.
/// </summary>
[TestClass]
public sealed class McpApplyEditHandlerTests
{
    private static readonly string Module = Path.Combine(Path.GetTempPath(), "rdcore-mcp-apply-edit", "Module1.bas");
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    private static ApplyWorkspaceEditParams EditOf(int? version) => new()
    {
        Edit = new WorkspaceEdit
        {
            DocumentChanges = new Container<WorkspaceEditDocumentChange>(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.FromFileSystemPath(Module), Version = version },
                Edits = new TextEditContainer(new TextEdit { Range = new Range(0, 11, 0, 15), NewText = "Thé" }),
            }),
        },
    };

    private static (McpApplyEditHandler Handler, MockFileSystem Files, ILanguageClientFacade Client) Serve()
    {
        var files = new MockFileSystem(new Dictionary<string, MockFileData> { [Module] = new(Windows1252.GetBytes("Public Sub Café()\r\nEnd Sub\r\n")) });
        var client = Substitute.For<ILanguageClientFacade>();
        return (new McpApplyEditHandler(new WorkspaceEditFiles(files, new SdkAppOptions().Environment.AnsiCodePage), client), files, client);
    }

    // the files the server was told changed.
    private static string[] Told(ILanguageClientFacade client)
        => [.. client.Workspace.ReceivedCalls()
            .SelectMany(call => call.GetArguments().OfType<DidChangeWatchedFilesParams>())
            .SelectMany(notification => notification.Changes.Select(change => $"{change.Type} {change.Uri}"))];

    [TestMethod]
    public async Task AnEdit_IsWrittenInTheFilesEncoding_AndTheServerIsToldTheFileChanged()
    {
        var (handler, files, client) = Serve();

        var response = await handler.Handle(EditOf(version: null), CancellationToken.None);

        Assert.IsTrue(response.Applied, response.FailureReason);
        CollectionAssert.AreEqual(Windows1252.GetBytes("Public Sub Thé()\r\nEnd Sub\r\n"), files.File.ReadAllBytes(Module));
        CollectionAssert.AreEqual(new[] { $"{FileChangeType.Changed} {DocumentUri.FromFileSystemPath(Module)}" }, Told(client));
    }

    [TestMethod]
    public async Task AnEditOfAVersion_IsOfADocumentRdcDoesNotHave_AndIsRefused()
    {
        var (handler, files, client) = Serve();

        var response = await handler.Handle(EditOf(version: 2), CancellationToken.None);

        Assert.IsFalse(response.Applied);
        Assert.AreEqual(0, response.FailedChange);
        CollectionAssert.AreEqual(Windows1252.GetBytes("Public Sub Café()\r\nEnd Sub\r\n"), files.File.ReadAllBytes(Module));
        Assert.AreEqual(0, Told(client).Length);
    }
}
