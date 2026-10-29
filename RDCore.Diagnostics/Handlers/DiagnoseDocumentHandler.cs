using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Errors;
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

        // the phases are asked for apart, so that what is ready is not kept waiting for what is not: what is answered is the findings of the phase asked for.
        if (payload.Phase.HasFlag(AnalysisPhase.Static))
        {
            // what is wrong with the syntax,
            diagnosticList.AddRange(payload.ParseResult.SyntaxErrors.Select(diagnostics.FromVBSyntaxError));

            // what the host's static pass found wrong with the code, which is the language's to say and not an analyzer's: a compile error is an error,
            // but at a name of a host's document, which RD-VBA does not host: what is said there is why it names nothing (RDC01005), and not only that it does not.
            if (payload.Semantics is { } semantics)
            {
                var documentNames = Analyzers.HostedDocumentCodeAnalyzer.References(semantics).Select(fact => fact.Location.Range).ToHashSet();
                diagnosticList.AddRange(semantics.DeclarationErrors
                    .Concat(semantics.Procedures.SelectMany(procedure => procedure.CompileErrors))
                    .Where(error => !(error.Id == VBCompileErrorId.VariableNotDefined && documentNames.Contains(error.Location.Range)))
                    .Select(error => diagnostics.FromVBCompileError(error.ToInfo())));
            }
        }

        // what the host's evaluation of the code found it raises whenever it runs, which is the language's to say too: a run-time error is an error. One that the
        // code raises for itself (Error, Err.Raise) is what the code is there to do, and is a fact for an analyzer to read rather than something wrong with it.
        if (payload.Phase.HasFlag(AnalysisPhase.Runtime) && payload.Semantics is { } evaluated)
        {
            diagnosticList.AddRange(evaluated.Procedures
                .SelectMany(procedure => procedure.Runtime?.Errors ?? [])
                .Where(error => !error.IsApplicationError)
                .Select(error => diagnostics.FromVBRuntimeError((VBRuntimeErrorInfo)error.ToInfo())));
        }

        // and what the analyzers find worth saying, from the same facts.
        var context = new ModuleAnalysisContext(payload.DocumentUri, payload.ParseResult, payload.Semantics);
        foreach (var analyzer in analyzers.Where(analyzer => payload.Phase.HasFlag(analyzer.Phase)))
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
