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

    // a project file as the platform wrote them before the standard library was implicit: VBA first, with the path of Office's VBE7.DLL.
    private const string WrittenBefore = """
        {
          "Version": "0.1.0",
          "Configuration": [],
          "ProjectInfo": {
            "Name": "Battleship",
            "References": [
              {
                "Name": "VBA",
                "AbsolutePath": "C:\\Program Files\\Microsoft Office\\root\\vfs\\ProgramFilesCommonX64\\Microsoft Shared\\VBA\\VBA7.1\\VBE7.DLL",
                "Guid": "000204ef-0000-0000-c000-000000000046",
                "Major": 4,
                "Minor": 2,
                "TypeLibInfoPath": null,
                "IsUnremovable": true
              },
              { "Name": "Scripting", "Guid": "420b2830-e718-11cf-893d-00a0c9054228", "Major": 1, "Minor": 0 }
            ],
            "Modules": [ { "RelativeUri": "modMain.bas" } ]
          }
        }
        """;

    [TestMethod]
    public async Task LoadAsync_AProjectFileWrittenBefore_IsReadWithoutTheStandardLibrary_AndWrittenWithoutIt()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [ProjectFilePath] = new MockFileData(WrittenBefore) });

        var loaded = await new ProjectFileLoader(fs).LoadAsync(Root);

        CollectionAssert.AreEqual(new[] { "Scripting" }, loaded.ProjectInfo.References.Select(reference => reference.Name).ToArray(), "every project has the standard library");
        Assert.AreEqual("modMain.bas", loaded.ProjectInfo.Modules.Single().RelativeUri);

        await new ProjectFileWriter(fs).SaveAsync(loaded);
        var written = fs.File.ReadAllText(ProjectFilePath);
        StringAssert.DoesNotMatch(written, new System.Text.RegularExpressions.Regex("VBE7|\"VBA\"|000204ef"), "dropped the next time it is written");
        StringAssert.Contains(written, "Scripting");
    }

    [TestMethod]
    public async Task ANewProject_AndOneTheFolderMakesUp_ListNoStandardLibrary()
    {
        Assert.IsEmpty(new RDCoreProject().References);
        StringAssert.DoesNotMatch(JsonSerializer.Serialize(new ProjectFile(Root)), new System.Text.RegularExpressions.Regex("VBE7|\"VBA\""));

        var synthesized = await new ProjectFileLoader(FolderWith(("Program.bas", Named("Program")))).LoadOrSynthesizeAsync(Root, SupportedLanguages.RDVBA);
        Assert.IsEmpty(synthesized.ProjectInfo.References);
    }

    [TestMethod]
    public void AReference_IsToTheStandardLibrary_ByItsNameOrItsTypeLibrary()
    {
        Assert.IsTrue(new RDCoreReference { Name = "vba" }.IsStandardLibrary);
        Assert.IsTrue(new RDCoreReference { Name = "Visual Basic For Applications", Guid = RDCoreReference.StandardLibraryGuid }.IsStandardLibrary);
        Assert.IsFalse(new RDCoreReference { Name = "Scripting" }.IsStandardLibrary);
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
