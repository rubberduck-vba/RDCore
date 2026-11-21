using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Source;
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
    public async Task ProjectsWithoutACycle_HaveNothingWrongWithThem()
    {
        var sut = new ProjectFileDiagnostics(ProjectsOf(Project("NavalConsole", "NavalLib"), Project("NavalLib")), Publisher());

        Assert.IsEmpty(await sut.CurrentAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task EachProjectOnACycle_IsReportedOnItsProjectFile_WithThePath()
    {
        var sut = new ProjectFileDiagnostics(
            ProjectsOf(Project("NavalLib", "NavalWindow"), Project("NavalWindow", "NavalLib"), Project("NavalConsole", "NavalLib")), Publisher());

        var reports = await sut.CurrentAsync(CancellationToken.None);

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
    public async Task AProjectOnTwoCycles_IsToldOfEach()
    {
        var sut = new ProjectFileDiagnostics(ProjectsOf(Project("A", "B", "C"), Project("B", "A"), Project("C", "A")), Publisher());

        var first = (await sut.CurrentAsync(CancellationToken.None)).Single(report => report.Uri == ProjectFileOf("A"));

        Assert.HasCount(2, first.Diagnostics);
    }

    [TestMethod]
    public async Task TheCode_CanBeDisabled()
    {
        var disabled = Substitute.For<IDisabledDiagnostics>();
        disabled.Codes.Returns(new HashSet<RDCoreDiagnosticCode> { ProjectFileDiagnostics.CircularProjectReference });
        var sut = new ProjectFileDiagnostics(ProjectsOf(Project("NavalLib", "NavalLib")), Publisher(), disabled);

        Assert.IsEmpty(await sut.CurrentAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Publish_SendsEachProjectFilesDiagnostics_AndClearsAProjectFileThatHasNoneNow()
    {
        WorkspaceProject[] projects = [Project("NavalLib", "NavalWindow"), Project("NavalWindow", "NavalLib")];
        var workspace = Substitute.For<IWorkspaceProjects>();
        workspace.Projects.Returns(projects);
        var publisher = Publisher();
        var sut = new ProjectFileDiagnostics(workspace, publisher);

        await sut.PublishAsync(CancellationToken.None);
        publisher.Received(1).Publish(ProjectFileOf("NavalLib").ToUri(), null, Arg.Is<IReadOnlyList<Diagnostic>>(diagnostics => diagnostics.Count == 1));
        publisher.Received(1).Publish(ProjectFileOf("NavalWindow").ToUri(), null, Arg.Any<IReadOnlyList<Diagnostic>>());

        // the reference that closed the cycle is removed from NavalWindow.
        workspace.Projects.Returns([projects[0], Project("NavalWindow")]);
        await sut.PublishAsync(CancellationToken.None);

        publisher.Received(1).Clear(ProjectFileOf("NavalLib").ToUri());
        publisher.Received(1).Clear(ProjectFileOf("NavalWindow").ToUri());
    }

    [TestMethod]
    public async Task Publish_SendsNothing_ToAClientThatPulls()
    {
        var publisher = Publisher(enabled: false);
        var sut = new ProjectFileDiagnostics(ProjectsOf(Project("NavalLib", "NavalLib")), publisher);

        await sut.PublishAsync(CancellationToken.None);

        publisher.DidNotReceiveWithAnyArgs().Publish(default!, default, default!);
    }

    // the project analyzers of the extensions are asked of each project, with where its file writes its name and the codes that are disabled.
    [TestMethod]
    public async Task WhatTheExtensionsFind_IsReportedOnTheProjectFile_WithTheCyclesOfTheProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "rdcore-project-analyzers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, ProjectFile.FileName), "{\r\n  \"Name\": \"VBAProject\",\r\n  \"Modules\": []\r\n}\r\n");
            var project = new WorkspaceProject(root, new ProjectFile(root, new RDCoreProject { Name = "VBAProject" }));
            var disabled = Substitute.For<IDisabledDiagnostics>();
            disabled.Codes.Returns(new HashSet<RDCoreDiagnosticCode> { ProjectFileDiagnostics.CircularProjectReference });
            var providers = Substitute.For<IProjectDiagnosticsProviders>();
            DiagnoseProjectPayload? sent = null;
            providers.DiagnoseAsync(Arg.Do<DiagnoseProjectPayload>(payload => sent = payload), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Diagnostic>>([new Diagnostic { Code = "RDC01024", Message = "default name" }]));
            var sut = new ProjectFileDiagnostics(ProjectsOf(project), Publisher(), disabled, providers);

            var report = (await sut.CurrentAsync(CancellationToken.None)).Single();

            Assert.AreEqual(DocumentUri.FromFileSystemPath(Path.Combine(root, ProjectFile.FileName)), report.Uri);
            Assert.AreEqual("RDC01024", report.Diagnostics.Single().Code?.String);
            Assert.IsNotNull(sent);
            Assert.AreEqual("VBAProject", sent.Project.Name);
            Assert.AreEqual(new SourceRange(new SourcePosition(1, 11), new SourcePosition(1, 21)), sent.NameRange);
            CollectionAssert.AreEqual(new[] { ProjectFileDiagnostics.CircularProjectReference }, sent.DisabledCodes.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task AProjectNoExtensionFindsAnythingOf_AndOnNoCycle_IsNotReported()
    {
        var providers = Substitute.For<IProjectDiagnosticsProviders>();
        providers.DiagnoseAsync(Arg.Any<DiagnoseProjectPayload>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Diagnostic>>([]));
        var sut = new ProjectFileDiagnostics(ProjectsOf(Project("NavalConsole", "NavalLib"), Project("NavalLib")), Publisher(), providers: providers);

        Assert.IsEmpty(await sut.CurrentAsync(CancellationToken.None));
        await providers.Received(2).DiagnoseAsync(Arg.Is<DiagnoseProjectPayload>(payload => payload.NameRange == null), Arg.Any<CancellationToken>());
    }

    // a folder opened with no .rdproj is a project the workspace makes up, named after the folder or VBAProject: it has no file to report against.
    [TestMethod]
    public async Task AProjectWithNoProjectFileYet_IsNotAskedOf()
    {
        var root = Path.Combine(Workspace, "My Macros");
        var synthesized = new WorkspaceProject(root, new ProjectFile(root, new RDCoreProject { Name = RDCoreProject.DefaultName }) { IsSynthesized = true });
        var providers = Substitute.For<IProjectDiagnosticsProviders>();
        var sut = new ProjectFileDiagnostics(ProjectsOf(synthesized), Publisher(), providers: providers);

        Assert.IsEmpty(await sut.CurrentAsync(CancellationToken.None));
        await providers.DidNotReceiveWithAnyArgs().DiagnoseAsync(default!, default);
    }
}
