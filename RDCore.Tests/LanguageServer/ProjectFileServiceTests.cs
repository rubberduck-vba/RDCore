using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class ProjectFileServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-pfs");

    // builds a workspace whose .rdproj lists `projectModules` and whose disk also carries `looseFiles`.
    private static async Task<ProjectFileService> LoadedServiceAsync(
        (string RelativeUri, string Content)[] projectModules,
        params (string RelativeUri, string Content)[] looseFiles)
    {
        var project = new ProjectFile(Root, new RDCoreProject
        {
            Modules = [.. projectModules.Select(m => new RDCoreModule { RelativeUri = m.RelativeUri })],
        });

        var entries = new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)),
        };
        foreach (var (relativeUri, content) in projectModules.Concat(looseFiles))
        {
            entries[Path.Combine(Root, relativeUri)] = new(content);
        }

        var fs = new MockFileSystem(entries);
        var sut = new ProjectFileService(
            NullLogger<ProjectFileService>.Instance, fs.Path, fs.File, new ProjectFileLoader(fs));
        await sut.LoadAsync(Root);
        return sut;
    }

    [TestMethod]
    public async Task AddSourceFile_RejectsAModuleWhoseVBNameCollidesWithAnExistingOne()
    {
        var sut = await LoadedServiceAsync(
            [("src/Mod1.bas", "Attribute VB_Name = \"Shared\"\r\n")],
            ("src/vendor/Other.bas", "Attribute VB_Name = \"Shared\"\r\n"));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => sut.AddSourceFile(new RDCoreModule { RelativeUri = "src/vendor/Other.bas" }));

        StringAssert.Contains(exception.Message, "Shared");
        Assert.HasCount(1, sut.Project.ProjectInfo.Modules);
    }

    [TestMethod]
    public async Task AddSourceFile_AllowsAModuleWhoseFileNameMatchesButVBNameDiffers()
    {
        // two files both named Helpers.* in different folders, but distinct VB_Names.
        var sut = await LoadedServiceAsync(
            [("a/Helpers.bas", "Attribute VB_Name = \"HelpersA\"\r\n")],
            ("b/Helpers.bas", "Attribute VB_Name = \"HelpersB\"\r\n"));

        sut.AddSourceFile(new RDCoreModule { RelativeUri = "b/Helpers.bas" });

        Assert.HasCount(2, sut.Project.ProjectInfo.Modules);
    }

    [TestMethod]
    public async Task AddSourceFile_FallsBackToFileName_WhenSourceHasNoVBName()
    {
        var sut = await LoadedServiceAsync(
            [("src/Mod1.bas", "Attribute VB_Name = \"Mod1\"\r\n")],
            ("src/Mod1Copy.bas", "Option Explicit\r\n"));

        sut.AddSourceFile(new RDCoreModule { RelativeUri = "src/Mod1Copy.bas" });

        Assert.HasCount(2, sut.Project.ProjectInfo.Modules);

        // adding it again now collides on the file-name fallback.
        Assert.ThrowsExactly<InvalidOperationException>(
            () => sut.AddSourceFile(new RDCoreModule { RelativeUri = "src/Mod1Copy.bas" }));
    }
}
