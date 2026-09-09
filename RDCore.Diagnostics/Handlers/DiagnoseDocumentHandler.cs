using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Diagnostics.Handlers;

[Method(RDCorePlatformProtocol.DiagnoseDocument)]
internal sealed class DiagnoseDocumentHandler(ILogger<DiagnoseDocumentHandler> logger)
    : RDCoreRequestHandler<DiagnoseDocumentRequest, PlatformJsonEnvelope>
{
    private const string ProviderName = "RDCore.Diagnostics";

    protected override Task<PlatformJsonEnvelope> HandleAsync(DiagnoseDocumentRequest request, CancellationToken token)
    {
        var payload = PlatformJson.Deserialize<DiagnoseDocumentPayload>(request.Json);

        // the sole diagnostic today: the parser's located syntax errors, re-emitted as platform
        // diagnostics. the semantic analyzers (RuntimeSemanticsAnalyzer, RDCoreDiagnosticId) are a later pass.
        var diagnostics = payload.ParseResult.SyntaxErrors
            .Select(error => new PlatformDiagnostic(
                error.ErrorId, ProviderName, DiagnosticSeverity.Error,
                error.Location, error.Description, error.Verbose))
            .ToArray();

        logger.LogInformation("📥 {method}: {uri} → {count} diagnostic(s)",
            RDCorePlatformProtocol.DiagnoseDocument, payload.DocumentUri, diagnostics.Length);

        return Task.FromResult(PlatformJsonEnvelope.Of(new DiagnoseDocumentResult(diagnostics, payload.SourceVersion)));
    }
}
