using NSubstitute;
using RDCore.CLI.App.Repl;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli.Repl;

[TestClass]
public sealed class ReplWorkspaceTests
{
    // MS-VBA refuses a module named as its project ("Name conflicts with existing module, project, or object library"): the shell's project is named as
    // MS-VBA names a new one, which is the name Err.Source and a project-qualified reference give.
    [TestMethod]
    public async Task TheShellsProject_IsVBAProject_WithItsOneModuleProgram()
    {
        var writer = Substitute.For<IProjectFileWriter>();
        RDCoreProject? written = null;
        await writer.SaveAsync(Arg.Do<ProjectFile>(project => written = project.ProjectInfo), Arg.Any<CancellationToken>());

        using var workspace = await ReplWorkspace.CreateAsync(new MockFileSystem(), writer);

        Assert.IsNotNull(written);
        Assert.AreEqual("VBAProject", written.Name);
        CollectionAssert.AreEqual(new[] { $"{ReplProgram.ModuleName}.bas" }, written.Modules.Select(module => module.RelativeUri).ToArray());
    }
}
