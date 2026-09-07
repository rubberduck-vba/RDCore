using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.LanguageServer.Parsing;

/// <summary>
/// Sends <c>rdcore/parser/document</c> requests to the parsing server and caches the returned
/// <see cref="ModuleParseResult"/> per document. This is the language server's only entry point to
/// the parser transport.
/// </summary>
internal interface IParsingClientService
{
    /// <summary>
    /// Parses one workspace document, waiting for the parsing server to be ready first, and caches the result.
    /// </summary>
    Task<ModuleParseResult> ParseDocumentAsync(Uri documentUri, ModuleType moduleType, CancellationToken token);

    /// <summary>
    /// Parses every currently-loaded workspace source document. Failures are logged, not thrown.
    /// </summary>
    Task ParseWorkspaceAsync(CancellationToken token);

    /// <summary>
    /// Gets the last <see cref="ModuleParseResult"/> cached for <paramref name="documentUri"/>.
    /// </summary>
    bool TryGetCached(Uri documentUri, out ModuleParseResult result);
}

internal sealed class ParsingClientService(
    IPlatformOrchestrationService orchestration,
    IWorkspaceDocumentService documents,
    ILogger<ParsingClientService> logger) : IParsingClientService
{
    private static readonly HashSet<string> _classModuleExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".cls", ".frm", ".doccls" };

    private readonly ConcurrentDictionary<Uri, ModuleParseResult> _cache = new();

    public bool TryGetCached(Uri documentUri, out ModuleParseResult result)
        => _cache.TryGetValue(documentUri, out result!);

    public async Task<ModuleParseResult> ParseDocumentAsync(Uri documentUri, ModuleType moduleType, CancellationToken token)
    {
        await orchestration.ParsingService.WaitForReadyAsync(token);

        var envelope = await orchestration.ParsingService.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            new ParseDocumentParams { DocumentUri = documentUri, ModuleType = moduleType }, token);

        // an error response from the parser comes back as a null envelope; degrade this one document
        // rather than abort the whole workspace parse.
        var result = envelope is not null
            ? envelope.Unwrap<ModuleParseResult>()
            : ModuleParseResult.Failed(new SourceLocation(documentUri, SourceRange.Empty), "the parser returned no result");

        _cache[documentUri] = result;
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("📄 Parsed {uri}: {status}", documentUri,
                result.IsSuccess ? "ok" : $"{result.SyntaxErrors.Length} syntax error(s)");
        }
        if (!result.IsSuccess && logger.IsEnabled(LogLevel.Warning))
        {
            foreach (var error in result.SyntaxErrors)
            {
                logger.LogWarning("   ↳ {detail}", error.Verbose);
            }
        }
        return result;
    }

    public async Task ParseWorkspaceAsync(CancellationToken token)
    {
        try
        {
            await orchestration.ParsingService.WaitForReadyAsync(token);

            var parsed = 0;
            var failed = 0;
            foreach (var document in documents.GetAllDocuments())
            {
                token.ThrowIfCancellationRequested();
                var uri = document.Id.Uri.ToUri();
                try
                {
                    await ParseDocumentAsync(uri, ModuleTypeOf(document), token);
                    parsed++;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // a parser failure on one module must not abort the whole workspace parse.
                    failed++;
                    logger.LogError(exception, "❌ Parse failed for {uri}.", uri);
                }
            }

            LogIfEnabled(LogLevel.Information, $"✅ Workspace parse completed ({parsed} ok, {failed} failed)");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            LogIfEnabled(LogLevel.Information, "Workspace parse was cancelled; language server is shutting down.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "❌ Workspace parse failed.");
        }
    }

    private static ModuleType ModuleTypeOf(WorkspaceDocument document)
        => _classModuleExtensions.Contains(document.Extension) ? ModuleType.ClassModule : ModuleType.StdModule;

    private void LogIfEnabled(LogLevel level, string message)
    {
        if (logger.IsEnabled(level))
        {
            logger.Log(level, "{message}", message);
        }
    }
}
