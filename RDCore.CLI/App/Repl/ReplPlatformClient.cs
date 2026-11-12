using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using SemanticTokens = OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokens;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The shell's view of the platform, over the one connection it has: the language client attached to
/// the language server.
/// </summary>
/// <param name="client">The running language client — <c>rdc.exe</c>'s own LSP client app.</param>
internal sealed class ReplPlatformClient(IRDCoreClientApp client) : IReplPlatformClient
{
    /// <inheritdoc/>
    public bool Provides<TCapability>() where TCapability : CorePlatformClientCapability
        => client.PlatformInfo?.Provides<TCapability>() ?? false;

    /// <inheritdoc/>
    public Task OpenDocumentAsync(Uri document, string text, int version, CancellationToken token)
        => client.SendNotificationAsync(
            new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem { Uri = document, LanguageId = SupportedLanguages.BASIC.Id, Version = version, Text = text },
            }, token);

    /// <inheritdoc/>
    public Task ChangeDocumentAsync(Uri document, int version, string text, CancellationToken token)
        => client.SendNotificationAsync(
            new DidChangeTextDocumentParams
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = document, Version = version },
                ContentChanges = new Container<TextDocumentContentChangeEvent>(new TextDocumentContentChangeEvent { Text = text }),
            }, token);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TextEdit>> WillSaveDocumentAsync(Uri document, CancellationToken token)
    {
        await client.SendNotificationAsync(
            new WillSaveTextDocumentParams { TextDocument = new TextDocumentIdentifier(document), Reason = TextDocumentSaveReason.Manual }, token);

        var edits = await client.SendRequestAsync<WillSaveWaitUntilTextDocumentParams, TextEditContainer?>(
            new WillSaveWaitUntilTextDocumentParams { TextDocument = new TextDocumentIdentifier(document), Reason = TextDocumentSaveReason.Manual }, token);
        return edits?.ToArray() ?? [];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReplSemanticToken>> GetSemanticTokensAsync(Uri document, CancellationToken token)
    {
        var tokens = await client.SendRequestAsync<SemanticTokensParams, SemanticTokens?>(
            new SemanticTokensParams { TextDocument = new TextDocumentIdentifier(document) }, token);
        return tokens is null ? [] : ReplSemanticToken.Decode(tokens.Data);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReplFold>> GetBlockFoldsAsync(Uri document, CancellationToken token)
    {
        // a block's range has no kind; a procedure's is a region (FoldingRangeProjector).
        var ranges = await client.SendRequestAsync<FoldingRangeRequestParam, Container<FoldingRange>?>(
            new FoldingRangeRequestParam { TextDocument = new TextDocumentIdentifier(document) }, token);
        return ranges is null ? [] : [.. ranges.Where(range => range.Kind is null).Select(range => new ReplFold(range.StartLine, range.EndLine))];
    }

    /// <inheritdoc/>
    public async Task<Uri?> FindModuleAsync(string moduleName, CancellationToken token)
    {
        // a module is a symbol of its document that is declared in nothing; what a module declares is named with the module it is declared in.
        var symbols = await client.SendRequestAsync<WorkspaceSymbolParams, Container<WorkspaceSymbol>?>(new WorkspaceSymbolParams { Query = moduleName }, token);
        var module = symbols?.FirstOrDefault(symbol => symbol.ContainerName is null && string.Equals(symbol.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        return module?.Location switch
        {
            { IsLocation: true, Location: { } location } => location.Uri.ToUri(),
            { IsFileLocation: true, FileLocation: { } file } => file.Uri.ToUri(),
            _ => null,
        };
    }

    // the end of the range is a line past any program: int.MaxValue overflows the line shift of the header the program is listed under.
    private const int WholeProgramLines = 100_000;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReplInlayHint>> GetInlayHintsAsync(Uri document, CancellationToken token)
    {
        // the whole document: a program is a screenful of lines.
        var hints = await client.SendRequestAsync<InlayHintParams, InlayHintContainer?>(
            new InlayHintParams { TextDocument = new TextDocumentIdentifier(document), Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(0, 0, WholeProgramLines, 0) }, token);
        return hints is null ? [] : [.. hints.Select(hint => new ReplInlayHint(
            hint.Position.Line, hint.Position.Character,
            (hint.PaddingLeft == true ? " " : string.Empty)
                + (hint.Label.String ?? string.Concat((hint.Label.InlayHintLabelParts ?? []).Select(part => part.Value)))
                + (hint.PaddingRight == true ? " " : string.Empty)))];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Diagnostic>> PullDiagnosticsAsync(Uri document, CancellationToken token)
    {
        // the report is read as a record of its own: OmniSharp reads no RelatedDocumentDiagnosticReport (DocumentDiagnosticPullParams).
        var report = await AskAgainIfModifiedAsync(() => client.SendRequestAsync<DocumentDiagnosticPullParams, DocumentDiagnosticPullReport?>(
            new DocumentDiagnosticPullParams { TextDocument = new TextDocumentIdentifier(document) }, token));
        return report?.Items ?? [];
    }

    /// <inheritdoc/>
    public async Task<ReplDiagnosticReport?> PullDiagnosticReportAsync(Uri document, string? previousResultId, CancellationToken token)
    {
        var report = await AskAgainIfModifiedAsync(() => client.SendRequestAsync<DocumentDiagnosticPullParams, DocumentDiagnosticPullReport?>(
            new DocumentDiagnosticPullParams { TextDocument = new TextDocumentIdentifier(document), PreviousResultId = previousResultId }, token));
        return report is null ? null : new ReplDiagnosticReport(report.ResultId, string.Equals(report.Kind, "unchanged", StringComparison.Ordinal), report.Items);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(Uri document, OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range,
        IReadOnlyList<Diagnostic> diagnostics, CancellationToken token)
    {
        var actions = await AskAgainIfModifiedAsync(() => client.SendRequestAsync<CodeActionParams, CommandOrCodeActionContainer?>(
            new CodeActionParams
            {
                TextDocument = new TextDocumentIdentifier(document),
                Range = range,
                Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(diagnostics) },
            }, token));
        return actions is null ? [] : [.. actions.Where(item => item.IsCodeAction).Select(item => item.CodeAction!)];
    }

    // a request the server answers with ContentModified was overtaken by a change of the document (LSP 3.17 ErrorCodes.ContentModified), which a client asks
    // again, of the text as it is now; a few times, and then the error is the answer.
    private static async Task<T> AskAgainIfModifiedAsync<T>(Func<Task<T>> ask)
    {
        const int Attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ask();
            }
            catch (OmniSharp.Extensions.JsonRpc.Server.ContentModifiedException) when (attempt < Attempts)
            {
                await Task.Delay(100 * attempt);
            }
        }
    }

    /// <inheritdoc/>
    public Task<ReplRenameAnswer<PlaceholderRange>> PrepareRenameAsync(Uri document, Position position, CancellationToken token)
        => AnswerAsync(async () => (await client.SendRequestAsync<PrepareRenameParams, RangeOrPlaceholderRange?>(
            new PrepareRenameParams { TextDocument = new TextDocumentIdentifier(document), Position = position }, token))?.PlaceholderRange);

    /// <inheritdoc/>
    public Task<ReplRenameAnswer<WorkspaceEdit>> RenameAsync(Uri document, Position position, string newName, CancellationToken token)
        => AnswerAsync(() => client.SendRequestAsync<RenameParams, WorkspaceEdit?>(
            new RenameParams { TextDocument = new TextDocumentIdentifier(document), Position = position, NewName = newName }, token));

    // a rename the server refuses is answered with an error, whose message says why.
    private static async Task<ReplRenameAnswer<T>> AnswerAsync<T>(Func<Task<T?>> ask) where T : class
    {
        try
        {
            return new(await ask(), null);
        }
        catch (OmniSharp.Extensions.JsonRpc.Server.JsonRpcException exception)
        {
            return new(null, exception.Message);
        }
    }

    /// <inheritdoc/>
    public Task SaveDocumentAsync(Uri document, string text, CancellationToken token)
        => client.SendNotificationAsync(
            new DidSaveTextDocumentParams { TextDocument = new TextDocumentIdentifier(document), Text = text }, token);

    /// <inheritdoc/>
    public Task CloseDocumentAsync(Uri document, CancellationToken token)
        => client.SendNotificationAsync(
            new DidCloseTextDocumentParams { TextDocument = new TextDocumentIdentifier(document) }, token);

    /// <inheritdoc/>
    public Task<SessionStatusResult> GetSessionStatusAsync(int waitMilliseconds, CancellationToken token)
        => Platform.StatusAsync(new SessionStatusParams { WaitMilliseconds = waitMilliseconds }, token);

    /// <inheritdoc/>
    public Task<ExecuteResult> ExecuteAsync(string source, string moduleName, string entryPoint, CancellationToken token)
        => Platform.ExecuteAsync(new SessionExecuteParams { Source = source, ModuleName = moduleName, EntryPoint = entryPoint, SessionModule = IsProgram(moduleName) }, token);

    /// <inheritdoc/>
    public Task<DiscardResult> DiscardAsync(string moduleName, CancellationToken token)
        => Platform.DiscardAsync(new SessionDiscardParams { ModuleName = moduleName }, token);

    /// <inheritdoc/>
    public Task<DiscardResult> DiscardAsync(string moduleName, bool endProgram, CancellationToken token)
        => Platform.DiscardAsync(new SessionDiscardParams { ModuleName = moduleName, EndProgram = endProgram }, token);

    /// <inheritdoc/>
    public Task<ExecuteResult> ExecuteAsync(string source, string moduleName, string entryPoint, bool debug, bool immediate, CancellationToken token)
        => Platform.ExecuteAsync(new SessionExecuteParams
        {
            Source = source, ModuleName = moduleName, EntryPoint = entryPoint, Debug = debug, Immediate = immediate, SessionModule = IsProgram(moduleName),
        }, token);

    // the shell's program is its own module, of the session: the project of a workspace the shell is attached to does not list it (SessionExecuteParams.SessionModule).
    private static bool IsProgram(string moduleName)
        => string.Equals(moduleName, ReplProgram.ModuleName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(moduleName, ReplProgram.ImmediateModuleName, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<ExecuteResult> ResumeAsync(StepKind? step, CancellationToken token)
        => Platform.DebugResumeAsync(new SessionDebugResumeParams { Step = step }, token);

    /// <inheritdoc/>
    public Task<DebugAck> PauseAsync(CancellationToken token)
        => Platform.DebugPauseAsync(new SessionDebugPauseParams(), token);

    /// <inheritdoc/>
    public Task<DebugGotoResult> GotoAsync(string label, CancellationToken token)
        => Platform.DebugGotoAsync(new SessionDebugGotoParams { Label = label }, token);

    /// <inheritdoc/>
    public Task<DebugEditStatusResult> GetEditStatusAsync(CancellationToken token)
        => Platform.DebugEditStatusAsync(new SessionDebugEditStatusParams(), token);

    /// <inheritdoc/>
    public Task<DebugBreakpointsResult> SetBreakpointsAsync(string moduleName, IReadOnlyList<SessionBreakpointLine> breakpoints, CancellationToken token)
        => Platform.DebugBreakpointsAsync(new SessionDebugBreakpointsParams { ModuleName = moduleName, Breakpoints = breakpoints }, token);

    /// <inheritdoc/>
    public Task SetErrorBreakAsync(ErrorBreakMode mode, CancellationToken token)
        => Platform.DebugErrorBreakAsync(new SessionDebugErrorBreakParams { Mode = mode }, token);

    /// <inheritdoc/>
    public Task<DebugStackResult> GetStackAsync(CancellationToken token)
        => Platform.DebugStackAsync(new SessionDebugStackParams(), token);

    /// <inheritdoc/>
    public Task<DebugVariablesResult> GetVariablesAsync(int frameId, DebugVariableScope scope, int reference, CancellationToken token)
        => Platform.DebugVariablesAsync(new SessionDebugVariablesParams { FrameId = frameId, Scope = scope, Reference = reference }, token);

    /// <inheritdoc/>
    public Task<DebugEvaluateResult> ExecuteInFrameAsync(int frameId, string statement, CancellationToken token)
        => Platform.DebugExecuteAsync(new SessionDebugExecuteParams { FrameId = frameId, Statement = statement }, token);

    /// <inheritdoc/>
    public async Task<SessionAnalyzeResult> AnalyzeAsync(string source, string moduleName, CancellationToken token)
    {
        // the analysis reports its progress to a token of its own, which is tracked until it is answered.
        using var progress = client.TrackWorkDone();
        return await client.SendRequestAsync<SessionAnalyzeParams, SessionAnalyzeResult>(
            new SessionAnalyzeParams { Source = source, ModuleName = moduleName, WorkDoneToken = progress.Token }, token);
    }

    /// <inheritdoc/>
    public Task<CompletionList> CompleteAsync(string source, string moduleName, int line, int character, CancellationToken token, string? triggerCharacter = null)
        => client.SendRequestAsync<SessionCompletionParams, CompletionList>(
            new SessionCompletionParams { Source = source, ModuleName = moduleName, Line = line, Character = character, TriggerCharacter = triggerCharacter }, token);

    /// <inheritdoc/>
    public Task NotifyLiveSettingsChangedAsync(CancellationToken token) => client.NotifyLiveSettingsChangedAsync(token);

    /// <inheritdoc/>
    public Task<SignatureHelp?> SignatureHelpAsync(string source, string moduleName, int line, int character, CancellationToken token)
        => client.SendRequestAsync<SessionSignatureHelpParams, SignatureHelp?>(
            new SessionSignatureHelpParams { Source = source, ModuleName = moduleName, Line = line, Character = character }, token);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IReadOnlyList<ReplSemanticToken>>> GetFragmentTokensAsync(string source, string moduleName, IReadOnlyList<string> fragments, CancellationToken token)
    {
        var result = await client.SendRequestAsync<SessionSemanticTokensParams, SessionSemanticTokensResult>(
            new SessionSemanticTokensParams { Source = source, ModuleName = moduleName, Fragments = fragments }, token);
        return [.. result.Fragments.Select(fragment => ReplSemanticToken.Decode(fragment.Data))];
    }

    /// <inheritdoc/>
    public Task<SessionCommandsResult> RegisterCommandsAsync(IReadOnlyList<VerbDescriptor> verbs, CancellationToken token)
        => client.SendRequestAsync<SessionCommandsParams, SessionCommandsResult>(new SessionCommandsParams { Verbs = verbs }, token);

    /// <inheritdoc/>
    public Task<CompletionList> CompleteCommandAsync(string source, string moduleName, SessionCommandCompletion command, CancellationToken token)
        => client.SendRequestAsync<SessionCompletionParams, CompletionList>(
            new SessionCompletionParams { Source = source, ModuleName = moduleName, Command = command }, token);

    /// <inheritdoc/>
    public Task<PeekResult> PeekAsync(int address, CancellationToken token)
        => Platform.PeekAsync(new SessionPeekParams { Address = address }, token);

    /// <inheritdoc/>
    public Task<PokeResult> PokeAsync(int address, byte value, CancellationToken token)
        => Platform.PokeAsync(new SessionPokeParams { Address = address, Value = value }, token);

    // the platform protocol's requests go through the client logic every client of the platform has; the connection is the channel only once it is ready.
    private RDCorePlatformClient Platform => new(client.Channel);
}
