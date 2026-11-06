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
}
