using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <strong>RD-VBAL §2.3.1.2</strong> the libraries the language server binds a workspace's names among are the ones its project lists, and the ones every project of
/// its language references without listing them.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.3.1.2 References")]
public sealed class ReferencedLibraryServiceTests
{
    private static string Symbols
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "the repository root, where RDCore.slnx is");
            return Path.Combine(directory.FullName, "Symbols");
        }
    }

    private static ReferencedLibraryService ServiceFor(SupportedLanguage language)
    {
        var project = Substitute.For<IProjectFileService>();
        project.Project.Returns(new ProjectFile(Path.GetTempPath(), new RDCoreProject { Name = "Project1" }));
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { Language = language.Id } });

        return new ReferencedLibraryService(project, new DirectoryLibrarySource(new FileSystem(), Symbols), options, NullLogger<ReferencedLibraryService>.Instance);
    }

    [TestMethod]
    public void AVB6Workspace_WhoseProjectListsNoReference_HasTheVisualBasicRuntime()
        => Assert.IsTrue(ServiceFor(SupportedLanguages.VB6).Libraries.Priorities.ContainsKey("VBRUN"));

    [TestMethod]
    public void AVBAWorkspace_WhoseProjectListsNoReference_HasNoLibrary()
        => Assert.IsEmpty(ServiceFor(SupportedLanguages.RDVBA).Libraries.Libraries);

    // the same name, of another type library: the project references another library, and the one it referenced before is not kept.
    [TestMethod]
    public void AReferenceToAnotherTypeLibraryOfTheSameName_IsAnotherLibrary()
    {
        var project = Substitute.For<IProjectFileService>();
        RDCoreReference Dao(string guid, int major, int minor) => new() { Name = "DAO", Guid = new Guid(guid), Major = major, Minor = minor };
        project.Project.Returns(
            new ProjectFile(Path.GetTempPath(), new RDCoreProject { Name = "Project1", References = [Dao("00025E01-0000-0000-C000-000000000046", 5, 0)] }),
            new ProjectFile(Path.GetTempPath(), new RDCoreProject { Name = "Project1", References = [Dao("00025E04-0000-0000-C000-000000000046", 3, 5)] }));
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { Language = SupportedLanguages.RDVBA.Id } });
        var service = new ReferencedLibraryService(project, new DirectoryLibrarySource(new FileSystem(), Symbols), options, NullLogger<ReferencedLibraryService>.Instance);

        var before = service.Libraries.Libraries.Single().Origin?.Version;
        var after = service.Libraries.Libraries.Single().Origin?.Version;

        Assert.AreEqual("5.0", before);
        Assert.AreEqual("3.5", after);
    }
}
