using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class DocumentDiagnosticHandlerTests
{
    private static readonly Uri DocUri = new("file:///c:/ws/src/Mod1.bas");

    private readonly IDocumentDiagnosticsService _service = Substitute.For<IDocumentDiagnosticsService>();

    private DocumentDiagnosticHandler Sut() => new(_service);

    private static DocumentDiagnosticParams Request(string? previousResultId = null) => new()
    {
        TextDocument = new TextDocumentIdentifier(DocUri),
        PreviousResultId = previousResultId,
    };

    [TestMethod]
    public async Task FreshResult_MapsToARelatedFullReport_ForwardingTheDiagnostics()
    {
        var diagnostic = new Diagnostic
        {
            Code = new DiagnosticCode("VBC01027"),
            Source = "RDCore",
            Message = "Syntax error",
            Severity = DiagnosticSeverity.Error,
            Range = new Range(new Position(3, 4), new Position(3, 18)),
        };
        _service.GetAsync(DocUri, null, Arg.Any<CancellationToken>())
            .Returns(DocumentDiagnosticsResult.Fresh(2, [diagnostic]));

        var report = await Sut().Handle(Request(), CancellationToken.None);

        var full = (RelatedFullDocumentDiagnosticReport)report;
        Assert.AreEqual("v2", full.ResultId);
        Assert.AreSame(diagnostic, full.Items.Single());
    }

    [TestMethod]
    public async Task UnchangedResult_MapsToARelatedUnchangedReport()
    {
        _service.GetAsync(DocUri, "v5", Arg.Any<CancellationToken>())
            .Returns(DocumentDiagnosticsResult.NotChanged(5));

        var report = await Sut().Handle(Request("v5"), CancellationToken.None);

        var unchanged = (RelatedUnchangedDocumentDiagnosticReport)report;
        Assert.AreEqual("v5", unchanged.ResultId);
    }

    [TestMethod]
    public void RegistrationOptions_IdentifyAsRdcore_WithNoInterFileOrWorkspaceDiagnostics()
    {
        var registration = (IRegistration<DiagnosticsRegistrationOptions, DiagnosticClientCapabilities>)Sut();

        var options = registration.GetRegistrationOptions(new(), new());

        Assert.AreEqual("rdcore", options.Identifier);
        Assert.IsFalse(options.InterFileDependencies);
        Assert.IsFalse(options.WorkspaceDiagnostics);
    }
}
