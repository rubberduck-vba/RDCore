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

    private static MockFileSystem FolderWith(params (string RelativePath, string Source)[] files)
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(Root);
        foreach (var (relativePath, source) in files)
        {
            fs.AddFile(Path.Combine(Root, relativePath), new MockFileData(source));
        }

        return fs;
    }

    private static string Named(string name) => $"Attribute VB_Name = \"{name}\"\r\nOption Explicit\r\n";

    [TestMethod]
    public async Task LoadOrSynthesizeAsync_AProjectFile_IsLoaded()
    {
        var sut = new ProjectFileLoader(FileSystemWith(new RDCoreProject { Name = "Listed", Modules = [new RDCoreModule { RelativeUri = "Mod1.bas" }] }));

        var loaded = await sut.LoadOrSynthesizeAsync(Root, SupportedLanguages.RDVBA);

        Assert.IsFalse(loaded.IsSynthesized);
        Assert.AreEqual("Listed", loaded.ProjectInfo.Name);
    }

    [TestMethod]
    public async Task LoadOrSynthesizeAsync_AFolderWithNoProjectFile_IsTheProjectOfItsSourceFiles_AndNothingIsWritten()
    {
        var fs = FolderWith(
            ("Program.bas", Named("Program")),
            ("src/Shapes/Disc.cls", Named("Disc")),
            ("src/Sheet1.doccls", Named("Sheet1")),
            ("docs/README.md", "not a module"),
            (".git/hooks/Hook.bas", Named("Hook")),
            ("notes/.hidden/Hidden.bas", Named("Hidden")));

        var project = await new ProjectFileLoader(fs).LoadOrSynthesizeAsync(Root, SupportedLanguages.RDVBA);

        Assert.IsTrue(project.IsSynthesized);
        Assert.AreEqual(Root, project.Uri);
        CollectionAssert.AreEqual(new[] { "Program.bas", "src/Sheet1.doccls", "src/Shapes/Disc.cls" }, project.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
        CollectionAssert.AreEqual(new[] { "src", "src/Shapes" }, project.ProjectInfo.Folders);
        Assert.IsFalse(fs.FileExists(ProjectFilePath));
    }

    [TestMethod]
    public async Task LoadOrSynthesizeAsync_TheSourceFilesAreTheLanguages()
    {
        var fs = FolderWith(("Program.bas", Named("Program")), ("Sheet1.doccls", Named("Sheet1")), ("Shell.rdc", "PRINT 1"));

        var vb6 = await new ProjectFileLoader(fs).LoadOrSynthesizeAsync(Root, SupportedLanguages.VB6);
        var basic = await new ProjectFileLoader(fs).LoadOrSynthesizeAsync(Root, SupportedLanguages.BASIC);

        CollectionAssert.AreEqual(new[] { "Program.bas" }, vb6.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
        CollectionAssert.AreEqual(new[] { "Shell.rdc" }, basic.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
    }

    [TestMethod]
    public async Task LoadOrSynthesizeAsync_OfTwoFilesThatDeclareOneModuleName_TheFirstIsTheModule()
    {
        var fs = FolderWith(("a/Utils.bas", Named("Utils")), ("b/Utils.bas", Named("Utils")), ("b/Other.bas", Named("utils")));

        var project = await new ProjectFileLoader(fs).LoadOrSynthesizeAsync(Root, SupportedLanguages.RDVBA);

        CollectionAssert.AreEqual(new[] { "a/Utils.bas" }, project.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
        CollectionAssert.AreEqual(new[] { "a" }, project.ProjectInfo.Folders);
    }

    [TestMethod]
    [DataRow("Battleship", "Battleship", DisplayName = "an identifier")]
    [DataRow("my project", "VBAProject", DisplayName = "a space")]
    [DataRow("2048", "VBAProject", DisplayName = "a digit first")]
    [DataRow("vba", "VBAProject", DisplayName = "the standard library's name")]
    public async Task LoadOrSynthesizeAsync_TheProjectIsNamedAfterTheFolder_WhenItsNameIsAnIdentifier(string folder, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), folder);
        var fs = new MockFileSystem();
        fs.AddFile(Path.Combine(root, "Module1.bas"), new MockFileData(Named("Module1")));

        var project = await new ProjectFileLoader(fs).LoadOrSynthesizeAsync(root, SupportedLanguages.RDVBA);

        Assert.AreEqual(expected, project.ProjectInfo.Name);
    }
}
