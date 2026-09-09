using Microsoft.Extensions.Logging;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// Answers the language server's <c>textDocument/diagnostic</c> pull: parses the document, fans it
/// out to every diagnostics-provider extension over <c>rdcore/diagnostics/document</c>, aggregates
/// the results, and drops any that raced a later edit.
/// </summary>
internal interface IDocumentDiagnosticsService
{
    Task<DocumentDiagnosticsResult> GetAsync(Uri documentUri, string? previousResultId, CancellationToken token);
}

/// <summary>
/// The outcome of a diagnostics pull. <see cref="ResultId"/> tracks the workspace document version;
/// when it equals the client's <c>previousResultId</c> the report is <see cref="Unchanged"/> and
/// carries no items.
/// </summary>
internal readonly record struct DocumentDiagnosticsResult(string ResultId, bool Unchanged, IReadOnlyList<PlatformDiagnostic> Diagnostics)
{
    public static DocumentDiagnosticsResult Fresh(int version, IReadOnlyList<PlatformDiagnostic> diagnostics)
        => new($"v{version}", Unchanged: false, diagnostics);

    public static DocumentDiagnosticsResult NotChanged(int version)
        => new($"v{version}", Unchanged: true, []);
}

internal sealed class DocumentDiagnosticsService(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    IPlatformOrchestrationService orchestration,
    ILogger<DocumentDiagnosticsService> logger) : IDocumentDiagnosticsService
{
    public async Task<DocumentDiagnosticsResult> GetAsync(Uri documentUri, string? previousResultId, CancellationToken token)
    {
        var version = VersionOf(documentUri);
        if (previousResultId == $"v{version}")
        {
            return DocumentDiagnosticsResult.NotChanged(version);
        }

        // registered capabilities decide who provides diagnostics; today that is only RDCore.Diagnostics.
        var providers = orchestration.Extensions
            .Where(extension => extension.ExtensionInfo?.Capabilities
                .Any(capability => capability.Name == nameof(DiagnoseDocument) && capability.IsSupported) == true)
            .ToArray();
        if (providers.Length == 0)
        {
            return DocumentDiagnosticsResult.Fresh(version, []);
        }

        var parseResult = await parsing.ParseDocumentAsync(documentUri, token);
        var payloadJson = PlatformJson.Serialize(new DiagnoseDocumentPayload(documentUri, version, parseResult));

        var reports = await Task.WhenAll(providers.Select(provider => AnalyzeAsync(provider, documentUri, payloadJson, token)));

        // the document moved under us while providers were running — the report would be stale, so drop it.
        var currentVersion = VersionOf(documentUri);
        if (currentVersion != version)
        {
            logger.LogInformation("🔎 {uri}: diagnostics for v{stale} dropped, document is at v{current}.",
                documentUri, version, currentVersion);
            return DocumentDiagnosticsResult.Fresh(currentVersion, []);
        }

        var aggregated = reports
            .SelectMany(report => report)
            .GroupBy(diagnostic => (diagnostic.Location, diagnostic.Code, diagnostic.Source, diagnostic.Message))
            .Select(group => group.First())
            .ToArray();

        return DocumentDiagnosticsResult.Fresh(version, aggregated);
    }

    private async Task<IReadOnlyList<PlatformDiagnostic>> AnalyzeAsync(
        IRDCoreClientApp provider, Uri documentUri, string payloadJson, CancellationToken token)
    {
        var name = provider.ExtensionInfo?.Title ?? "extension";
        try
        {
            await provider.WaitForReadyAsync(token);
            var envelope = await provider.SendRequestAsync<DiagnoseDocumentRequest, PlatformJsonEnvelope>(
                new DiagnoseDocumentRequest { Json = payloadJson }, token);

            return envelope is null ? [] : envelope.Unwrap<DiagnoseDocumentResult>().Diagnostics;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // one provider failing must not sink the others' diagnostics.
            logger.LogWarning(exception, "❌ {provider} diagnostics provider failed for {uri}.", name, documentUri);
            return [];
        }
    }

    // no DidChangeTextDocument handler yet, so the version only moves on reload/rename today; the gate
    // is in place so a future edit that bumps WorkspaceDocument.Version invalidates the next pull.
    private int VersionOf(Uri documentUri)
        => documents.GetAllDocuments().FirstOrDefault(document => document.Id.Uri.ToUri().Equals(documentUri))?.Version ?? 0;
}
