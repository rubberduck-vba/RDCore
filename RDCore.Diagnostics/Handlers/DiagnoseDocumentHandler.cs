using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Diagnostics.Handlers;

[Method(RDCorePlatformProtocol.DiagnoseDocument)]
internal sealed class DiagnoseDocumentHandler(ICoreDiagnosticsFactory diagnostics, ILogger<DiagnoseDocumentHandler> logger)
    : RDCoreRequestHandler<DiagnoseDocumentRequest, DiagnoseDocumentResponse>
{
    protected override Task<DiagnoseDocumentResponse> HandleAsync(DiagnoseDocumentRequest request, CancellationToken token)
    {
        var payload = PlatformJson.Deserialize<DiagnoseDocumentPayload>(request.Json);

        // syntax errors are the diagnostics this extension projects at this stage; the compile-time,
        // runtime, and analyzer passes feed the same ICoreDiagnosticsFactory as they come online.
        var diagnosticList = payload.ParseResult.SyntaxErrors
            .Select(diagnostics.FromVBSyntaxError)
            .ToArray();

        logger.LogInformation("📥 {method}: {uri} → {count} diagnostic(s)",
            RDCorePlatformProtocol.DiagnoseDocument, payload.DocumentUri, diagnosticList.Length);

        return Task.FromResult(new DiagnoseDocumentResponse
        {
            Diagnostics = diagnosticList,
            SourceVersion = payload.SourceVersion,
        });
    }
}
