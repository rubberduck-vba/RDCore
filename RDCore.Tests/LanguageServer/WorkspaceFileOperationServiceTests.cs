using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The files a client created, renamed or deleted (<c>workspace/didCreateFiles</c>, <c>didRenameFiles</c>, <c>didDeleteFiles</c>), and what the project is made
/// of them: a source file is a module, a folder that holds one is a folder of the project, and what is outside the workspace or in a dot folder is nothing of it.
/// </summary>
[TestClass]
public sealed class WorkspaceFileOperationServiceTests
{
    // an absolute path the URI ctor accepts on both Windows and the Linux CI runner.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-fileops-ws");

    private readonly MockFileSystem _files = new();
    private readonly IWorkspaceReloadService _reload = Substitute.For<IWorkspaceReloadService>();
    private readonly WorkspaceFileOperationService _sut;
    private ProjectFile _project;

    public WorkspaceFileOperationServiceTests()
    {
        _project = new ProjectFile(Root, new RDCoreProject
        {
            Name = "Ops",
            Modules = [Module("A.bas"), Module("src/B.cls"), new RDCoreModule { RelativeUri = "src/Sheet1.doccls", Super = DocClassType.ExcelWorksheet }],
            Folders = ["src"],
        });
        Write("A.bas", "A");
        Write("src/B.cls", "B");
        Write("src/Sheet1.doccls", "Sheet1");

        // the change is made of the project as it is, as the reload service makes it.
        _reload.ChangeAsync(Arg.Any<Func<ProjectFile, ProjectFile>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _project = ((Func<ProjectFile, ProjectFile>)call[0])(_project);
            return Task.FromResult<WorkspaceReloadResult?>(new WorkspaceReloadResult(0, 0, false, false));
        });

        _sut = new WorkspaceFileOperationService(_reload, _files.Path, _files.File, _files.Directory, () => SupportedLanguages.RDVBA,
            NullLogger<WorkspaceFileOperationService>.Instance);
    }

    private static RDCoreModule Module(string relativeUri) => new() { RelativeUri = relativeUri };

    private void Write(string relativePath, string moduleName)
        => _files.AddFile(Path.Combine(Root, relativePath), new MockFileData($"Attribute VB_Name = \"{moduleName}\"\r\nOption Explicit\r\n"));

    private static Uri UriOf(string relativePath) => new(Path.Combine(Root, relativePath));

    private string[] Modules => [.. _project.ProjectInfo.Modules.Select(module => module.RelativeUri)];

    [TestMethod]
    public async Task ACreatedSourceFile_IsAModule_InTheFoldersItIsIn()
    {
        Write("lib/io/New.bas", "New");

        var result = await _sut.CreatedAsync([UriOf("lib/io/New.bas")], CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsEmpty(result.NotAdded);
        CollectionAssert.AreEqual(new[] { "A.bas", "src/B.cls", "src/Sheet1.doccls", "lib/io/New.bas" }, Modules);
        CollectionAssert.AreEqual(new[] { "lib", "lib/io", "src" }, _project.ProjectInfo.Folders);
    }

    [TestMethod]
    public async Task ACreatedFileThatIsNotASourceFile_IsNothingOfTheProject()
    {
        _files.AddFile(Path.Combine(Root, "README.md"), new MockFileData("# Ops"));

        await _sut.CreatedAsync([UriOf("README.md")], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "A.bas", "src/B.cls", "src/Sheet1.doccls" }, Modules);
    }

    [TestMethod]
    public async Task ACreatedFileThatDeclaresTheNameOfAModuleTheProjectHas_IsNotAdded_AndSaysWhy()
    {
        Write("Copy of A.bas", "a");

        var result = await _sut.CreatedAsync([UriOf("Copy of A.bas")], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "A.bas", "src/B.cls", "src/Sheet1.doccls" }, Modules);
        Assert.AreEqual(("Copy of A.bas", "a"), result!.NotAdded.Single());
    }

    [TestMethod]
    public async Task ACreatedFolder_IsAddedWithTheSourceFilesItHolds_ButNotWhatADotFolderHolds()
    {
        Write("lib/X.bas", "X");
        Write("lib/sub/Y.cls", "Y");
        Write("lib/.cache/Z.bas", "Z");

        await _sut.CreatedAsync([UriOf("lib")], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "A.bas", "src/B.cls", "src/Sheet1.doccls", "lib/X.bas", "lib/sub/Y.cls" }, Modules);
        CollectionAssert.AreEqual(new[] { "lib", "lib/sub", "src" }, _project.ProjectInfo.Folders);
    }

    [TestMethod]
    public async Task ARenamedModule_IsListedWhereItIsNow_AndADocumentModuleKeepsItsDocumentClass()
    {
        await _sut.RenamedAsync([(UriOf("src/Sheet1.doccls"), UriOf("Sheets/Sheet1.doccls"))], CancellationToken.None);

        var moved = _project.ProjectInfo.Modules.Single(module => module.RelativeUri == "Sheets/Sheet1.doccls");
        Assert.AreEqual(DocClassType.ExcelWorksheet, moved.Super);
        CollectionAssert.AreEqual(new[] { "Sheets", "src" }, _project.ProjectInfo.Folders);
    }

    [TestMethod]
    public async Task ARenamedFolder_IsRenamedWithWhatItHolds()
    {
        await _sut.RenamedAsync([(UriOf("src"), UriOf("lib"))], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "A.bas", "lib/B.cls", "lib/Sheet1.doccls" }, Modules);
        CollectionAssert.AreEqual(new[] { "lib" }, _project.ProjectInfo.Folders);
    }

    [TestMethod]
    public async Task AModuleRenamedToWhatIsNotASourceFile_IsNoLongerAModule()
    {
        await _sut.RenamedAsync([(UriOf("A.bas"), UriOf("A.bas.txt"))], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "src/B.cls", "src/Sheet1.doccls" }, Modules);
    }

    [TestMethod]
    public async Task AFileRenamedToASourceFile_IsAModule()
    {
        Write("Notes.bas", "Notes");

        await _sut.RenamedAsync([(UriOf("Notes.txt"), UriOf("Notes.bas"))], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "A.bas", "src/B.cls", "src/Sheet1.doccls", "Notes.bas" }, Modules);
    }

    [TestMethod]
    public async Task ADeletedFolder_IsRemovedWithWhatItHeld()
    {
        await _sut.DeletedAsync([UriOf("src")], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "A.bas" }, Modules);
        Assert.IsEmpty(_project.ProjectInfo.Folders);
    }

    [TestMethod]
    public async Task ADeletedModule_IsRemoved_AndAFileOfTheSameNameElsewhereIsNot()
    {
        await _sut.DeletedAsync([UriOf("B.cls")], CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "A.bas", "src/B.cls", "src/Sheet1.doccls" }, Modules);

        await _sut.DeletedAsync([UriOf("src/B.cls")], CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "A.bas", "src/Sheet1.doccls" }, Modules);
    }

    [TestMethod]
    public async Task WhatIsOutsideTheWorkspace_InADotFolder_OrTheProjectFile_IsNothingOfTheProject()
    {
        var outside = Path.Combine(Path.GetTempPath(), "rdcore-fileops-elsewhere", "Far.bas");
        _files.AddFile(outside, new MockFileData("Attribute VB_Name = \"Far\"\r\n"));
        Write(".vscode/Tool.bas", "Tool");

        await _sut.CreatedAsync([new Uri(outside), UriOf(".vscode/Tool.bas"), UriOf(ProjectFile.FileName)], CancellationToken.None);
        await _sut.DeletedAsync([UriOf(ProjectFile.FileName)], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "A.bas", "src/B.cls", "src/Sheet1.doccls" }, Modules);
    }

    [TestMethod]
    public async Task TheOperations_AreMadeInTheOrderTheClientMadeThem()
    {
        Write("New.bas", "New");

        var created = _sut.CreatedAsync([UriOf("New.bas")], CancellationToken.None);
        var renamed = _sut.RenamedAsync([(UriOf("New.bas"), UriOf("src/Renamed.bas"))], CancellationToken.None);
        await Task.WhenAll(created, renamed);

        CollectionAssert.AreEqual(new[] { "A.bas", "src/B.cls", "src/Sheet1.doccls", "src/Renamed.bas" }, Modules);
    }
}
