using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// Handles <c>rdcore/session/analyze</c>: analyzes the module a client supplied and answers with what
/// the platform's diagnostics providers found.
/// </summary>
/// <remarks>
/// The same providers an editor's <c>textDocument/diagnostic</c> pull fans out to — a client with a
/// prompt instead of an editor gets the same findings from the same place, flattened to lines it can
/// print.
/// </remarks>
internal sealed class SessionAnalyzeHandler(
    IDocumentDiagnosticsService diagnostics,
    IPlatformClientCapabilitiesService clientCapabilities,
    IOptions<SdkAppOptions> options,
    ILogger<SessionAnalyzeHandler> logger)
    : RDCoreRequestHandler<AnalyzeSessionParams, AnalyzeSessionResult>
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    protected override async Task<AnalyzeSessionResult> HandleAsync(AnalyzeSessionParams request, CancellationToken token)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.SessionAnalyze))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(SessionAnalyze)}' platform capability.");
        }

        var moduleUri = new UriBuilder(new Uri(options.Value.Workspace.WorkspaceUri)) { Fragment = request.ModuleName }.Uri;
        var (found, providers) = await diagnostics.AnalyzeFragmentAsync(moduleUri, request.Source, token);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("🔎 {module}: {count} diagnostic(s) from {providers} provider(s).",
                request.ModuleName, found.Count, providers);
        }

        return new AnalyzeSessionResult
        {
            Providers = providers,
            Diagnostics = [.. found
                .OrderBy(diagnostic => diagnostic.Range.Start.Line)
                .ThenBy(diagnostic => diagnostic.Range.Start.Character)
                .Select(Flatten)],
        };
    }

    // LSP positions are zero-based; a client that prints lines counts from one.
    private static SessionDiagnostic Flatten(Diagnostic diagnostic) => new()
    {
        Code = diagnostic.Code?.String ?? diagnostic.Code?.Long.ToString() ?? string.Empty,
        Severity = (SessionDiagnosticSeverity)(int)(diagnostic.Severity ?? DiagnosticSeverity.Error),
        Message = diagnostic.Message,
        Line = diagnostic.Range.Start.Line + 1,
        Column = diagnostic.Range.Start.Character + 1,
    };
}
