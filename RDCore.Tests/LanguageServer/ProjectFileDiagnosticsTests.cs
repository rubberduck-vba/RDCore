using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A project on a circular reference of projects is reported on its <c>.rdproj</c> (RDC31001), with the path of the cycle, by push and by the workspace pull.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.6.4 Rubberduck Core Diagnostics")]
public sealed class ProjectFileDiagnosticsTests
{
    private static readonly string Workspace = Path.Combine(Path.GetTempPath(), "rdcore-cycles");

    private static WorkspaceProject Project(string name, params string[] references)
    {
        var root = Path.Combine(Workspace, name);
        var project = new RDCoreProject
        {
            Name = name,
            References = [.. references.Select(reference => new RDCoreReference { Name = reference, IsProject = true })],
        };
        return new WorkspaceProject(root, new ProjectFile(root, project));
    }

    private static DocumentUri ProjectFileOf(string name) => DocumentUri.FromFileSystemPath(Path.Combine(Workspace, name, ProjectFile.FileName));

    private static IWorkspaceProjects ProjectsOf(params WorkspaceProject[] projects)
    {
        var workspace = Substitute.For<IWorkspaceProjects>();
        workspace.Projects.Returns(projects);
        return workspace;
    }

    private static IDiagnosticsPublisher Publisher(bool enabled = true)
    {
        var publisher = Substitute.For<IDiagnosticsPublisher>();
        publisher.IsEnabled.Returns(enabled);
        return publisher;
    }

    [TestMethod]
    public void ProjectsWithoutACycle_HaveNothingWrongWithThem()
    {
        var sut = new ProjectFileDiagnostics(ProjectsOf(Project("NavalConsole", "NavalLib"), Project("NavalLib")), Publisher());

        Assert.IsEmpty(sut.Current());
    }

    [TestMethod]
    public void EachProjectOnACycle_IsReportedOnItsProjectFile_WithThePath()
    {
        var sut = new ProjectFileDiagnostics(
            ProjectsOf(Project("NavalLib", "NavalWindow"), Project("NavalWindow", "NavalLib"), Project("NavalConsole", "NavalLib")), Publisher());

        var reports = sut.Current();

        CollectionAssert.AreEqual(new[] { ProjectFileOf("NavalLib"), ProjectFileOf("NavalWindow") }, reports.Select(report => report.Uri).ToArray());
        foreach (var (_, diagnostics) in reports)
        {
            var diagnostic = diagnostics.Single();
            Assert.AreEqual("RDC31001", diagnostic.Code?.String);
            Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
            StringAssert.Contains(diagnostic.Message, "NavalLib → NavalWindow → NavalLib");
        }
    }

    // a project on two cycles is told of each: A → B → A and A → C → A.
    [TestMethod]
    public void AProjectOnTwoCycles_IsToldOfEach()
    {
        var sut = new ProjectFileDiagnostics(ProjectsOf(Project("A", "B", "C"), Project("B", "A"), Project("C", "A")), Publisher());

        var first = sut.Current().Single(report => report.Uri == ProjectFileOf("A"));

        Assert.HasCount(2, first.Diagnostics);
    }

    [TestMethod]
    public void TheCode_CanBeDisabled()
    {
        var disabled = Substitute.For<IDisabledDiagnostics>();
        disabled.Codes.Returns(new HashSet<RDCoreDiagnosticCode> { ProjectFileDiagnostics.CircularProjectReference });
        var sut = new ProjectFileDiagnostics(ProjectsOf(Project("NavalLib", "NavalLib")), Publisher(), disabled);

        Assert.IsEmpty(sut.Current());
    }

    [TestMethod]
    public void Publish_SendsEachProjectFilesDiagnostics_AndClearsAProjectFileThatHasNoneNow()
    {
        WorkspaceProject[] projects = [Project("NavalLib", "NavalWindow"), Project("NavalWindow", "NavalLib")];
        var workspace = Substitute.For<IWorkspaceProjects>();
        workspace.Projects.Returns(projects);
        var publisher = Publisher();
        var sut = new ProjectFileDiagnostics(workspace, publisher);

        sut.Publish();
        publisher.Received(1).Publish(ProjectFileOf("NavalLib").ToUri(), null, Arg.Is<IReadOnlyList<Diagnostic>>(diagnostics => diagnostics.Count == 1));
        publisher.Received(1).Publish(ProjectFileOf("NavalWindow").ToUri(), null, Arg.Any<IReadOnlyList<Diagnostic>>());

        // the reference that closed the cycle is removed from NavalWindow.
        workspace.Projects.Returns([projects[0], Project("NavalWindow")]);
        sut.Publish();

        publisher.Received(1).Clear(ProjectFileOf("NavalLib").ToUri());
        publisher.Received(1).Clear(ProjectFileOf("NavalWindow").ToUri());
    }

    [TestMethod]
    public void Publish_SendsNothing_ToAClientThatPulls()
    {
        var publisher = Publisher(enabled: false);
        var sut = new ProjectFileDiagnostics(ProjectsOf(Project("NavalLib", "NavalLib")), publisher);

        sut.Publish();

        publisher.DidNotReceiveWithAnyArgs().Publish(default!, default, default!);
    }
}
