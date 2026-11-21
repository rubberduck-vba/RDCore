using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Handlers;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC01024</c>: a project named <c>VBAProject</c>, the name MS-VBA gives a new project, reported against its <c>.rdproj</c> at the name.
/// </summary>
[TestClass]
public sealed class DefaultProjectNameAnalyzerTests
{
    private static readonly Uri ProjectFile = new("file:///c:/ws/.rdproj");
    private static readonly SourceRange NameRange = new(new SourcePosition(1, 11), new SourcePosition(1, 21));

    private static AnalyzerFinding[] Analyze(string name, SourceRange? nameRange = null)
        => [.. new DefaultProjectNameAnalyzer().Analyze(new ProjectAnalysisContext(ProjectFile, new RDCoreProject { Name = name }, nameRange))];

    [TestMethod]
    public void AProjectNamedVBAProject_IsReportedAtItsName()
    {
        var finding = Analyze(RDCoreProject.DefaultName, NameRange).Single();

        Assert.AreEqual(RDCoreDiagnosticId.DefaultProjectName.ToCode(), finding.Code);
        Assert.AreEqual(NameRange, finding.Range);
        Assert.AreEqual(DiagnosticSeverity.Information, finding.Severity);
        StringAssert.Contains(finding.Message, "'VBAProject'");
    }

    [TestMethod]
    public void TheNameIsComparedWithoutRegardToCase()
        => Assert.HasCount(1, Analyze("vbaproject", NameRange));

    [TestMethod]
    public void AProjectFileThatWritesNoName_IsReportedAtItsStart()
        => Assert.AreEqual(SourceRange.Empty, Analyze(RDCoreProject.DefaultName).Single().Range);

    [TestMethod]
    public void AProjectWithANameOfItsOwn_IsNotReported()
    {
        Assert.IsEmpty(Analyze("NavalBattle", NameRange));
        Assert.IsEmpty(Analyze("VBAProject1", NameRange));
    }

    [TestMethod]
    public async Task TheExtension_AnswersForTheProject_UnlessTheCodeIsDisabled()
    {
        var handler = new DiagnoseProjectHandler(new DiagnosticFactory(), [new DefaultProjectNameAnalyzer()], NullLogger<DiagnoseProjectHandler>.Instance);
        var project = new RDCoreProject { Name = RDCoreProject.DefaultName };

        var answered = await handler.Handle(
            new DiagnoseProjectParams { Json = PlatformJson.Serialize(new DiagnoseProjectPayload(ProjectFile, project, NameRange)) }, CancellationToken.None);
        var off = new DiagnoseProjectPayload(ProjectFile, project, NameRange) { DisabledCodes = [RDCoreDiagnosticId.DefaultProjectName.ToCode()] };
        var disabled = await handler.Handle(new DiagnoseProjectParams { Json = PlatformJson.Serialize(off) }, CancellationToken.None);

        var diagnostic = answered.Diagnostics.Single();
        Assert.AreEqual("RDC01024", diagnostic.Code?.String);
        Assert.AreEqual(new Position(1, 11), diagnostic.Range.Start);
        Assert.IsEmpty(disabled.Diagnostics);
    }
}
