using System.IO.Abstractions.TestingHelpers;
using NSubstitute;
using RDCore.CLI.App.Commands;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

[TestClass]
public sealed class NewWorkspaceCommandTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-new-ws");
    private static readonly string RdprojPath = Path.Combine(Root, ProjectFile.FileName);

    private static (NewWorkspaceCommand Sut, MockFileSystem Fs) NewSut(params (string RelativePath, string Content)[] files)
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(Root);
        foreach (var (relative, content) in files)
        {
            fs.AddFile(Path.Combine(Root, relative), new MockFileData(content));
        }

        var sut = new NewWorkspaceCommand(Substitute.For<IConsoleMessageWriter>(), fs, new ProjectFileWriter(fs));
        return (sut, fs);
    }

    private static async Task<RDCoreProject> LoadProject(MockFileSystem fs)
        => (await new ProjectFileLoader(fs).LoadAsync(Root)).ProjectInfo;

    [TestMethod]
    public async Task Empty_WritesAnEmptyProjectNamedAfterTheDirectory()
    {
        var (sut, fs) = NewSut();

        var exit = await sut.ExecuteAsync([Root, "--empty"], CancellationToken.None);

        Assert.AreEqual(0, exit);
        var project = await LoadProject(fs);
        Assert.AreEqual("rdcore-new-ws", project.Name);
        Assert.IsEmpty(project.Modules);
        Assert.IsEmpty(project.OtherFiles);
    }

    [TestMethod]
    public async Task Name_OverridesTheDirectoryName()
    {
        var (sut, fs) = NewSut();

        await sut.ExecuteAsync([Root, "--empty", "--name", "MyProject"], CancellationToken.None);

        Assert.AreEqual("MyProject", (await LoadProject(fs)).Name);
    }

    [TestMethod]
    public async Task Scan_SortsModuleFilesFromOtherFiles()
    {
        var (sut, fs) = NewSut(
            ("Zeta.bas", "Attribute VB_Name = \"Zeta\""),
            ("sub/Alpha.cls", "Attribute VB_Name = \"Alpha\""),
            ("ThisWorkbook.doccls", ""),
            ("readme.txt", "notes"),
            ("data/values.json", "{}"));

        var exit = await sut.ExecuteAsync([Root], CancellationToken.None);

        Assert.AreEqual(0, exit);
        var project = await LoadProject(fs);
        // classification
        CollectionAssert.AreEquivalent(
            new[] { "Zeta.bas", "sub/Alpha.cls", "ThisWorkbook.doccls" },
            project.Modules.Select(module => module.RelativeUri).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "readme.txt", "data/values.json" },
            project.OtherFiles.Select(other => other.RelativeUri).ToArray());
        CollectionAssert.AreEquivalent(new[] { "data", "sub" }, project.Folders);
        // deterministic order (OrdinalIgnoreCase)
        var moduleUris = project.Modules.Select(module => module.RelativeUri).ToArray();
        CollectionAssert.AreEqual(
            moduleUris.OrderBy(uri => uri, StringComparer.OrdinalIgnoreCase).ToArray(),
            moduleUris);
    }

    [TestMethod]
    public async Task Scan_SkipsBuildOutputAndDotDirectories()
    {
        var (sut, fs) = NewSut(
            ("Real.bas", ""),
            (".git/config", "x"),
            ("bin/Debug/app.dll", "x"),
            ("obj/project.assets.json", "{}"));

        await sut.ExecuteAsync([Root], CancellationToken.None);

        var project = await LoadProject(fs);
        Assert.AreEqual("Real.bas", project.Modules.Single().RelativeUri);
        Assert.IsEmpty(project.OtherFiles);
    }

    [TestMethod]
    public async Task ExistingRdproj_WithoutForce_IsLeftAlone()
    {
        var (sut, fs) = NewSut((ProjectFile.FileName, "{ \"stale\": true }"));

        var exit = await sut.ExecuteAsync([Root], CancellationToken.None);

        Assert.AreEqual(0, exit);
        Assert.AreEqual("{ \"stale\": true }", fs.File.ReadAllText(RdprojPath));
    }

    [TestMethod]
    public async Task ExistingRdproj_WithForce_IsOverwritten()
    {
        var (sut, fs) = NewSut((ProjectFile.FileName, "{ \"stale\": true }"), ("Mod.bas", ""));

        var exit = await sut.ExecuteAsync([Root, "--force"], CancellationToken.None);

        Assert.AreEqual(0, exit);
        Assert.IsFalse(fs.File.ReadAllText(RdprojPath).Contains("stale"));
        Assert.AreEqual("Mod.bas", (await LoadProject(fs)).Modules.Single().RelativeUri);
    }

    [TestMethod]
    public async Task PathIsAnExistingFile_ReturnsOne()
    {
        var fs = new MockFileSystem();
        var filePath = Path.Combine(Path.GetTempPath(), "rdcore-new-ws-file");
        fs.AddFile(filePath, new MockFileData("not a directory"));
        var sut = new NewWorkspaceCommand(Substitute.For<IConsoleMessageWriter>(), fs, new ProjectFileWriter(fs));

        var exit = await sut.ExecuteAsync([filePath], CancellationToken.None);

        Assert.AreEqual(1, exit);
    }

    [TestMethod]
    public async Task Path_IsCreated_WhenItDoesNotExist()
    {
        var fs = new MockFileSystem();
        var fresh = Path.Combine(Path.GetTempPath(), "rdcore-new-ws-fresh");
        var sut = new NewWorkspaceCommand(Substitute.For<IConsoleMessageWriter>(), fs, new ProjectFileWriter(fs));

        var exit = await sut.ExecuteAsync([fresh, "--empty"], CancellationToken.None);

        Assert.AreEqual(0, exit);
        Assert.IsTrue(fs.File.Exists(Path.Combine(fresh, ProjectFile.FileName)));
    }
}
