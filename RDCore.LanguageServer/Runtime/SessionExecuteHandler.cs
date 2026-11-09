using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Components;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// Handles <c>rdcore/session/execute</c>: parses the module a client supplied, defines its symbols,
/// and has the component that owns the runtime session run one procedure of it.
/// </summary>
/// <remarks>
/// Three components do the work and the client addresses none of them: the parser turns the source
/// into an AST, the environment host lowers and runs it, and the language server is what knows that
/// either exists. A parse with no syntax tree never reaches the host: there is nothing to run, and the
/// syntax errors are the answer. A parse with a syntax error does: the host refuses to run the module
/// when it loads it, unless the error is in a branch of a <c>#If</c> that is excluded, which only the
/// host knows.
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
    IWorkspaceDocumentService documents,
    IReferencedLibraryService libraries,
    ILogger<SessionExecuteHandler> logger,
    IEditStatusMonitor? editStatus = null)
    : RDCoreRequestHandler<SessionExecuteRequest, ExecuteResult>
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    protected override async Task<ExecuteResult> HandleAsync(SessionExecuteRequest request, CancellationToken token)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.SessionExecute))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(SessionExecute)}' platform capability.");
        }

        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return NotFound(DebuggerMessages.NoRuntimeEnvironment);
        }

        // a client that opened no folder has no workspace, and the host no session: what it runs has nowhere to be addressed or run.
        if (documents.WorkspaceRoot.Length == 0)
        {
            return NotFound(DebuggerMessages.NoWorkspaceToRunIn);
        }

        // the project, the projects and libraries it references and its procedural modules are matched in one tier (MS-VBAL §5.6.10): a module of the client's
        // own named VBA, or after the project, would make that name match twice, and VBA.Len, or any name qualified by it, invalid.
        if (request.SessionModule && IsTaken(request.ModuleName))
        {
            return NotFound(string.Format(DebuggerMessages.SessionModuleNameIsTaken, request.ModuleName));
        }

        // the module URI comes back from the symbol sync, which derives it from the workspace's own
        // root: the same declaration addressed two ways is the same declaration defined twice, and a
        // name that then resolves to neither.
        var moduleUri = SymbolUri.Module(options.Value.Workspace.WorkspaceUri, request.ModuleName);

        // a module with a syntax error goes to the host as well, which refuses to run it when it loads it: unless the error is in a branch of a #If that is
        // excluded, which only the host knows (RD-VBAL 2.6.1).
        var parseResult = await parsing.ParseFragmentAsync(moduleUri, request.Source, token);
        if (parseResult.SyntaxTree is null)
        {
            return new ExecuteResult
            {
                Outcome = ExecutionOutcome.SyntaxError,
                Diagnostics = [.. parseResult.SyntaxErrors.Select(error => error.Description)],
            };
        }

        // the host resolves the entry point out of its own symbol table, so the module's symbols have
        // to be defined there before it is asked to run anything.
        moduleUri = await symbols.SyncModuleAsync(request.ModuleName, parseResult, token, request.SessionModule);

        await environment.WaitForReadyAsync(token);
        var result = await environment.SendRequestAsync<HostExecuteParams, ExecuteResult>(new HostExecuteParams
        {
            Json = PlatformJson.Serialize(new HostExecutePayload(moduleUri, parseResult)),
            ModuleName = request.ModuleName,
            EntryPoint = request.EntryPoint,
            Debug = request.Debug,
            Immediate = request.Immediate,

            // nobody answers the dialogs of a client that runs unattended: a dialog fails the run.
            Unattended = clientCapabilities.Expects(capabilities => capabilities.Unattended),
        }, token);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("▶️ {module}.{entryPoint}: {outcome}", request.ModuleName, request.EntryPoint, result.Outcome);
        }

        // a program run again ends the one that waited, and has every edit: what was said of the edits that one did not have is not so any more. The host has
        // answered: a request cancelled now is not one whose answer is lost.
        if (editStatus is not null)
        {
            await editStatus.CheckAsync(CancellationToken.None);
        }

        return result;
    }

    // the name of the project, and of every library it has - the standard library, the ones it references, and the ones they depend on.
    private bool IsTaken(string moduleName)
        => string.Equals(moduleName, libraries.ProjectName ?? RDCore.SDK.Workspace.RDCoreProject.DefaultName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(moduleName, RDCore.SDK.Runtime.StdLib.StandardLibrary.LibraryName, StringComparison.OrdinalIgnoreCase)
            || libraries.Libraries.Priorities.ContainsKey(moduleName);

    private static ExecuteResult NotFound(string reason)
        => new() { Outcome = ExecutionOutcome.NotFound, ErrorMessage = reason };
}
