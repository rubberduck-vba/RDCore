using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
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
    public Task SaveDocumentAsync(Uri document, string text, CancellationToken token)
        => client.SendNotificationAsync(
            new DidSaveTextDocumentParams { TextDocument = new TextDocumentIdentifier(document), Text = text }, token);

    /// <inheritdoc/>
    public Task CloseDocumentAsync(Uri document, CancellationToken token)
        => client.SendNotificationAsync(
            new DidCloseTextDocumentParams { TextDocument = new TextDocumentIdentifier(document) }, token);

    /// <inheritdoc/>
    public Task<SessionStatusResult> GetSessionStatusAsync(int waitMilliseconds, CancellationToken token)
        => client.SendRequestAsync<SessionStatusParams, SessionStatusResult>(
            new SessionStatusParams { WaitMilliseconds = waitMilliseconds }, token);

    /// <inheritdoc/>
    public Task<ExecuteSessionResult> ExecuteAsync(string source, string moduleName, string entryPoint, CancellationToken token)
        => client.SendRequestAsync<ExecuteSessionParams, ExecuteSessionResult>(
            new ExecuteSessionParams { Source = source, ModuleName = moduleName, EntryPoint = entryPoint }, token);

    /// <inheritdoc/>
    public Task<DiscardSessionResult> DiscardAsync(string moduleName, CancellationToken token)
        => client.SendRequestAsync<DiscardSessionParams, DiscardSessionResult>(new DiscardSessionParams { ModuleName = moduleName }, token);

    /// <inheritdoc/>
    public Task<AnalyzeSessionResult> AnalyzeAsync(string source, string moduleName, CancellationToken token)
        => client.SendRequestAsync<AnalyzeSessionParams, AnalyzeSessionResult>(
            new AnalyzeSessionParams { Source = source, ModuleName = moduleName }, token);

    /// <inheritdoc/>
    public Task<PeekSessionResult> PeekAsync(int address, CancellationToken token)
        => client.SendRequestAsync<PeekSessionParams, PeekSessionResult>(new PeekSessionParams { Address = address }, token);

    /// <inheritdoc/>
    public Task<PokeSessionResult> PokeAsync(int address, byte value, CancellationToken token)
        => client.SendRequestAsync<PokeSessionParams, PokeSessionResult>(
            new PokeSessionParams { Address = address, Value = value }, token);
}
