using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using System.IO.Abstractions.TestingHelpers;
using System.Text;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A module file the language server reads and saves keeps its encoding: a Windows-1252 file is read with its accented letters, and saved in Windows-1252, its
/// line endings as the document has them.
/// </summary>
[TestClass]
public sealed class WorkspaceDocumentEncodingTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-ls-encoding-ws");
    private static readonly string Module = Path.Combine(Root, "Module1.bas");
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    private const string Text = "Attribute VB_Name = \"Module1\"\r\nPublic Sub Café()\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task AWindows1252Document_IsReadWithItsAccentedLetters_AndSavedInWindows1252()
    {
        var files = new MockFileSystem(new Dictionary<string, MockFileData> { [Module] = new(Windows1252.GetBytes(Text)) });
        var documents = new WorkspaceDocumentService(new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance), NullLogger<WorkspaceDocumentService>.Instance,
            files.Path, files.File);
        documents.Initialize(Root);
        var uri = DocumentUri.FromFileSystemPath(Module);
        var id = new TextDocumentIdentifier(uri);

        Assert.IsTrue(await documents.TryLoadAsync(id));
        Assert.IsTrue(documents.TryGetDocument(uri.ToUri(), out var loaded));
        Assert.AreEqual(Text, loaded.Text);

        // the client opens it, and edits it: the document is dirty, and the server saves it.
        documents.Open(uri.ToUri(), Text, 1);
        var edited = Text.Replace("Café", "Thé") + "' é\r\n";
        Assert.AreEqual(DocumentChangeOutcome.Applied, documents.Change(uri.ToUri(), 2, [new TextDocumentContentChangeEvent { Text = edited }]));
        Assert.IsTrue(await documents.TrySaveAsync(id));

        CollectionAssert.AreEqual(Windows1252.GetBytes(edited), files.File.ReadAllBytes(Module));
    }

    [TestMethod]
    public async Task AWindows1252Document_WithATextWindows1252CannotWrite_IsNotSaved_AndItsFileIsLeftAsItWas()
    {
        var bytes = Windows1252.GetBytes(Text);
        var files = new MockFileSystem(new Dictionary<string, MockFileData> { [Module] = new(bytes) });
        var documents = new WorkspaceDocumentService(new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance), NullLogger<WorkspaceDocumentService>.Instance,
            files.Path, files.File);
        documents.Initialize(Root);
        var uri = DocumentUri.FromFileSystemPath(Module);
        var id = new TextDocumentIdentifier(uri);
        Assert.IsTrue(await documents.TryLoadAsync(id));

        documents.Open(uri.ToUri(), Text, 1);
        Assert.AreEqual(DocumentChangeOutcome.Applied, documents.Change(uri.ToUri(), 2, [new TextDocumentContentChangeEvent { Text = Text + "' →\r\n" }]));

        Assert.IsFalse(await documents.TrySaveAsync(id));
        CollectionAssert.AreEqual(bytes, files.File.ReadAllBytes(Module));
    }
}
