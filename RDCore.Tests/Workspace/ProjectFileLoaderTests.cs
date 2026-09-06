using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

[TestClass]
public sealed class ProjectFileLoaderTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-loader-ws");
    private static readonly string ProjectFilePath = Path.Combine(Root, ProjectFile.FileName);

    private static MockFileSystem FileSystemWith(RDCoreProject project) => new(new Dictionary<string, MockFileData>
    {
        [ProjectFilePath] = new MockFileData(JsonSerializer.Serialize(new ProjectFile(Root, project))),
    });

    [TestMethod]
    public void TryLocate_ResolvesTheRdprojPath_AndReportsExistence()
    {
        var sut = new ProjectFileLoader(FileSystemWith(new RDCoreProject()));

        Assert.IsTrue(sut.TryLocate(Root, out var path));
        Assert.AreEqual(ProjectFilePath, path);

        Assert.IsFalse(new ProjectFileLoader(new MockFileSystem()).TryLocate(Root, out _));
    }

    [TestMethod]
    public async Task LoadAsync_DeserializesAndStampsTheWorkspaceRoot()
    {
        var project = new RDCoreProject
        {
            Modules = [new RDCoreModule { RelativeUri = "src/Mod1.bas" }],
            PrecompilerConstants = { ["RDDEBUG"] = "1" },
        };
        var sut = new ProjectFileLoader(FileSystemWith(project));

        var loaded = await sut.LoadAsync(Root);

        Assert.AreEqual(Root, loaded.Uri);
        Assert.AreEqual("src/Mod1.bas", loaded.ProjectInfo.Modules[0].RelativeUri);
        Assert.AreEqual("1", loaded.ProjectInfo.PrecompilerConstants["RDDEBUG"]);
    }

    [TestMethod]
    public async Task LoadAsync_NoProjectFile_ThrowsFileNotFound()
        => await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => new ProjectFileLoader(new MockFileSystem()).LoadAsync(Root));
}
