using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The workspace's documents are loaded while a client's requests already ask for them: a request the server answers in parallel (semantic tokens, for one)
/// looks a document up while the load adds the others. HexIDE's findings desk #24 saw a <see cref="NullReferenceException"/> in the lookup during load.
/// </summary>
[TestClass]
public sealed class WorkspaceDocumentConcurrencyTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-concurrency-ws");

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ADocumentLookedUpWhileTheWorkspaceLoads_IsFoundOrNot_AndNeverFails()
    {
        var files = new MockFileSystem();
        const int count = 2000;
        for (var index = 0; index < count; index++)
        {
            files.AddFile(Path.Combine(Root, $"Module{index}.bas"), new MockFileData($"Attribute VB_Name = \"Module{index}\"\r\n"));
        }

        // as WorkspaceService.LoadAsync does: every document the project lists is tracked, unloaded, before they are loaded - all at once.
        var state = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        state.Initialize(Enumerable.Range(0, count).Select(index => new TextDocumentIdentifier(new Uri(Path.Combine(Root, $"Module{index}.bas")))));
        var documents = new WorkspaceDocumentService(state, NullLogger<WorkspaceDocumentService>.Instance, files.Path, files.File);
        documents.Initialize(Root);

        using var loaded = new CancellationTokenSource();
        var looking = Enumerable.Range(0, 2).Select(reader => Task.Run(() =>
        {
            var looked = 0;
            while (!loaded.IsCancellationRequested)
            {
                documents.TryGetDocument(new Uri(Path.Combine(Root, $"Module{looked++ % count}.bas")), out _);
                _ = documents.GetAllDocuments().Count();
            }
        })).ToArray();

        await Parallel.ForEachAsync(Enumerable.Range(0, count), async (index, _) =>
            await documents.TryLoadAsync(new TextDocumentIdentifier(new Uri(Path.Combine(Root, $"Module{index}.bas")))));
        loaded.Cancel();
        await Task.WhenAll(looking);

        Assert.AreEqual(count, documents.GetAllDocuments().Count());
        Assert.IsTrue(documents.TryGetDocument(new Uri(Path.Combine(Root, "Module1234.bas")), out var found));
        Assert.AreEqual("Attribute VB_Name = \"Module1234\"\r\n", found.Text);
    }
}
