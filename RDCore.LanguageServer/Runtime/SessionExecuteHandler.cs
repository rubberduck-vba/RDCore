using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// Handles <c>rdcore/session/execute</c>: parses the module a client supplied, defines its symbols,
/// and has the component that owns the runtime session run one procedure of it.
/// </summary>
/// <remarks>
/// Three components do the work and the client addresses none of them: the parser turns the source
/// into an AST, the environment host lowers and runs it, and the language server is what knows that
/// either exists. A parse that fails never reaches the host — there is nothing to run, and the
/// syntax errors are the answer.
/// <para>
/// The handler's own cancellation token is the client's: cancelling the request cancels the request
/// to the host, which cancels the interpreter loop. That is the whole chain behind
/// <kbd>Ctrl</kbd>+<kbd>Break</kbd> stopping a program that would never stop by itself.
/// </para>
/// </remarks>
internal sealed class SessionExecuteHandler(
    IPlatformOrchestrationService orchestration,
    IPlatformClientCapabilitiesService clientCapabilities,
    IParsingClientService parsing,
    ISymbolSyncService symbols,
    IOptions<SdkAppOptions> options,
    ILogger<SessionExecuteHandler> logger)
    : RDCoreRequestHandler<ExecuteSessionParams, ExecuteSessionResult>
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    protected override async Task<ExecuteSessionResult> HandleAsync(ExecuteSessionParams request, CancellationToken token)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.SessionExecute))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(SessionExecute)}' platform capability.");
        }

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return NotFound("no runtime environment component is registered");
        }

        // the module URI comes back from the symbol sync, which derives it from the workspace's own
        // root: the same declaration addressed two ways is the same declaration defined twice, and a
        // name that then resolves to neither.
        var moduleUri = new UriBuilder(new Uri(options.Value.Workspace.WorkspaceUri)) { Fragment = request.ModuleName }.Uri;

        var parseResult = await parsing.ParseFragmentAsync(moduleUri, request.Source, token);
        if (!parseResult.IsSuccess || parseResult.SyntaxTree is null)
        {
            return new ExecuteSessionResult
            {
                Outcome = ExecutionOutcome.SyntaxError,
                Diagnostics = [.. parseResult.SyntaxErrors.Select(error => error.Description)],
            };
        }

        // the host resolves the entry point out of its own symbol table, so the module's symbols have
        // to be defined there before it is asked to run anything.
        moduleUri = await symbols.SyncModuleAsync(request.ModuleName, parseResult, token);

        await environment.WaitForReadyAsync(token);
        var result = await environment.SendRequestAsync<HostExecuteParams, ExecuteSessionResult>(new HostExecuteParams
        {
            Json = PlatformJson.Serialize(new HostExecutePayload(moduleUri, parseResult)),
            ModuleName = request.ModuleName,
            EntryPoint = request.EntryPoint,
        }, token);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("▶️ {module}.{entryPoint}: {outcome}", request.ModuleName, request.EntryPoint, result.Outcome);
        }

        return result;
    }

    private static ExecuteSessionResult NotFound(string reason)
        => new() { Outcome = ExecutionOutcome.NotFound, ErrorMessage = reason };
}
