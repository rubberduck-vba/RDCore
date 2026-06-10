using RDCore.SDK.Server;
using RDCore.Workspace;
using System.Text.Json;

namespace RDCore.Tests.Workspace;

[TestClass]
public sealed class ProjectFileTests
{
    private static readonly string TestWorkspaceUri = "file://c:/dev/rdcore/workspaces/rdcore.tests";

    private static RDCoreReference TestLibraryReference { get; } = new() { Name = "TestLibrary", IsUnremovable = false };
    private static RDCoreModule TestModule { get; } = new() { RelativeUri = "model/Class1.cls", Name = "Class1" };
    private static RDCoreFile TestDocument { get; } = new RDCoreFile { RelativeUri = "README.md" };
    private static string TestFolder { get; } = "test/projectfile";

    [TestMethod]
    public void Version_ServerAppVersionByDefault()
    {
        // arrange
        var expected = RDCoreLanguageServerHost.Info.Version!.ToString(3);
        var sut = new ProjectFile();

        // act
        var version = sut.Version;

        // assert
        Assert.AreEqual(expected, version);
    }

    [TestMethod]
    public void ProjectInfo_IsNotNullByDefault()
    {
        // arrange
        var sut = new ProjectFile();

        // act
        var project = sut.ProjectInfo;

        // assert
        Assert.IsNotNull(project);
    }

    [TestMethod]
    public void GetHashCode_MatchesWorkspaceUri()
    {
        // arrange
        var sut = new ProjectFile(TestWorkspaceUri);
        var expected = TestWorkspaceUri.GetHashCode();

        // act
        var result = sut.GetHashCode();

        // assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void SameWorkspaceUri_Equals()
    {
        // arrange
        var project1 = new ProjectFile(TestWorkspaceUri);
        var project2 = new ProjectFile(TestWorkspaceUri);

        // act
        var equalsResult = project1.Equals(project2);
        var eqopResult = project1 == project2;
        var neqopResult = project1 != project2;

        // assert
        Assert.AreEqual(project1, project2);

        Assert.IsTrue(equalsResult, "Equals<ProjectFile>() method should return true");
        Assert.IsTrue(eqopResult, "== operation should return true");
        Assert.IsFalse(neqopResult, "!= operation should return false");
    }

    [TestMethod]
    public void Serialization_IgnoresWorkspaceUri()
    {
        // arrange
        var sut = new ProjectFile(TestWorkspaceUri);

        // act
        var serialized = JsonSerializer.Serialize(sut);
        var result = JsonSerializer.Deserialize<ProjectFile>(serialized)!;

        // assert
        Assert.AreEqual(string.Empty, result.Uri);
    }

    [TestMethod]
    public void Serialization_IgnoresDirtyFlag()
    {
        // arrange
        var sut = new ProjectFile(TestWorkspaceUri) { IsDirty = true };

        // act
        var serialized = JsonSerializer.Serialize(sut);
        var result = JsonSerializer.Deserialize<ProjectFile>(serialized)!;

        // assert
        Assert.IsFalse(result.IsDirty);
    }

    [TestMethod]
    public void WithReference_AddsWithDirtyFlag()
    {
        // arrange
        var sut = new ProjectFile(TestWorkspaceUri);

        // act
        var result = sut.WithReference(TestLibraryReference);

        // assert
        Assert.IsTrue(result.IsDirty, "IsDirty flag was not set");
        Assert.IsTrue(result.ProjectInfo.References.Contains(TestLibraryReference), "The reference was not added.");
    }

    [TestMethod]
    public void WithoutReference_RemovesWithDirtyFlag()
    {
        // arrange
        var project = new RDCoreProject
        {
            References = [TestLibraryReference]
        };

        var sut = new ProjectFile(TestWorkspaceUri, project);

        // act
        var result = sut.WithoutReference(TestLibraryReference);

        // assert
        Assert.IsTrue(result.IsDirty, "IsDirty flag was not set");
        Assert.IsFalse(result.ProjectInfo.References.Contains(TestLibraryReference), "The reference was not removed.");
    }

    [TestMethod]
    public void WithModule_AddsWithDirtyFlag()
    {
        // arrange
        var sut = new ProjectFile(TestWorkspaceUri);

        // act
        var result = sut.WithModule(TestModule);

        // assert
        Assert.IsTrue(result.IsDirty, "IsDirty flag was not set");
        Assert.IsTrue(result.ProjectInfo.Modules.Contains(TestModule), "The module was not added.");
    }

    [TestMethod]
    public void WithoutModule_RemovesWithDirtyFlag()
    {
        // arrange
        var project = new RDCoreProject
        {
            Modules = [TestModule]
        };

        var sut = new ProjectFile(TestWorkspaceUri, project);

        // act
        var result = sut.WithoutModule(TestModule);

        // assert
        Assert.IsTrue(result.IsDirty, "IsDirty flag was not set");
        Assert.IsFalse(result.ProjectInfo.Modules.Contains(TestModule), "The module was not removed.");
    }

    [TestMethod]
    public void WithDocument_AddsWithDirtyFlag()
    {
        // arrange
        var sut = new ProjectFile(TestWorkspaceUri);

        // act
        var result = sut.WithDocument(TestDocument);

        // assert
        Assert.IsTrue(result.IsDirty, "IsDirty flag was not set");
        Assert.IsTrue(result.ProjectInfo.OtherFiles.Contains(TestDocument), "The file was not added.");
    }

    [TestMethod]
    public void WithoutDocument_RemovesWithDirtyFlag()
    {
        // arrange
        var project = new RDCoreProject
        {
            OtherFiles = [TestDocument]
        };

        var sut = new ProjectFile(TestWorkspaceUri, project);

        // act
        var result = sut.WithoutDocument(TestDocument);

        // assert
        Assert.IsTrue(result.IsDirty, "IsDirty flag was not set");
        Assert.IsFalse(result.ProjectInfo.Modules.Contains(TestDocument), "The file was not removed.");
    }

    [TestMethod]
    public void WithFolder_AddsWithDirtyFlag()
    {
        // arrange
        var sut = new ProjectFile(TestWorkspaceUri);

        // act
        var result = sut.WithFolder(TestFolder);

        // assert
        Assert.IsTrue(result.IsDirty, "IsDirty flag was not set");
        Assert.IsTrue(result.ProjectInfo.Folders.Contains(TestFolder), "The folder was not added.");
    }

    [TestMethod]
    public void WithoutFolder_RemovesWithDirtyFlag()
    {
        // arrange
        var project = new RDCoreProject
        {
            Folders = [TestFolder]
        };

        var sut = new ProjectFile(TestWorkspaceUri, project);

        // act
        var result = sut.WithoutFolder(TestFolder);

        // assert
        Assert.IsTrue(result.IsDirty, "IsDirty flag was not set");
        Assert.IsFalse(result.ProjectInfo.Folders.Contains(TestFolder), "The folder was not removed.");
    }

    [TestMethod]
    public void RDCoreReference_Name_Equals()
    {
        // arrange
        var sut = new RDCoreReference
        {
            Name = TestLibraryReference.Name,
            Guid = Guid.NewGuid()
        };

        // act
        var equalsResult = sut.Equals(TestLibraryReference);
        var eqopResult = sut == TestLibraryReference;
        var neqopResult = sut != TestLibraryReference;

        // assert
        Assert.IsTrue(equalsResult, "Equals<RDCoreReference>() method should return true");
        Assert.IsTrue(eqopResult, "== operation should return true");
        Assert.IsFalse(neqopResult, "!= operation should return false");
    }

    [TestMethod]
    public void RDCoreFile_RelativeUri_Equals()
    {
        // arrange
        var sut = new RDCoreFile
        {
            RelativeUri = TestDocument.RelativeUri
        };

        // act
        var equalsResult = sut.Equals(TestDocument);
        var eqopResult = sut == TestDocument;
        var neqopResult = sut != TestDocument;

        // assert
        Assert.IsTrue(equalsResult, "Equals<RDCoreFile>() method should return true");
        Assert.IsTrue(eqopResult, "== operation should return true");
        Assert.IsFalse(neqopResult, "!= operation should return false");
    }
}
