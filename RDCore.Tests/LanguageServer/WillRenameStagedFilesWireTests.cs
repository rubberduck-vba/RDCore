using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Server.Handlers.Workspace;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>workspace/willRenameFiles</c> of a staged file, over the wire: the request reaches the server's own handler with the old and the new URI of each file,
/// and a staged file whose .frx changed since it was staged is deleted before the client renames it.
/// </summary>
[TestClass]
public sealed class WillRenameStagedFilesWireTests
{
    // a cap on a hang, not a budget: each wait is on the answer itself.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    [DataRow(false, true, DisplayName = "a .frx as it was: its staged file is kept")]
    [DataRow(true, false, DisplayName = "a .frx that changed: its staged file is deleted")]
    public async Task AClientAboutToRenameAStagedFile_AsksTheServer_WhichKeepsItOnlyForTheFrxItWasMadeOf(bool changed, bool kept)
    {
        var folder = Directory.CreateTempSubdirectory("rdcore-will-rename-").FullName;
        try
        {
            var frx = Path.Combine(folder, "Dialog.frx");
            File.WriteAllBytes(frx, [1, 2, 3, 4]);
            var staged = new StagedFiles(new System.IO.Abstractions.FileSystem());
            var file = staged.Stage(DocumentUri.FromFileSystemPath(frx), [1, 2, 3, 4], [5, 6, 7])!;
            if (changed)
            {
                File.WriteAllBytes(frx, [1, 2, 3, 0]);
            }

            using var timeout = new CancellationTokenSource(SafetyCap);
            var toServer = new Pipe();
            var toClient = new Pipe();
            var serving = OmniSharpLanguageServer.From(server => server
                .WithInput(toServer.Reader)
                .WithOutput(toClient.Writer)
                .AddHandler(new WillRenameStagedFilesHandler(staged)), timeout.Token);
            var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer), timeout.Token);
            await Task.WhenAll(serving, connecting);
            using var server = await serving;
            using var languageClient = await connecting;

            var answer = await languageClient.SendRequest("workspace/willRenameFiles", new WillRenameWorkspaceFilesParams
            {
                Files = [new WorkspaceFileRename { OldUri = file.Staged.ToString(), NewUri = file.Target.ToString() }],
            }).Returning<WorkspaceEdit?>(timeout.Token);

            Assert.IsNull(answer, "the server answers without an edit");
            Assert.AreEqual(kept, File.Exists(file.Staged.GetFileSystemPath()));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
