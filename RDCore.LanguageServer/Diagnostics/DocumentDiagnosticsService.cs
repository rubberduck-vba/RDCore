using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// Answers the language server's <c>textDocument/diagnostic</c> pull: parses the document, fans it
/// out to every diagnostics-provider extension over <c>rdcore/diagnostics/document</c>, aggregates
/// the results, and drops any that raced a later edit.
/// </summary>
internal interface IDocumentDiagnosticsService
{
    Task<DocumentDiagnosticsResult> GetAsync(Uri documentUri, string? previousResultId, CancellationToken token);

    /// <summary>
    /// Analyzes source that is not a workspace document, through the same providers.
    /// </summary>
    /// <remarks>
    /// There is no document to version, so there is no staleness gate and no result id: the caller
    /// supplied the source, so the answer cannot have raced an edit to it.
    /// </remarks>
    /// <param name="documentUri">The URI the analysis is addressed under.</param>
    /// <param name="source">The source to analyze.</param>
    /// <param name="token">A token that cancels the fan-out.</param>
    /// <returns>The diagnostics, and how many providers answered.</returns>
    Task<(IReadOnlyList<Diagnostic> Diagnostics, int Providers)> AnalyzeFragmentAsync(Uri documentUri, string source, CancellationToken token);
}

/// <summary>
/// The outcome of a diagnostics pull. <see cref="ResultId"/> tracks the workspace document version;
/// when it equals the client's <c>previousResultId</c> the report is <see cref="Unchanged"/> and
/// carries no items.
/// </summary>
internal readonly record struct DocumentDiagnosticsResult(string ResultId, bool Unchanged, IReadOnlyList<Diagnostic> Diagnostics)
{
    public static DocumentDiagnosticsResult Fresh(int version, IReadOnlyList<Diagnostic> diagnostics)
        => new($"v{version}", Unchanged: false, diagnostics);

    public static DocumentDiagnosticsResult NotChanged(int version)
        => new($"v{version}", Unchanged: true, []);
}

internal sealed class DocumentDiagnosticsService(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    IPlatformOrchestrationService orchestration,
    ISymbolSyncService symbols,
    ILogger<DocumentDiagnosticsService> logger) : IDocumentDiagnosticsService
{
    public async Task<(IReadOnlyList<Diagnostic> Diagnostics, int Providers)> AnalyzeFragmentAsync(Uri documentUri, string source, CancellationToken token)
    {
        var providers = DiagnosticsProviders();
        if (providers.Length == 0)
        {
            return ([], 0);
        }

        var parseResult = await parsing.ParseFragmentAsync(documentUri, source, token);

        // a module the client supplied has no code in the host until it is put there: defined, and loaded - which is not run - so that the host's pass
        // analyzes it and has the facts to answer for it, as it has for a module of the workspace.
        var semantics = await AnalyzeInHostAsync(documentUri.Fragment.TrimStart('#'), parseResult, token);
        var payloadJson = PlatformJson.Serialize(new DiagnoseDocumentPayload(documentUri, 0, parseResult, semantics));
        var reports = await Task.WhenAll(providers.Select(provider => AnalyzeAsync(provider, documentUri, payloadJson, token)));

        return (Aggregate(reports), providers.Length);
    }

    public async Task<DocumentDiagnosticsResult> GetAsync(Uri documentUri, string? previousResultId, CancellationToken token)
    {
        var document = Resolve(documentUri);
        var version = document?.Version ?? 0;
        if (document is null || previousResultId == $"v{version}")
        {
            return document is null
                ? DocumentDiagnosticsResult.Fresh(version, [])
                : DocumentDiagnosticsResult.NotChanged(version);
        }

        var providers = DiagnosticsProviders();
        if (providers.Length == 0)
        {
            return DocumentDiagnosticsResult.Fresh(version, []);
        }

        var parseResult = await parsing.ParseDocumentAsync(documentUri, token);
        // the module is named the way the workspace sync names it when it defines it in the host.
        var semantics = await SemanticsOfAsync(parseResult.SyntaxTree?.GetDeclaredName() ?? document.Name, token);
        var payloadJson = PlatformJson.Serialize(new DiagnoseDocumentPayload(documentUri, version, parseResult, semantics));

        var reports = await Task.WhenAll(providers.Select(provider => AnalyzeAsync(provider, documentUri, payloadJson, token)));

        // the document moved under us while providers were running — the report would be stale, so drop it.
        var currentVersion = Resolve(documentUri)?.Version ?? 0;
        if (currentVersion != version)
        {
            logger.LogInformation("🔎 {uri}: diagnostics for v{stale} dropped, document is at v{current}.",
                documentUri, version, currentVersion);
            return DocumentDiagnosticsResult.Fresh(currentVersion, []);
        }

        return DocumentDiagnosticsResult.Fresh(version, InTheTextOf(document, Aggregate(reports)));
    }

    // puts a module the host does not have where the host can analyze it, and asks it for what it found out: nothing when the host does not run the pass,
    // and nothing when it could not be told of the module.
    private async Task<ModuleSemanticsDto?> AnalyzeInHostAsync(string moduleName, ModuleParseResult parseResult, CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment?.PlatformInfo?.Provides<SemanticAnalysis>() != true)
        {
            return null;
        }

        try
        {
            await symbols.SyncModuleAsync(moduleName, parseResult, token);
            await symbols.LoadModuleCodeAsync(moduleName, parseResult, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "🔎 The environment host could not be given {module} to analyze.", moduleName);
            return null;
        }

        return await SemanticsOfAsync(moduleName, token);
    }

    // what the environment host's semantic analysis pass found out about the module, which is what an analyzer decides what to say of; nothing when the host
    // does not run the pass, has not loaded the module, or could not answer: a diagnostics extension still has the syntax tree to go on.
    private async Task<ModuleSemanticsDto?> SemanticsOfAsync(string moduleName, CancellationToken token)
    {
        var host = orchestration.RuntimeEnvironment;
        if (host?.PlatformInfo?.Provides<SemanticAnalysis>() != true)
        {
            return null;
        }

        try
        {
            var result = await host.SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(new HostSemanticsParams { ModuleName = moduleName }, token);
            return PlatformJson.Deserialize<SemanticsPayload>(result.Json).Modules.FirstOrDefault();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "🔎 The environment host could not answer for the semantics of {module}.", moduleName);
            return null;
        }
    }

    // what the providers found is where it is in the module the document is, which for a program of the BASIC has a header the text does not: the client has the text.
    private static IReadOnlyList<Diagnostic> InTheTextOf(WorkspaceDocument document, IReadOnlyList<Diagnostic> found)
        => BasicProgramText.IsProgram(document.Id.Uri.GetFileSystemPath())
            ? [.. found.Select(diagnostic => diagnostic with { Range = BasicProgramText.ToTextRange(diagnostic.Range) })]
            : found;

    // registered capabilities decide who provides diagnostics; today that is only RDCore.Diagnostics.
    private IRDCoreClientApp[] DiagnosticsProviders() => [.. orchestration.Extensions
        .Where(extension => extension.ExtensionInfo?.Capabilities
            .Any(capability => capability.Name == nameof(DiagnoseDocument) && capability.IsSupported) == true)];

    // the same finding from two providers is one finding.
    private static IReadOnlyList<Diagnostic> Aggregate(IEnumerable<IReadOnlyList<Diagnostic>> reports) => [.. reports
        .SelectMany(report => report)
        .GroupBy(diagnostic => (diagnostic.Range, diagnostic.Code, diagnostic.Source, diagnostic.Message))
        .Select(group => group.First())];

    private async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(
        IRDCoreClientApp provider, Uri documentUri, string payloadJson, CancellationToken token)
    {
        var name = provider.ExtensionInfo?.Title ?? "extension";
        try
        {
            await provider.WaitForReadyAsync(token);
            var response = await provider.SendRequestAsync<DiagnoseDocumentRequest, DiagnoseDocumentResponse>(
                new DiagnoseDocumentRequest { Json = payloadJson }, token);

            return response?.Diagnostics is { } diagnostics ? [.. diagnostics] : [];
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
    private WorkspaceDocument? Resolve(Uri documentUri)
        => documents.TryGetDocument(documentUri, out var document) ? document : null;
}
