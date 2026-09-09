using System.IO.Abstractions.TestingHelpers;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

[TestClass]
public sealed class ProjectFileWriterTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-writer-ws");
    private static readonly string ProjectFilePath = Path.Combine(Root, ProjectFile.FileName);

    [TestMethod]
    public async Task SaveAsync_WritesAnIndentedRdproj_AtTheWorkspaceRoot()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(Root);
        var project = new ProjectFile(Root, new RDCoreProject
        {
            Name = "Probe",
            Modules = [new RDCoreModule { RelativeUri = "src/Mod1.bas" }],
        });

        await new ProjectFileWriter(fileSystem).SaveAsync(project);

        Assert.IsTrue(fileSystem.File.Exists(ProjectFilePath));
        var json = fileSystem.File.ReadAllText(ProjectFilePath);
        StringAssert.Contains(json, "\n  \"Version\"", "the output should be indented");
        StringAssert.Contains(json, "\"Probe\"");
    }

    [TestMethod]
    public async Task SaveAsync_RoundTripsThroughTheLoader()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(Root);
        var project = new ProjectFile(Root, new RDCoreProject
        {
            Name = "RoundTrip",
            Modules = [new RDCoreModule { RelativeUri = "A.cls" }, new RDCoreModule { RelativeUri = "B.bas" }],
            OtherFiles = [new RDCoreFile { RelativeUri = "notes.txt" }],
            PrecompilerConstants = { ["RDDEBUG"] = "1" },
        });

        await new ProjectFileWriter(fileSystem).SaveAsync(project);
        var reloaded = await new ProjectFileLoader(fileSystem).LoadAsync(Root);

        Assert.AreEqual(Root, reloaded.Uri);
        Assert.AreEqual("RoundTrip", reloaded.ProjectInfo.Name);
        CollectionAssert.AreEquivalent(
            new[] { "A.cls", "B.bas" },
            reloaded.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
        Assert.AreEqual("notes.txt", reloaded.ProjectInfo.OtherFiles.Single().RelativeUri);
        Assert.AreEqual("1", reloaded.ProjectInfo.PrecompilerConstants["RDDEBUG"]);
    }

    [TestMethod]
    public async Task SaveAsync_OverwritesAnExistingRdproj()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(ProjectFilePath, new MockFileData("{ \"stale\": true }"));

        await new ProjectFileWriter(fileSystem).SaveAsync(new ProjectFile(Root, new RDCoreProject { Name = "Fresh" }));

        var json = fileSystem.File.ReadAllText(ProjectFilePath);
        Assert.IsFalse(json.Contains("stale"));
        StringAssert.Contains(json, "\"Fresh\"");
    }
}
