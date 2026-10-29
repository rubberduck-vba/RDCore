using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Progress;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Progress;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// Answers the language server's <c>textDocument/diagnostic</c> pull: parses the document, fans it
/// out to every diagnostics-provider extension over <c>rdcore/diagnostics/document</c>, aggregates
/// the results, and drops any that raced a later edit.
/// </summary>
internal interface IDocumentDiagnosticsService
{
    /// <summary>
    /// Every finding there is for a document: all the phases, awaited.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="previousResultId">The result id of the findings the client has, which are not sent again when the document has not changed since.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <param name="progress">Where the steps of the analysis are reported: the parse, the semantic model, and each provider as it answers.</param>
    /// <exception cref="OmniSharp.Extensions.JsonRpc.RpcErrorException">
    /// The document changed while its findings were being found: <c>ContentModified</c> (<see cref="IncomingDocumentChanges.ContentModified"/>). What was found
    /// is of text that is not there any more, and no report is made of it: a report would carry a result id the client could send again, and be answered
    /// <c>unchanged</c> for findings it never had.
    /// </exception>
    Task<DocumentDiagnosticsResult> GetAsync(Uri documentUri, string? previousResultId, CancellationToken token, IProgress<WorkProgressStep>? progress = null);

    /// <summary>
    /// The findings of one phase of the analysis of a document, and nothing of the others (<see cref="AnalysisPhase"/>).
    /// </summary>
    /// <remarks>
    /// The static phase is ready as soon as the host has the module and does not wait for the facts that come of evaluating its code, which the runtime phase
    /// does. A caller that can show findings as they come asks for the phases in turn and shows each as it arrives; the answer for a document that moved on while
    /// it was being found is empty, and is of the version the document is at.
    /// </remarks>
    /// <param name="documentUri">The document.</param>
    /// <param name="phase">The phase to ask for.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<DocumentDiagnosticsResult> GetPhaseAsync(Uri documentUri, AnalysisPhase phase, CancellationToken token);

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
    /// <param name="progress">Where the steps of the analysis are reported: the parse, the semantic model, and each provider as it answers.</param>
    /// <returns>The diagnostics, and how many providers answered.</returns>
    Task<(IReadOnlyList<Diagnostic> Diagnostics, int Providers)> AnalyzeFragmentAsync(Uri documentUri, string source, CancellationToken token, IProgress<WorkProgressStep>? progress = null);
}

/// <summary>
/// The outcome of a diagnostics pull. <see cref="ResultId"/> is of the version of the document, and of the workspace it is in
/// (<see cref="IWorkspaceRefresh.Generation"/>): the findings of a document that did not change change with what another module declares. When it equals the
/// client's <c>previousResultId</c> the report is <see cref="Unchanged"/> and carries no items.
/// </summary>
internal readonly record struct DocumentDiagnosticsResult(string ResultId, bool Unchanged, IReadOnlyList<Diagnostic> Diagnostics)
{
    public static DocumentDiagnosticsResult Fresh(int version, IReadOnlyList<Diagnostic> diagnostics, long generation = 0)
        => new(ResultIdOf(version, generation), Unchanged: false, diagnostics);

    public static DocumentDiagnosticsResult NotChanged(int version, long generation = 0)
        => new(ResultIdOf(version, generation), Unchanged: true, []);

    /// <summary>The result id of the findings of a document at a version, in the workspace at a generation.</summary>
    public static string ResultIdOf(int version, long generation) => $"v{version}.{generation}";
}

internal sealed class DocumentDiagnosticsService(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    IPlatformOrchestrationService orchestration,
    ISymbolSyncService symbols,
    IWorkspaceRefresh refresh,
    ILogger<DocumentDiagnosticsService> logger,
    IEditStatusMonitor? editStatus = null) : IDocumentDiagnosticsService
{
    public async Task<(IReadOnlyList<Diagnostic> Diagnostics, int Providers)> AnalyzeFragmentAsync(Uri documentUri, string source, CancellationToken token, IProgress<WorkProgressStep>? progress = null)
    {
        var providers = DiagnosticsProviders();
        if (providers.Length == 0)
        {
            return ([], 0);
        }

        progress?.Report(new WorkProgressStep(ProgressMessages.Parsing, 0));
        var parseResult = await parsing.ParseFragmentAsync(documentUri, source, token);

        // a module the client supplied has no code in the host until it is put there: defined, and loaded - which is not run - so that the host's pass
        // analyzes it and has the facts to answer for it, as it has for a module of the workspace.
        progress?.Report(new WorkProgressStep(ProgressMessages.SemanticModel, ParsedShare));
        var semantics = await AnalyzeInHostAsync(documentUri.Fragment.TrimStart('#'), parseResult, token);
        var payloadJson = PlatformJson.Serialize(new DiagnoseDocumentPayload(documentUri, 0, parseResult, semantics));
        var reports = await ProvidersAsync(providers, documentUri, payloadJson, token, progress);

        return (Aggregate(reports), providers.Length);
    }

    // how much of an analysis is done once the module is parsed, and once the semantic model is there: the providers take the rest of it, each their share.
    private const int ParsedShare = 20;
    private const int ModelShare = 50;

    // every provider asked at once, and each that answers reported: how many of them have.
    private async Task<IReadOnlyList<Diagnostic>[]> ProvidersAsync(
        IRDCoreClientApp[] providers, Uri documentUri, string payloadJson, CancellationToken token, IProgress<WorkProgressStep>? progress)
    {
        progress?.Report(new WorkProgressStep(string.Format(ProgressMessages.Providers, 0, providers.Length), ModelShare));

        var answered = 0;
        return await Task.WhenAll(providers.Select(async provider =>
        {
            var report = await AnalyzeAsync(provider, documentUri, payloadJson, token);
            var count = Interlocked.Increment(ref answered);
            progress?.Report(new WorkProgressStep(string.Format(ProgressMessages.Providers, count, providers.Length), ModelShare + (100 - ModelShare) * count / providers.Length));
            return report;
        }));
    }

    public async Task<DocumentDiagnosticsResult> GetAsync(Uri documentUri, string? previousResultId, CancellationToken token, IProgress<WorkProgressStep>? progress = null)
    {
        var document = Resolve(documentUri);
        var version = document?.Version ?? 0;
        var generation = refresh.Generation;
        if (document is null || previousResultId == DocumentDiagnosticsResult.ResultIdOf(version, generation))
        {
            return document is null
                ? DocumentDiagnosticsResult.Fresh(version, [], generation)
                : DocumentDiagnosticsResult.NotChanged(version, generation);
        }

        return await AnalyzePhaseAsync(document, AnalysisPhase.All, token, progress) ?? throw IncomingDocumentChanges.ContentModified();
    }

    public async Task<DocumentDiagnosticsResult> GetPhaseAsync(Uri documentUri, AnalysisPhase phase, CancellationToken token)
    {
        var document = Resolve(documentUri);
        return document is null
            ? DocumentDiagnosticsResult.Fresh(0, [], refresh.Generation)
            : await AnalyzePhaseAsync(document, phase, token) ?? DocumentDiagnosticsResult.Fresh(Resolve(documentUri)?.Version ?? 0, [], refresh.Generation);
    }

    // what is found of the document; nothing, when it moved on while it was being found.
    private async Task<DocumentDiagnosticsResult?> AnalyzePhaseAsync(WorkspaceDocument document, AnalysisPhase phase, CancellationToken token, IProgress<WorkProgressStep>? progress = null)
    {
        var documentUri = document.Id.Uri.ToUri();
        var version = document.Version;

        // the findings are of the workspace as it is when they are looked for: one that changes while they are is another result.
        var generation = refresh.Generation;

        // the edits a program that waits does not have are the server's own finding, which no extension is needed for; they are told once, by the static phase,
        // which a client pushed to has published before the runtime phase adds to it.
        var isHostModule = document.IsFile && (documents.WorkspaceRoot.Length == 0 || document.Projects.Any(root => WorkspacePaths.Same(root, documents.WorkspaceRoot)));
        var tellsEdits = isHostModule && phase.HasFlag(AnalysisPhase.Static);

        var providers = DiagnosticsProviders();
        if (providers.Length == 0)
        {
            var named = parsing.TryGetCached(documentUri, out var cached) ? cached.SyntaxTree?.GetDeclaredName() ?? document.Name : document.Name;
            return DocumentDiagnosticsResult.Fresh(version, tellsEdits ? InTheTextOf(document, [.. EditsNotApplied(document, named)]) : [], generation);
        }

        progress?.Report(new WorkProgressStep(ProgressMessages.Parsing, 0));
        var parseResult = await parsing.ParseDocumentAsync(documentUri, token);
        // the module is named the way the workspace sync names it when it defines it in the host. The host answers a request for the static phase at once, and one
        // for the runtime phase when it has evaluated the code: the phases are asked for apart so that what is ready is not kept waiting for what is not. A document
        // that is no file is no module the host has - one of the workspace may have its name - and is analyzed for its syntax alone.
        progress?.Report(new WorkProgressStep(ProgressMessages.SemanticModel, ParsedShare));
        // the host runs the primary project only: a module of another project has the static diagnostics, and none of what running it would find out - and its
        // name is never asked for, since the host may hold a module by that name that is another. A document that is no file is the client's alone, and none of the host's.
        var moduleName = parseResult.SyntaxTree?.GetDeclaredName() ?? document.Name;
        var semantics = isHostModule ? await SemanticsOfAsync(moduleName, phase, token) : null;
        var payloadJson = PlatformJson.Serialize(new DiagnoseDocumentPayload(documentUri, version, parseResult, semantics, phase));

        var reports = await ProvidersAsync(providers, documentUri, payloadJson, token, progress);

        // the document moved under us while providers were running — the report would be stale, so drop it.
        var currentVersion = Resolve(documentUri)?.Version ?? 0;
        if (currentVersion != version)
        {
            logger.LogInformation("🔎 {uri}: diagnostics for v{stale} dropped, document is at v{current}.",
                documentUri, version, currentVersion);
            return null;
        }

        IReadOnlyList<Diagnostic> found = tellsEdits ? [.. Aggregate(reports), .. EditsNotApplied(document, moduleName)] : Aggregate(reports);
        return DocumentDiagnosticsResult.Fresh(version, InTheTextOf(document, found), generation);
    }

    // RDC20001: each edit of the module the program that waits does not have, where the edited declaration is now - an edit of the module as a whole, or of a
    // declaration that is gone, on its first line.
    private IEnumerable<Diagnostic> EditsNotApplied(WorkspaceDocument document, string moduleName)
    {
        if (editStatus is null)
        {
            yield break;
        }

        foreach (var edit in editStatus.Current.Edits.Where(edit => string.Equals(edit.ModuleUri?.Fragment.TrimStart('#'), moduleName, StringComparison.OrdinalIgnoreCase)))
        {
            var firstLine = document.Text.Split('\n', 2)[0].TrimEnd('\r').Length;
            var range = edit.Range ?? new SourceRange(new SourcePosition(0, 0), new SourcePosition(0, firstLine));
            // the reason is the host's, in the language of the user interface.
            yield return Findings.FromAnalyzerFinding(new AnalyzerFinding(RDCoreDiagnosticId.EditAppliesOnRestart, range, DiagnosticSeverity.Information,
                string.Format(DebuggerMessages.EditNamed, edit.Name, edit.Reason)));
        }
    }

    private static readonly DiagnosticFactory Findings = new();

    // puts a module the host does not have where the host can analyze it, and asks it for what it found out: nothing when the host does not run the pass,
    // and nothing when it could not be told of the module.
    private async Task<ModuleSemanticsDto?> AnalyzeInHostAsync(string moduleName, ModuleParseResult parseResult, CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment?.PlatformInfo?.Provides<SemanticAnalysis>() != true)
        {
            return null;
        }

        // the host holds one module by this name, which the workspace may be sending its own text of (the shell's scaffold of the program is one): what is asked
        // about is what was sent only if nothing else is sent until the answer is in, the evaluation of the code included.
        await using var hold = await symbols.HoldAsync(token);
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

        return await SemanticsOfAsync(moduleName, AnalysisPhase.All, token);
    }

    // what the environment host's semantic analysis pass found out about the module, which is what an analyzer decides what to say of; nothing when the host
    // does not run the pass, has not loaded the module, or could not answer: a diagnostics extension still has the syntax tree to go on.
    private async Task<ModuleSemanticsDto?> SemanticsOfAsync(string moduleName, AnalysisPhase phase, CancellationToken token)
    {
        var host = orchestration.RuntimeEnvironment;
        if (host?.PlatformInfo?.Provides<SemanticAnalysis>() != true)
        {
            return null;
        }

        try
        {
            var result = await host.SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(new HostSemanticsParams { ModuleName = moduleName, Phase = phase }, token);
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
