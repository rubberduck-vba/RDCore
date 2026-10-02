using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Diagnostics.Handlers;

[Method(RDCorePlatformProtocol.DiagnoseDocument)]
internal sealed class DiagnoseDocumentHandler(
    ICoreDiagnosticsFactory diagnostics, IEnumerable<IModuleAnalyzer> analyzers, ILogger<DiagnoseDocumentHandler> logger)
    : RDCoreRequestHandler<DiagnoseDocumentRequest, DiagnoseDocumentResponse>
{
    protected override Task<DiagnoseDocumentResponse> HandleAsync(DiagnoseDocumentRequest request, CancellationToken token)
    {
        var payload = PlatformJson.Deserialize<DiagnoseDocumentPayload>(request.Json);

        var diagnosticList = new List<Diagnostic>();

        // what is wrong with the syntax,
        diagnosticList.AddRange(payload.ParseResult.SyntaxErrors.Select(diagnostics.FromVBSyntaxError));

        // what the host's static pass found wrong with the code, which is the language's to say and not an analyzer's: a compile error is an error,
        if (payload.Semantics is { } semantics)
        {
            diagnosticList.AddRange(semantics.DeclarationErrors
                .Concat(semantics.Procedures.SelectMany(procedure => procedure.CompileErrors))
                .Select(error => diagnostics.FromVBCompileError(error.ToInfo())));
        }

        // and what the analyzers find worth saying, from the same facts.
        var context = new ModuleAnalysisContext(payload.DocumentUri, payload.ParseResult, payload.Semantics);
        foreach (var analyzer in analyzers)
        {
            try
            {
                diagnosticList.AddRange(analyzer.Analyze(context).Select(diagnostics.FromAnalyzerFinding));
            }
            catch (Exception exception)
            {
                // one analyzer failing must not sink the others' diagnostics.
                logger.LogWarning(exception, "❌ {analyzer} failed for {uri}.", analyzer.GetType().Name, payload.DocumentUri);
            }
        }

        logger.LogInformation("📥 {method}: {uri} → {count} diagnostic(s)",
            RDCorePlatformProtocol.DiagnoseDocument, payload.DocumentUri, diagnosticList.Count);

        return Task.FromResult(new DiagnoseDocumentResponse
        {
            Diagnostics = diagnosticList,
            SourceVersion = payload.SourceVersion,
        });
    }
}
