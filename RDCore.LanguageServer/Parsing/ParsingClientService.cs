using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RDCore.LanguageServer.Progress;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Progress;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;

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
    /// <remarks>
    /// Sends the document's current in-memory text (not necessarily what is saved to disk) as the
    /// request's <c>Fragment</c> — the parser never reads from the filesystem. A URI with no loaded
    /// <see cref="WorkspaceDocument"/> degrades to a failed <see cref="ModuleParseResult"/> rather than
    /// contacting the parser.
    /// </remarks>
    Task<ModuleParseResult> ParseDocumentAsync(Uri documentUri, CancellationToken token);

    /// <summary>
    /// Parses source that is not a workspace document; the last few parses are kept apart from the documents' own, by the text they are of.
    /// </summary>
    /// <remarks>
    /// Not every module a client wants parsed is a file. An interactive shell's program lives in a
    /// buffer, and its immediate-mode line is a procedure that exists for one statement's worth of
    /// time — neither is a workspace document, and neither should evict the cached parse of one.
    /// A completion list is asked of the same text at each name typed on a line - the module with
    /// the line taken out - and the text that was parsed last is answered with the very parse it was,
    /// which is what lets what is composed of it be reused too.
    /// </remarks>
    /// <param name="documentUri">The URI the parse result is addressed under.</param>
    /// <param name="source">The source to parse.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<ModuleParseResult> ParseFragmentAsync(Uri documentUri, string source, CancellationToken token);

    /// <summary>
    /// Parses every currently-loaded workspace source document. Failures are logged, not thrown.
    /// </summary>
    /// <param name="token">A token that cancels the parse.</param>
    /// <param name="progress">Where each document is reported as it is parsed.</param>
    Task ParseWorkspaceAsync(CancellationToken token, IProgress<WorkProgressStep>? progress = null);

    /// <summary>
    /// Gets the last <see cref="ModuleParseResult"/> cached for <paramref name="documentUri"/>.
    /// </summary>
    bool TryGetCached(Uri documentUri, out ModuleParseResult result);

    /// <summary>
    /// Gets the <see cref="ModuleParseResult"/> cached for <paramref name="documentUri"/> if it is a parse of the version of its text that is asked for, and not of another.
    /// </summary>
    /// <remarks>
    /// What is derived from a parse - symbols, the model of the host, diagnostics - is of one version of the text: a parse of another is of text that is not there any more.
    /// </remarks>
    bool TryGetCached(Uri documentUri, int version, out ModuleParseResult result);

    /// <summary>
    /// Forgets the parse cached for <paramref name="documentUri"/>.
    /// </summary>
    void Invalidate(Uri documentUri);

    /// <summary>
    /// Asks the parsing server for the lexical tokens of a text, and caches nothing.
    /// </summary>
    /// <param name="documentUri">The URI of the document the text is of.</param>
    /// <param name="text">The text, as it is held.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The tokens in the order they are in the text; none when the parsing server answered nothing.</returns>
    Task<IReadOnlyList<SyntaxToken>> TokenizeAsync(Uri documentUri, string text, CancellationToken token);
}

internal sealed class ParsingClientService(
    IPlatformOrchestrationService orchestration,
    IWorkspaceDocumentService documents,
    ILogger<ParsingClientService> logger) : IParsingClientService
{
    // the last parse of each document, and the version of the text it is a parse of.
    private readonly ConcurrentDictionary<Uri, (int Version, ModuleParseResult Result)> _cache = new();

    public bool TryGetCached(Uri documentUri, out ModuleParseResult result)
    {
        var found = _cache.TryGetValue(documentUri, out var cached);
        result = cached.Result;
        return found;
    }

    public bool TryGetCached(Uri documentUri, int version, out ModuleParseResult result)
    {
        var found = _cache.TryGetValue(documentUri, out var cached) && cached.Version == version;
        result = found ? cached.Result : default!;
        return found;
    }

    public void Invalidate(Uri documentUri) => _cache.TryRemove(documentUri, out _);

    public async Task<IReadOnlyList<SyntaxToken>> TokenizeAsync(Uri documentUri, string text, CancellationToken token)
    {
        await orchestration.ParsingService.WaitForReadyAsync(token);

        var result = await orchestration.ParsingService.SendRequestAsync<ParseTokensParams, ParseTokensResult>(
            new ParseTokensParams { DocumentUri = documentUri, Text = text }, token);
        return result?.Tokens ?? [];
    }

    public async Task<ModuleParseResult> ParseFragmentAsync(Uri documentUri, string source, CancellationToken token)
    {
        lock (_fragments)
        {
            var index = _fragments.FindIndex(fragment => fragment.Uri == documentUri.AbsoluteUri && fragment.Source == source);
            if (index >= 0)
            {
                var kept = _fragments[index];
                _fragments.RemoveAt(index);
                _fragments.Insert(0, kept);
                return kept.Parse;
            }
        }

        await orchestration.ParsingService.WaitForReadyAsync(token);

        var envelope = await orchestration.ParsingService.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            new ParseDocumentParams { DocumentUri = documentUri, Fragment = source }, token);

        if (envelope is null)
        {
            return ModuleParseResult.Failed(new SourceLocation(documentUri, SourceRange.Empty), "the parser returned no result");
        }

        var parse = envelope.Unwrap<ModuleParseResult>();
        lock (_fragments)
        {
            _fragments.Insert(0, (documentUri.AbsoluteUri, source, parse));
            if (_fragments.Count > KeptFragments)
            {
                _fragments.RemoveAt(_fragments.Count - 1);
            }
        }

        return parse;
    }

    // the fragments parsed last, the last first: a few, since each holds the syntax tree of a whole module.
    private const int KeptFragments = 4;
    private readonly List<(string Uri, string Source, ModuleParseResult Parse)> _fragments = [];

    public async Task<ModuleParseResult> ParseDocumentAsync(Uri documentUri, CancellationToken token)
    {
        if (!documents.TryGetDocument(documentUri, out var document))
        {
            var error = ModuleParseResult.Failed(new SourceLocation(documentUri, SourceRange.Empty),
                "no workspace document is loaded for this URI");
            _cache[documentUri] = (-1, error);
            logger.LogWarning("❌ Parse skipped for {uri}: no workspace document is loaded for it.", documentUri);
            return error;
        }

        // the text has not changed since it was parsed: so the parse has not either.
        if (TryGetCached(documentUri, document.Version, out var cached))
        {
            return cached;
        }

        // a parse of this version already asked for is waited for, not asked for again: the refresh of a keystroke and the client's pull and tokens requests
        // after it each want the same one. A parse of a later version supersedes it (the parser is told to stop), and so does every one of its callers giving
        // up.
        InFlightParse parse;
        InFlightParse? superseded = null;
        lock (_inFlight)
        {
            if (_inFlight.TryGetValue(documentUri, out var running) && running.Version == document.Version)
            {
                parse = running;
            }
            else
            {
                if (running is not null && running.Version < document.Version)
                {
                    superseded = running;
                }

                parse = new InFlightParse(document.Version);
                parse.Answer = ParseVersionAsync(documentUri, document, parse.Cancellation.Token);
                _inFlight[documentUri] = parse;
            }

            parse.Waiting++;
        }

        if (superseded is not null)
        {
            await superseded.Cancellation.CancelAsync();
        }

        try
        {
            return await parse.Answer.WaitAsync(token);
        }
        finally
        {
            var abandoned = false;
            lock (_inFlight)
            {
                if (--parse.Waiting == 0)
                {
                    abandoned = !parse.Answer.IsCompleted;
                    if (_inFlight.TryGetValue(documentUri, out var current) && ReferenceEquals(current, parse))
                    {
                        _inFlight.Remove(documentUri);
                    }
                }
            }

            if (abandoned)
            {
                await parse.Cancellation.CancelAsync();
            }
        }
    }

    // a parse asked of the parser, and how many callers wait for it.
    private sealed class InFlightParse(int version)
    {
        public int Version { get; } = version;

        public CancellationTokenSource Cancellation { get; } = new();

        public Task<ModuleParseResult> Answer { get; set; } = Task.FromResult<ModuleParseResult>(null!);

        public int Waiting { get; set; }
    }

    private readonly Dictionary<Uri, InFlightParse> _inFlight = [];

    private async Task<ModuleParseResult> ParseVersionAsync(Uri documentUri, WorkspaceDocument document, CancellationToken token)
    {
        // not under the lock it is asked for in: the request is sent once the caller has let go of it.
        await Task.Yield();
        await orchestration.ParsingService.WaitForReadyAsync(token);

        var envelope = await orchestration.ParsingService.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            new ParseDocumentParams { DocumentUri = documentUri, Fragment = ModuleSourceOf(document) }, token);

        // an error response from the parser comes back as a null envelope; degrade this one document
        // rather than abort the whole workspace parse.
        var result = envelope is not null
            ? envelope.Unwrap<ModuleParseResult>() with { Designer = DesignerOf(document) }
            : ModuleParseResult.Failed(new SourceLocation(documentUri, SourceRange.Empty), "the parser returned no result");

        // what the parser answered is the parse of the version it was sent. A failure to answer is not: it is kept under no version, so that it is what the last
        // parse was without being taken for the parse of any text, and the next ask is another try. A parse that comes back after a later one has been kept does
        // not replace it.
        var parsedVersion = envelope is not null ? document.Version : -1;
        _cache.AddOrUpdate(documentUri, (parsedVersion, result), (_, existing) => existing.Version > parsedVersion ? existing : (parsedVersion, result));
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

    public async Task ParseWorkspaceAsync(CancellationToken token, IProgress<WorkProgressStep>? progress = null)
    {
        try
        {
            await orchestration.ParsingService.WaitForReadyAsync(token);

            var parsed = 0;
            var failed = 0;
            var all = documents.GetAllDocuments().ToList();
            foreach (var document in all)
            {
                token.ThrowIfCancellationRequested();
                var uri = document.Id.Uri.ToUri();
                progress?.Report(new WorkProgressStep(
                    string.Format(ProgressMessages.ItemOf, document.Name, parsed + failed + 1, all.Count), 100 * (parsed + failed) / Math.Max(1, all.Count)));
                try
                {
                    await ParseDocumentAsync(uri, token);
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

    // what a form module's designer declares, from its text and its companion file: the parser reads text alone. A form whose designer data cannot be read
    // declares no controls; the designer's own problem is logged.
    // TODO: report an unreadable designer as a diagnostic of the form module, by its FormDesignerFormatProblem.
    private FormDescriptor? DesignerOf(WorkspaceDocument document)
    {
        try
        {
            return FormDesigner.Read(document.Text, document.Companion);
        }
        catch (FormDesignerFormatException exception)
        {
            logger.LogWarning("⚠️ The designer of {uri} cannot be read: {reason}", document.Id.Uri, exception.Message);
            return null;
        }
    }

    // what the parser is given of a document: its text, or - for a program of the platform's BASIC, which is lines and nothing around them - the module the lines are.
    internal static string ModuleSourceOf(WorkspaceDocument document)
        => BasicProgramText.IsProgram(document.Id.Uri.GetFileSystemPath()) ? BasicProgramText.ToModuleSource(document.Text) : document.Text;

    // review #170: fixed. The file extension was a stopgap; module kind is a fact of the source
    // (the VERSION header), not the file name — RD-VBA determines it the same way regardless of
    // extension (see ModuleHeader). No longer sent to the parser: it never derived the kind from
    // this hint, only echoed it onto the AST root, which was itself the wrong layer to carry it.
    internal static ModuleType ModuleTypeOf(WorkspaceDocument document)
        => ModuleHeader.IsClassModule(document.Text) == true ? ModuleType.ClassModule : ModuleType.StdModule;

    private void LogIfEnabled(LogLevel level, string message)
    {
        if (logger.IsEnabled(level))
        {
            logger.Log(level, "{message}", message);
        }
    }
}
