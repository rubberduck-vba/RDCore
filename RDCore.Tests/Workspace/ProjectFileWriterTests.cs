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

    // a project file keeps its version as it is read and copied; a write stamps the version of the format it is written in.
    [TestMethod]
    public async Task AProjectFile_KeepsTheVersionThatWroteIt_UntilItIsWritten()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(Root);
        fileSystem.AddFile(ProjectFilePath, new MockFileData("""{ "Version": "0.0.9", "Configuration": [], "ProjectInfo": { "Name": "Old" } }"""));

        var loaded = await new ProjectFileLoader(fileSystem).LoadAsync(Root);
        Assert.AreEqual("0.0.9", loaded.Version, "read as it was written");
        Assert.AreEqual("0.0.9", (loaded with { IsDirty = true }).Version, "copied as it is");

        await new ProjectFileWriter(fileSystem).SaveAsync(loaded);
        Assert.AreEqual(ProjectFile.SchemaVersion, (await new ProjectFileLoader(fileSystem).LoadAsync(Root)).Version, "stamped by the write");
    }

    // a project the platform made up for a folder stays one, however it is changed, until it is written: a copy keeps what the project file is.
    [TestMethod]
    public async Task ASynthesizedProject_StaysSynthesized_WhenItIsChanged()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(Root, "Program.bas"), new MockFileData("Attribute VB_Name = \"Program\"\r\n"));
        var synthesized = await new ProjectFileLoader(fileSystem).LoadOrSynthesizeAsync(Root, SupportedLanguages.RDVBA);

        Assert.IsTrue(synthesized.IsSynthesized);
        Assert.IsTrue(synthesized.WithUri(Root).IsSynthesized);
        Assert.IsTrue(synthesized.WithFolder("src").IsSynthesized);
        Assert.IsTrue((synthesized with { ProjectInfo = synthesized.ProjectInfo with { Name = "Renamed" } }).IsSynthesized);
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

    // a .rdproj reads as the list of its files: a module or a file is its path, and only a document module says more.
    [TestMethod]
    public async Task AModuleOrAFile_IsWrittenAsItsPath_ADocumentModuleWithItsDocument()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(Root);
        var project = new ProjectFile(Root, new RDCoreProject
        {
            Name = "Terse",
            Modules = [new RDCoreModule { RelativeUri = "Module1.bas" }, new RDCoreModule { RelativeUri = "Sheet1.doccls", Super = DocClassType.ExcelWorksheet }],
            OtherFiles = [new RDCoreFile { RelativeUri = "README.md" }],
        });

        await new ProjectFileWriter(fileSystem).SaveAsync(project);

        var info = System.Text.Json.Nodes.JsonNode.Parse(fileSystem.File.ReadAllText(ProjectFilePath))!["ProjectInfo"]!;
        Assert.AreEqual("""["Module1.bas",{"RelativeUri":"Sheet1.doccls","Super":"ExcelWorksheet"}]""", info["Modules"]!.ToJsonString());
        Assert.AreEqual("""["README.md"]""", info["OtherFiles"]!.ToJsonString());
    }

    [TestMethod]
    [DataRow("""{ "RelativeUri": "A.bas", "Super": null }""", null, DisplayName = "an object whose Super is null")]
    [DataRow("""{ "RelativeUri": "A.bas" }""", null, DisplayName = "an object with no Super")]
    [DataRow("""{ "RelativeUri": "Sheet1.doccls", "Super": 2 }""", DocClassType.ExcelWorksheet, DisplayName = "an object whose Super is a number")]
    [DataRow("""{ "relativeUri": "Sheet1.doccls", "super": "ExcelWorksheet", "Name": "Sheet1" }""", DocClassType.ExcelWorksheet, DisplayName = "any case, and a member it has no use for")]
    [DataRow("\"A.bas\"", null, DisplayName = "a path")]
    public async Task AModule_IsReadFromAnyOfItsForms(string module, DocClassType? super)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(ProjectFilePath, new MockFileData($$"""{ "Version": "0.1.0", "Configuration": [], "ProjectInfo": { "Name": "P", "Modules": [ {{module}} ], "OtherFiles": [ { "RelativeUri": "notes.txt" }, "README.md" ] } }"""));

        var loaded = (await new ProjectFileLoader(fileSystem).LoadAsync(Root)).ProjectInfo;

        Assert.AreEqual(super, loaded.Modules.Single().Super);
        Assert.AreEqual(super is null ? "A.bas" : "Sheet1.doccls", loaded.Modules.Single().RelativeUri);
        CollectionAssert.AreEqual(new[] { "notes.txt", "README.md" }, loaded.OtherFiles.Select(file => file.RelativeUri).ToArray());
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
