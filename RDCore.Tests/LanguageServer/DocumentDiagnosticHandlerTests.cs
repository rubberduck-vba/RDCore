using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using DiagnosticSeverity = RDCore.SDK.Model.Diagnostics.DiagnosticSeverity;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

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
    public async Task FreshResult_MapsToARelatedFullReport_WithMappedDiagnostics()
    {
        var platform = new PlatformDiagnostic(
            1027, "RDCore.Diagnostics", DiagnosticSeverity.Error,
            new SourceLocation(DocUri, new SourceRange(3, 4, 3, 18)), "Syntax error", "unexpected token 'GetPtr'");
        _service.GetAsync(DocUri, null, Arg.Any<CancellationToken>())
            .Returns(DocumentDiagnosticsResult.Fresh(2, [platform]));

        var report = await Sut().Handle(Request(), CancellationToken.None);

        var full = (RelatedFullDocumentDiagnosticReport)report;
        Assert.AreEqual("v2", full.ResultId);

        var mapped = full.Items.Single();
        Assert.AreEqual(3, mapped.Range.Start.Line);
        Assert.AreEqual(4, mapped.Range.Start.Character);
        Assert.AreEqual(3, mapped.Range.End.Line);
        Assert.AreEqual(18, mapped.Range.End.Character);
        Assert.AreEqual(LspDiagnosticSeverity.Error, mapped.Severity);
        Assert.AreEqual(1027, mapped.Code!.Value.Long);
        Assert.AreEqual("RDCore.Diagnostics", mapped.Source);
        Assert.AreEqual("Syntax error", mapped.Message);
        Assert.AreEqual("unexpected token 'GetPtr'", mapped.Data!.ToObject<string>());
    }

    [TestMethod]
    public async Task NullVerbose_MapsToNullData()
    {
        _service.GetAsync(DocUri, null, Arg.Any<CancellationToken>())
            .Returns(DocumentDiagnosticsResult.Fresh(1,
            [
                new PlatformDiagnostic(1, "RDCore.Diagnostics", DiagnosticSeverity.Warning,
                    new SourceLocation(DocUri, SourceRange.Empty), "Implicit declaration", null),
            ]));

        var report = await Sut().Handle(Request(), CancellationToken.None);

        Assert.IsNull(((RelatedFullDocumentDiagnosticReport)report).Items.Single().Data);
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
